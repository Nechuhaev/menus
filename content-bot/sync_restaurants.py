#!/usr/bin/env python3
"""
Unified restaurant + dish sync, without Google Places API (too costly — ~$230/day
at our volume). Everything now comes from the restaurant's own website, fetched once
per restaurant and reused for both restaurant-level and dish-level enrichment.

OpenStreetMap (Nominatim) is used only for: (a) GPS coordinates, and (b) a free,
best-effort check for a `website` tag. A restaurant with no website at all is never
closed automatically: it is set to Pending (hidden from the site) and logged for an admin.
An unreachable website only gets a note in the admin column - status and website stay.

Dishes: price, weight, photo and description are taken ONLY from the restaurant's own
menu page (a "menu" link on the homepage, or the homepage itself) - nothing is
generated. Description is stored in the language of the site; translation is done
separately via DeepL (translate_missing.py).

Run daily via cron. Processes a batch of restaurants per run.
"""
import html
import json
import os
import re
import socket
import copy
import difflib
import time
import uuid
from datetime import datetime
from urllib.parse import urljoin, urlparse

import boto3
import mysql.connector
import requests
from anthropic import Anthropic
from dotenv import load_dotenv

load_dotenv()

CLAUDE_API_KEY = os.environ["CLAUDE_API_KEY"]
OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY")
GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
CLOUDFLARE_ACCOUNT_ID = os.environ.get("CLOUDFLARE_ACCOUNT_ID")
CLOUDFLARE_API_TOKEN = os.environ.get("CLOUDFLARE_API_TOKEN")
GEMINI_KEY = os.environ.get("GEMINI_KEY")
DO_SPACES_KEY = os.environ["DO_SPACES_KEY"]
DO_SPACES_SECRET = os.environ["DO_SPACES_SECRET"]
DO_SPACES_BUCKET = os.environ.get("DO_SPACES_BUCKET", "cdn-tipmymenus-com")
# Порожній DO_SPACES_ENDPOINT = Amazon S3 у регіоні DO_SPACES_REGION; DO_SPACES_PUBLIC_ACL=false - без ACL (бакет за CloudFront)
DO_SPACES_ENDPOINT = os.environ.get("DO_SPACES_ENDPOINT", "https://fra1.digitaloceanspaces.com") or None
DO_SPACES_REGION = os.environ.get("DO_SPACES_REGION", "fra1")
PUT_ACL = {} if os.environ.get("DO_SPACES_PUBLIC_ACL", "true").lower() == "false" else {"ACL": "public-read"}

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

RESTAURANTS_PER_RUN = 20
RECHECK_AFTER_DAYS = 30
NOMINATIM_USER_AGENT = "TipMyMenuBot/1.0 (+https://tipmymenu.com)"

RESTAURANT_STATUS_ACTIVE = 0
RESTAURANT_STATUS_PENDING = 1
# Зіставлення страв - найбільше викликів, тож дешевша модель
CHEAP_MODEL = "claude-haiku-5-5"

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)


def _extract_json(raw_text, allow_empty_list=False):
    """Strip markdown code fences and parse JSON. Raises on empty/invalid content —
    some free-tier models return HTTP 200 with an empty or garbage body, which must
    be treated as a failure so the chain moves on to the next provider."""
    if not raw_text or not raw_text.strip():
        raise ValueError("empty response")
    cleaned = re.sub(r"```(?:json)?", "", raw_text).strip()
    if not cleaned:
        raise ValueError("empty response after stripping code fences")
    # модель іноді пише пояснення перед JSON («Це сторінка входу, меню немає: []») -
    # беремо перший JSON-масив чи об'єкт у тексті
    # («[link https://...]» у тексті перед JSON - теж «[», тому пробуємо кожну позицію)
    parsed, decoder = None, json.JSONDecoder()
    for start in (i for i, ch in enumerate(cleaned) if ch in "[{"):
        try:
            parsed, _ = decoder.raw_decode(cleaned[start:])
            break
        except json.JSONDecodeError:
            continue
    else:
        raise ValueError(f"no JSON in response: {cleaned[:80]}")
    # «[]» - законна відповідь на «випиши страви», коли сторінка не є меню
    if not parsed and not (allow_empty_list and parsed == []):
        raise ValueError("parsed to an empty JSON object/array")
    return parsed


# Усі провайдери недоступні (немає кредитів, ліміти): запуск зупиняється, ресторан не
# позначається перевіреним - інакше наступна перевірка була б лише через RECHECK_AFTER_DAYS
LLM_DOWN = False


# Витрати AI на поточний ресторан: токени й ціна в $ за прайсом моделі (за 1 млн токенів, вхід / вихід).
# Groq, Cloudflare і Gemini - безкоштовні ліміти, тож лише токени
PRICES_PER_MTOK = {
    "claude-haiku-5-5": (0.10, 0.50),
    "claude-sonnet-4-5": (3.00, 15.00),
    "anthropic/claude-sonnet-4.5": (3.00, 15.00),
}
WEB_SEARCH_USD = 0.01  # за один пошук web_search
USAGE = {"input": 0, "output": 0, "cost": 0.0}


def record_usage(model, input_tokens, output_tokens, searches=0):
    price_in, price_out = PRICES_PER_MTOK.get(model, (0.0, 0.0))
    USAGE["input"] += input_tokens or 0
    USAGE["output"] += output_tokens or 0
    USAGE["cost"] += (input_tokens or 0) * price_in / 1e6 + (output_tokens or 0) * price_out / 1e6 + searches * WEB_SEARCH_USD


def save_usage(db, restaurant_id):
    if not USAGE["input"] and not USAGE["output"]:
        return
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS AiUsageLog (
            Id INT AUTO_INCREMENT PRIMARY KEY,
            RestaurantId VARCHAR(255) NOT NULL,
            CreatedAt DATETIME NOT NULL,
            InputTokens INT NOT NULL,
            OutputTokens INT NOT NULL,
            CostUsd DECIMAL(10,4) NOT NULL,
            INDEX IX_AiUsageLog_Restaurant (RestaurantId, CreatedAt)
        )
        """
    )
    cur.execute(
        "INSERT INTO AiUsageLog (RestaurantId, CreatedAt, InputTokens, OutputTokens, CostUsd) VALUES (%s, %s, %s, %s, %s)",
        (restaurant_id, datetime.now(), USAGE["input"], USAGE["output"], round(USAGE["cost"], 4)),
    )
    db.commit()
    cur.close()


def call_llm_json(prompt, max_tokens, model="claude-sonnet-4-5", allow_empty_list=False):
    """Try Claude, then OpenRouter, then Groq, then Cloudflare Workers AI, then
    Gemini directly (Gemini last — its free tier caps at only 20 requests/day).
    Each provider's raw response is validated as usable JSON before being accepted,
    so a 200 OK with empty/garbage content doesn't get treated as success."""

    def try_claude():
        # Haiku 5.5 думає за замовчуванням - для витягування даних це зайві токени
        extra = {"thinking": {"type": "disabled"}} if model == CHEAP_MODEL else {}
        message = anthropic_client.messages.create(
            model=model,
            max_tokens=max_tokens,
            messages=[{"role": "user", "content": prompt}],
            **extra,
        )
        record_usage(model, message.usage.input_tokens, message.usage.output_tokens)
        return "".join(b.text for b in message.content if b.type == "text")

    def try_openrouter():
        resp = requests.post(
            "https://openrouter.ai/api/v1/chat/completions",
            headers={"Authorization": f"Bearer {OPENROUTER_API_KEY}", "Content-Type": "application/json"},
            json={
                "model": "anthropic/claude-sonnet-4.5",
                "messages": [{"role": "user", "content": prompt}],
                "max_tokens": max_tokens,
            },
            timeout=90,
        )
        if resp.status_code >= 400:
            print(f"    [LLM] OpenRouter HTTP {resp.status_code}: {resp.text[:300]}")
        resp.raise_for_status()
        data = resp.json()
        usage = data.get("usage") or {}
        record_usage("anthropic/claude-sonnet-4.5", usage.get("prompt_tokens"), usage.get("completion_tokens"))
        return data["choices"][0]["message"]["content"]

    def try_groq():
        resp = requests.post(
            "https://api.groq.com/openai/v1/chat/completions",
            headers={"Authorization": f"Bearer {GROQ_API_KEY}", "Content-Type": "application/json"},
            json={
                "model": "openai/gpt-oss-20b",
                "messages": [{"role": "user", "content": prompt}],
                "max_tokens": max_tokens,
            },
            timeout=60,
        )
        if resp.status_code >= 400:
            print(f"    [LLM] Groq HTTP {resp.status_code}: {resp.text[:300]}")
        resp.raise_for_status()
        return resp.json()["choices"][0]["message"]["content"]

    def try_cloudflare():
        resp = requests.post(
            f"https://api.cloudflare.com/client/v4/accounts/{CLOUDFLARE_ACCOUNT_ID}/ai/v1/chat/completions",
            headers={"Authorization": f"Bearer {CLOUDFLARE_API_TOKEN}", "Content-Type": "application/json"},
            json={
                "model": "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
                "messages": [{"role": "user", "content": prompt}],
                # контекст моделі - 24K токенів на вхід і вихід разом
                "max_tokens": min(max_tokens, 4096),
            },
            timeout=60,
        )
        if resp.status_code >= 400:
            print(f"    [LLM] Cloudflare HTTP {resp.status_code}: {resp.text[:300]}")
        resp.raise_for_status()
        return resp.json()["choices"][0]["message"]["content"]

    def try_gemini():
        from google import genai
        from google.genai import types as genai_types
        gemini_client = genai.Client(api_key=GEMINI_KEY)
        response = gemini_client.models.generate_content(
            model="gemini-3.6-flash",
            contents=prompt,
            config=genai_types.GenerateContentConfig(max_output_tokens=max_tokens + 500),
        )
        return response.text

    providers = [("Claude", try_claude)]
    if OPENROUTER_API_KEY:
        providers.append(("OpenRouter", try_openrouter))
    if GROQ_API_KEY:
        providers.append(("Groq", try_groq))
    if CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN:
        providers.append(("Cloudflare", try_cloudflare))
    if GEMINI_KEY:
        providers.append(("Gemini", try_gemini))

    last_error = None
    for i, (name, fn) in enumerate(providers):
        try:
            return _extract_json(fn(), allow_empty_list)
        except Exception as e:
            last_error = e
            next_name = providers[i + 1][0] if i + 1 < len(providers) else None
            if next_name:
                print(f"    [LLM] {name} failed, trying {next_name}: {e}")
            else:
                print(f"    [LLM] {name} failed too: {e}")

    global LLM_DOWN
    LLM_DOWN = True
    raise last_error


def get_db():
    return mysql.connector.connect(
        host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME
    )


def ensure_log_table(db):
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS RestaurantSyncLog (
            RestaurantId VARCHAR(255) PRIMARY KEY,
            LastCheckedAt DATETIME NOT NULL,
            Notes TEXT NULL,
            ClosedDetected TINYINT(1) NOT NULL DEFAULT 0
        )
        """
    )
    db.commit()
    cur.close()


def is_localizable_empty(json_str):
    if not json_str:
        return True
    try:
        parsed = json.loads(json_str)
        values = parsed.get("Values", [])
        return not any((v.get("Value") or "").strip() for v in values)
    except (json.JSONDecodeError, TypeError, AttributeError):
        return True


def get_localizable_value(json_str, locale="uk"):
    if not json_str:
        return ""
    try:
        parsed = json.loads(json_str)
        for v in parsed.get("Values", []):
            if v.get("Locale") == locale:
                return v.get("Value") or ""
    except (json.JSONDecodeError, TypeError, AttributeError):
        pass
    return ""


def localizable_json(uk_value, ru_value):
    return json.dumps(
        {"Values": [{"Locale": "uk", "Value": uk_value}, {"Locale": "ru", "Value": ru_value}]},
        ensure_ascii=False,
    )


RESTAURANT_COLUMNS = """
        SELECT r.Id, r.Name, r.City, r.Address,
               ri.Website, ri.OpenHours, ri.Contacts, ri.Photos, ri.Cuisine,
               ri.GpsLatitude, ri.GpsLongitude,
               rs.Title AS SeoTitle, rs.Description AS SeoDescription, rs.Keywords AS SeoKeywords
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id"""


def fetch_restaurants_due_for_check(db, limit):
    ensure_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        RESTAURANT_COLUMNS + """
        LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
        WHERE r.Status = %s
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY))
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RESTAURANT_STATUS_ACTIVE, RECHECK_AFTER_DAYS, limit),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def normalize_site(url):
    return re.sub(r"^https?://(www\.)?|[?#].*$", "", (url or "").strip().lower()).rstrip("/")


def fetch_chain_branches(db, restaurant):
    """Інші активні філії з тим самим сайтом: перевіряються в тому ж запуску, щоб меню мережі
    читалося один раз, а нотатки й пропозиції всіх філій були однакової свіжості."""
    site = normalize_site(restaurant["Website"])
    if not site:
        return []
    cur = db.cursor(dictionary=True)
    cur.execute(RESTAURANT_COLUMNS + " WHERE r.Status = %s AND r.Id <> %s AND ri.Website LIKE %s",
                (RESTAURANT_STATUS_ACTIVE, restaurant["Id"], "%" + site.split("/")[0] + "%"))
    rows = [r for r in cur.fetchall() if normalize_site(r["Website"]) == site]
    cur.close()
    return rows


def fetch_active_dishes(db, restaurant_id):
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT Id, Name, Description, Price, Weight, WeightType, PhotoLink, IsPriceMultiple
        FROM Dishes
        WHERE RestaurantId = %s AND Status = 0
        """,
        (restaurant_id,),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def ensure_proposals_table(db):
    # та сама схема, що в Menus.Web/Services/MenuSyncService.cs
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS MenuSyncProposals (
            Id VARCHAR(36) PRIMARY KEY,
            RestaurantId VARCHAR(255) NOT NULL,
            Kind VARCHAR(16) NOT NULL,
            DishId VARCHAR(255) NULL,
            Name TEXT NOT NULL,
            NameLang VARCHAR(2) NULL,
            Category TEXT NULL,
            Price DECIMAL(10,2) NULL,
            Weight VARCHAR(50) NULL,
            Description TEXT NULL,
            ImageUrl TEXT NULL,
            Status VARCHAR(16) NOT NULL DEFAULT 'new',
            CreatedAt DATETIME NOT NULL,
            INDEX IX_MenuSyncProposals_Restaurant (RestaurantId, Status)
        )
        """
    )
    # колонка з'явилася пізніше за таблицю - для пропозицій «змінити ціну»
    cur.execute("ALTER TABLE MenuSyncProposals ADD COLUMN IF NOT EXISTS OldPrice DECIMAL(10,2) NULL")
    db.commit()
    cur.close()


def save_proposals(db, restaurant_id, to_add, to_remove, price_changes):
    """Старі невирішені пропозиції ресторану замінюються новими; застосовує їх адмін у «Звітах»."""
    ensure_proposals_table(db)
    cur = db.cursor()
    cur.execute("DELETE FROM MenuSyncProposals WHERE RestaurantId = %s AND Status = 'new'", (restaurant_id,))
    # відхилене адміном не пропонуємо знову: та сама дія й назва (для ціни - ще й та сама нова ціна)
    cur.execute("SELECT Kind, Name, Price FROM MenuSyncProposals WHERE RestaurantId = %s AND Status = 'rejected'", (restaurant_id,))
    rejected = {(kind, match_key(name), None if kind != "price" else float(price or 0)) for kind, name, price in cur.fetchall()}
    to_add = [d for d in to_add if ("add", match_key(d["name"]), None) not in rejected]
    to_remove = [d for d in to_remove if ("remove", match_key(dish_display_name(d)), None) not in rejected]
    price_changes = [(d, p) for d, p in price_changes if ("price", match_key(dish_display_name(d)), float(p)) not in rejected]
    now = datetime.now()
    for d in to_add:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, Name, NameLang, Category, Price, Weight, Description, ImageUrl, CreatedAt)
               VALUES (%s, %s, 'add', %s, %s, %s, %s, %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, d["name"], d.get("lang"), d.get("category"), d.get("price"),
             d.get("weight"), d.get("description"), d.get("image_url"), now),
        )
    for dish in to_remove:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, DishId, Name, Price, CreatedAt)
               VALUES (%s, %s, 'remove', %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, dish["Id"], dish_display_name(dish), dish["Price"], now),
        )
    for dish, new_price in price_changes:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, DishId, Name, Price, OldPrice, CreatedAt)
               VALUES (%s, %s, 'price', %s, %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, dish["Id"], dish_display_name(dish), new_price, dish["Price"], now),
        )
    db.commit()
    cur.close()


def verify_restaurant_closure(name, address, city):
    """When no website is found anywhere (DB, OSM), use Claude with real web search
    to check whether the restaurant is actually closed, rather than concluding
    closure merely from an absent website record (OSM's website-tag coverage for
    Ukrainian restaurants is patchy — treating it as ground truth risks wrongly
    closing active restaurants)."""
    try:
        message = anthropic_client.messages.create(
            model="claude-sonnet-4-5",
            max_tokens=600,
            tools=[{"type": "web_search_20250305", "name": "web_search", "max_uses": 3}],
            messages=[{
                "role": "user",
                "content": (
                    f'Ресторан "{name}" за адресою {address}, {city}, Україна. '
                    "Пошукай в інтернеті: (1) чи є в закладу власний сайт, і якщо так — яка саме URL-адреса, "
                    "(2) чи є ознаки, що заклад остаточно закрився (позначка \"Заклад закритий\" на Google Maps, "
                    "новини про закриття, відсутність активності/відгуків останнім часом тощо). "
                    "Поверни ЛИШЕ валідний JSON, без жодного тексту до чи після:\n"
                    '{"website": "..." or null, "likely_closed": true/false, "confidence": "high"/"medium"/"low", "reason": "коротке пояснення"}'
                ),
            }],
        )
        searches = getattr(getattr(message.usage, "server_tool_use", None), "web_search_requests", 0) or 0
        record_usage("claude-sonnet-4-5", message.usage.input_tokens, message.usage.output_tokens, searches)
        text_blocks = [block.text for block in message.content if block.type == "text"]
        raw = "\n".join(text_blocks).strip()
        raw = re.sub(r"^```json\s*|\s*```$", "", raw)
        return json.loads(raw)
    except Exception as e:
        print(f"    Closure verification failed: {e}")
        return None

def osm_lookup(name, address, city):
    """Free lookup via OpenStreetMap Nominatim: GPS always if matched, plus website/
    phone/opening_hours when the OSM tags happen to have them. Rate-limited to 1 req/s
    per Nominatim's usage policy."""
    try:
        resp = requests.get(
            "https://nominatim.openstreetmap.org/search",
            params={
                "q": f"{name}, {address}, {city}",
                "format": "jsonv2",
                "extratags": 1,
                "limit": 1,
            },
            headers={"User-Agent": NOMINATIM_USER_AGENT},
            timeout=15,
        )
        resp.raise_for_status()
        results = resp.json()
        time.sleep(1)  # Nominatim usage policy: max 1 request/second
        if not results:
            return {}
        result = results[0]
        extratags = result.get("extratags") or {}
        return {
            "lat": result.get("lat"),
            "lon": result.get("lon"),
            "website": extratags.get("website") or extratags.get("contact:website"),
            "phone": extratags.get("phone") or extratags.get("contact:phone"),
            "opening_hours": extratags.get("opening_hours"),
        }
    except Exception as e:
        print(f"    OSM lookup failed: {e}")
        time.sleep(1)
        return {}


MENU_LINK = re.compile(r'<a[^>]+href=["\']([^"\'#]+)["\'][^>]*>(?:(?!</a>).){0,200}?(?:меню|menu)', re.I | re.S)


def find_menu_url(raw_html, base_url):
    """Посилання «Меню» на головній: саме меню зазвичай на окремій сторінці."""
    m = MENU_LINK.search(raw_html)
    if not m:
        return None
    url = urljoin(base_url, m.group(1))
    return url if url.startswith("http") and url.rstrip("/") != base_url.rstrip("/") else None


SOCIAL_HOSTS = ("facebook.com", "instagram.com", "fb.com", "t.me", "tiktok.com")


def is_social_page(url):
    """Сторінки соцмереж віддають боту форму входу - меню там не прочитати."""
    host = (urlparse(url or "").hostname or "").lower()
    return any(host == h or host.endswith("." + h) for h in SOCIAL_HOSTS)


def is_dead_website(url):
    """Сайт мертвий лише тоді, коли його домен не існує: DNS не знаходить хост двічі поспіль.
    404 на сторінці, 403 від захисту від ботів, тайм-аути й 5xx - не підстава ховати ресторан
    (саме через 404/403 бот помилково приховав робочі сайти)."""
    host = urlparse(url).hostname
    if not host:
        return False
    for attempt in range(2):
        try:
            socket.getaddrinfo(host, 443)
            return False
        except socket.gaierror:
            if attempt == 0:
                time.sleep(5)
    return True


BLOCK_END = re.compile(r"</(?:div|li|p|h[1-6]|tr|td|section|article|ul|ol|table)>|<br\s*/?>", re.I)
IMG_TAG = re.compile(r"<img\b[^>]*>", re.I)


def page_text(raw_html):
    """Текст сторінки для моделі: без розмітки й службових блоків, зі зображеннями як «[img URL]»
    і рядками на межах блоків. Сирий HTML у 40 000 символів закінчувався ще в шапці сторінки
    (у pesto-family.com страви починаються після ~800 КБ розмітки), і модель не бачила меню."""
    text = re.sub(r"<(script|style|noscript|svg|head|template)\b[\s\S]*?</\1>", " ", raw_html, flags=re.I)

    def img(m):
        tag = m.group(0)
        src = re.search(r'(?:data-src|data-lazy-src|src)=["\']([^"\']+)["\']', tag)
        alt = re.search(r'alt=["\']([^"\']*)["\']', tag)
        return f" [img {src.group(1)}{' ' + alt.group(1) if alt and alt.group(1) else ''}] " if src else " "

    text = IMG_TAG.sub(img, text)
    # посилання на сторінку страви: там опис і вага, яких часто немає в списку категорії
    text = re.sub(r'<a\b[^>]*href=["\']((?!#|javascript:|tel:|mailto:)[^"\']+)["\'][^>]*>', r" [link \1] ", text, flags=re.I)
    text = BLOCK_END.sub("\n", text)
    text = html.unescape(re.sub(r"<[^>]+>", " ", text))
    text = re.sub(r"[ \t\r\f\v]+", " ", text)
    return re.sub(r"\s*\n\s*", "\n", text).strip()


def fetch_website_html(url, max_chars=12000):
    try:
        resp = requests.get(
            url, timeout=15, headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"}
        )
        resp.raise_for_status()
        raw_html = resp.text
        fetch_website_html.last_menu_url = find_menu_url(raw_html, resp.url)
        fetch_website_html.last_raw = raw_html
        og_image_match = re.search(
            r'<meta[^>]+property=["\']og:image["\'][^>]+content=["\']([^"\']+)["\']', raw_html
        )
        og_image = og_image_match.group(1) if og_image_match else None

        return page_text(raw_html)[:max_chars], resp.url, og_image
    except Exception as e:
        fetch_website_html.last_menu_url = None
        fetch_website_html.last_raw = None
        print(f"    Website fetch failed for {url}: {e}")
        return None, None, None


RESTAURANT_INFO_PROMPT = """Ось HTML-фрагмент сайту ресторану "{restaurant_name}":

{html}

Витягни (тільки те, що ЯВНО є на сторінці, не вигадуй):
- години роботи
- контактний телефон
- тип кухні / короткий опис — окремо українською (cuisine_uk) і російською (cuisine_ru)

Поверни ЛИШЕ валідний JSON:
{{"opening_hours": "у форматі OSM, напр. Mo-Fr 10:00-22:00; Sa,Su 11:00-23:00" or null, "phone": "..." or null, "cuisine_uk": "..." or null, "cuisine_ru": "..." or null}}
"""


def extract_restaurant_info(restaurant_name, html):
    try:
        return call_llm_json(RESTAURANT_INFO_PROMPT.format(restaurant_name=restaurant_name, html=html), max_tokens=500,
                             model=CHEAP_MODEL)
    except Exception as e:
        print(f"    Restaurant info extraction failed: {e}")
        return {}


SEO_PROMPT = """Згенеруй SEO-метадані для сторінки ресторану на сайті tipmymenu.com.

Назва: {name}
Місто: {city}
Адреса: {address}
Кухня/опис: {cuisine}

Поверни ЛИШЕ валідний JSON, з полями окремо українською (uk) та російською (ru):
{{
  "title": {{"uk": "SEO title до 60 символів", "ru": "..."}},
  "description": {{"uk": "SEO description до 160 символів", "ru": "..."}},
  "keywords": {{"uk": "5-8 фраз через кому", "ru": "..."}}
}}
"""


def generate_seo(name, city, address, cuisine_uk, cuisine_ru):
    try:
        cuisine_text = f"{cuisine_uk or ''} / {cuisine_ru or ''}".strip(" /") or "ресторан"
        prompt = SEO_PROMPT.format(name=name, city=city, address=address, cuisine=cuisine_text)
        return call_llm_json(prompt, max_tokens=600, model=CHEAP_MODEL)
    except Exception as e:
        print(f"    SEO generation failed: {e}")
        return {}


MENU_PROMPT = """Ось текст сторінки меню ресторану "{restaurant_name}" (базовий URL: {base_url}):

{html}

Випиши ВСІ страви й напої з цієї сторінки. Для кожної:
- name: назва дослівно, як на сторінці
- lang: мова назви - "uk", "ru" або "en"
- category: розділ меню, в якому стоїть страва (як на сторінці), або null
- price: ціна числом, як на сторінці, або null
- weight: вихід/об'єм, як на сторінці (наприклад "250 г"), або null
- description: опис або склад дослівно, як на сторінці, або null
- image_url: src зображення саме цієї страви, або null
- url: посилання [link ...] на окрему сторінку саме цієї страви, або null

НЕ вигадуй нічого: лише те, що явно є на сторінці. Якщо це не меню (форма входу, помилка, головна без страв) - поверни порожній масив [].

Поверни ЛИШЕ валідний JSON-масив:
[{{"name": "...", "lang": "uk", "category": "...", "price": 0, "weight": "...", "description": "...", "image_url": "...", "url": "..."}}]
"""


# меню розпізнане впевнено лише тоді - інакше нічого не пропонуємо прибрати чи додати
MIN_SITE_DISHES = 10
MIN_MATCH_SHARE = 0.5
# велике меню на сайті при застарілій базі (Pesto: 185 страв, збіглося 35 із 236) - теж розпізнане
LARGE_MENU_DISHES = 30
LARGE_MENU_MIN_SHARE = 0.10
MAX_MENU_SUBPAGES = 25


def extract_menu(restaurant_name, base_url, html):
    try:
        items = call_llm_json(MENU_PROMPT.format(restaurant_name=restaurant_name, base_url=base_url, html=html),
                              max_tokens=16000, model=CHEAP_MODEL, allow_empty_list=True)
        return [d for d in items if isinstance(d, dict) and (d.get("name") or "").strip()]
    except Exception as e:
        print(f"    Menu extraction failed for {base_url}: {e}")
        return []


def menu_subpages(raw_html, *menu_urls):
    """Меню, розбите на сторінки розділів: вкладені (/menu/soups) і сусідні
    (/pesto-cafe/ctg/pitsa поруч із /pesto-cafe/ctg/sezonne-menju). Адрес дві - запитана
    й кінцева після редиректу: pesto-family.com веде /pesto-cafe/ctg/sezonne-menju на
    /pesto-cafe/sezonnoe-menyu/, і розділи /ctg/ видно лише від запитаної."""
    bases = {u.rstrip("/") for u in menu_urls if u}
    prefixes = set()
    for base in bases:
        prefixes.add(base + "/")
        parent = base.rsplit("/", 1)[0]
        # сусідні розділи - лише якщо батьківський шлях не корінь сайту, інакше це всі посилання сайту
        if len([p for p in urlparse(parent).path.split("/") if p]) >= 2:
            prefixes.add(parent + "/")
    page_url = menu_urls[-1] or menu_urls[0]
    found = []
    for href in re.findall(r'<a[^>]+href=["\']([^"\'#]+)["\']', raw_html or "", re.I):
        url = urljoin(page_url, href).rstrip("/")
        if url not in bases and any(url.startswith(p) for p in prefixes) and url not in found:
            found.append(url)
    return found[:MAX_MENU_SUBPAGES]


SITE_MENU_CACHE = {}  # меню мережі читається один раз за запуск, а не для кожної філії


def read_site_menu(restaurant_name, menu_url):
    """Усі страви зі сторінки меню та її підсторінок, без повторів за назвою."""
    if menu_url not in SITE_MENU_CACHE:
        SITE_MENU_CACHE[menu_url] = _read_site_menu(restaurant_name, menu_url)
    # копія: sync_menu доповнює страви описом і фото
    return copy.deepcopy(SITE_MENU_CACHE[menu_url])


def _read_site_menu(restaurant_name, menu_url):
    html, base, _ = fetch_website_html(menu_url, max_chars=40000)
    if not html:
        return []
    pages = [(html, base)]
    links = anchor_links(fetch_website_html.last_raw, base)
    for url in menu_subpages(fetch_website_html.last_raw, menu_url, base):
        sub_html, sub_base, _ = fetch_website_html(url, max_chars=40000)
        if sub_html:
            pages.append((sub_html, sub_base))
            links.update(anchor_links(fetch_website_html.last_raw, sub_base))

    menu, seen = [], set()
    for page_html, page_base in pages:
        for d in extract_menu(restaurant_name, page_base, page_html):
            key = normalize_name(d["name"])
            if key and key not in seen:
                seen.add(key)
                d["image_url"] = resolve_url(page_base, d["image_url"]) if d.get("image_url") else None
                # посилання на сторінку страви: від моделі, а якщо його немає - за текстом посилання в HTML
                d["url"] = resolve_url(page_base, d["url"]) if d.get("url") else links.get(key)
                menu.append(d)
    return menu


def anchor_links(raw_html, base_url):
    """Текст посилання -> адреса, щоб знайти сторінку страви за її назвою без AI."""
    links = {}
    for href, inner in re.findall(r'<a\b[^>]*href=["\']([^"\'#]+)["\'][^>]*>([\s\S]{0,300}?)</a>', raw_html or "", re.I):
        key = normalize_name(html.unescape(re.sub(r"<[^>]+>", " ", inner)))
        if key and key not in links and not href.startswith(("javascript:", "tel:", "mailto:")):
            links[key] = urljoin(base_url, href)
    return links


def normalize_name(value):
    return re.sub(r"[^\w]+", " ", (value or "").lower().replace("ё", "е")).strip()


def dish_display_name(dish):
    return get_localizable_value(dish["Name"], "uk") or get_localizable_value(dish["Name"], "ru") or get_localizable_value(dish["Name"], "en")


# Те, що не змінює страву: розмір, вага/об'єм, кількість, текст у дужках, «NEW»
SIZE_NOISE = re.compile(r"\([^)]*\)|\b\d+(?:[.,]\d+)?\s*(?:см|cm|г|гр|g|кг|kg|мл|ml|л|l|шт|pcs)\b\.?|\bnew\b|\bnovelty\b|\bновинка\b", re.I)


def match_key(value, noise_words=()):
    """Назва для зіставлення: без розміру, ваги, дужок і назви ресторану («Піца барбекю 30 см (Pesto)»
    -> «піца барбекю»). Для дедуплікації меню використовується normalize_name - там розміри важливі."""
    words = normalize_name(SIZE_NOISE.sub(" ", value or "")).split()
    return " ".join(w for w in words if w not in noise_words)


def match_menu(our_dishes, site_menu, restaurant_name=""):
    """Зіставлення за назвою будь-якою мовою: точний збіг або дуже схожа (ratio >= 0.88)."""
    noise = set(normalize_name(restaurant_name).split())
    site_keys = [match_key(d["name"], noise) for d in site_menu]
    matches, used = {}, set()
    for dish in our_dishes:
        names = {match_key(get_localizable_value(dish["Name"], l), noise) for l in ("uk", "ru", "en")} - {""}
        hit = next((j for j, k in enumerate(site_keys) if k in names and j not in used), None)
        if hit is None:
            best = max(((difflib.SequenceMatcher(None, n, k).ratio(), j) for n in names
                        for j, k in enumerate(site_keys) if k and j not in used), default=(0, None))
            hit = best[1] if best[0] >= 0.88 else None
        if hit is not None:
            used.add(hit)
            matches[dish["Id"]] = site_menu[hit]
    unmatched_site = [d for j, d in enumerate(site_menu) if j not in used]
    return matches, unmatched_site


# Службові слова й загальні слова-категорії не відрізняють страви: «Салат Цезар з куркою» = «Цезар з куркою»
STOP_WORDS = {"з", "із", "зі", "с", "со", "та", "і", "и", "в", "у", "на", "по", "для", "салат", "суп", "соус"}


def name_stems(value, noise_words=()):
    """Основи слів назви (перші 5 літер) без службових слів, розмірів і назви ресторану."""
    return {w[:5] for w in match_key(value, noise_words).split() if w not in STOP_WORDS and not w.isdigit()}


def pair_by_stems(our_dishes, site_menu, restaurant_name=""):
    """Друге коло зіставлення без AI: інший порядок слів чи зайве слово-категорія. Мовні відмінності
    закриває українська назва в базі (усі назви перекладено DeepL). Пара - коли основи слів збігаються
    щонайменше на 75% (Жаккар) в одній із мов і пара найкраща для обох сторін; «Піца Маргарита» і
    «Піца Маргарита з беконом» (67%) не з'єднуються."""
    noise = set(normalize_name(restaurant_name).split())
    site_stems = [name_stems(d["name"], noise) for d in site_menu]
    candidates = []
    for dish in our_dishes:
        variants = [name_stems(get_localizable_value(dish["Name"], l), noise) for l in ("uk", "ru", "en")]
        for j, theirs in enumerate(site_stems):
            score = max((len(v & theirs) / len(v | theirs) for v in variants if v and theirs), default=0)
            if score >= 0.75:
                candidates.append((score, dish["Id"], j))
    pairs, used_dishes, used_site = {}, set(), set()
    for score, dish_id, j in sorted(candidates, key=lambda c: c[0], reverse=True):
        if dish_id not in used_dishes and j not in used_site:
            pairs[dish_id] = site_menu[j]
            used_dishes.add(dish_id)
            used_site.add(j)
    return pairs


MAX_DETAIL_PAGES = 200
DETAIL_CACHE = {}  # одна сторінка страви на всі філії мережі за запуск
# Склад на сторінці страви стоїть рядком під заголовком («Інгредієнти» у pesto-family.com)
DESCRIPTION_HEADERS = {"інгредієнти", "склад", "опис", "ингредиенты", "состав", "описание", "ingredients", "description"}
# мета-опис магазину - реклама («Замовити ... за ціною 465 грн»), а не склад
SALES_WORDS = re.compile(r"замов|купи|достав|ціною|цене|заказ|order|delivery", re.I)


def description_from_text(text):
    """Перший змістовний рядок під заголовком «Інгредієнти»/«Склад»/«Опис»."""
    lines = [l.strip() for l in (text or "").split("\n")]
    for i, line in enumerate(lines):
        if line.lower().rstrip(":") in DESCRIPTION_HEADERS:
            for nxt in lines[i + 1:i + 4]:
                if len(nxt) > 10 and not nxt.startswith("[") and not SALES_WORDS.search(nxt):
                    return nxt
    return None


WEIGHT_RE = re.compile(r"(\d+(?:[.,]\d+)?)\s*(г|гр|кг|мл|л|g|kg|ml|l)\b", re.I)


def meta_content(raw_html, name):
    m = re.search(r'<meta[^>]+(?:property|name)=["\']' + re.escape(name) + r'["\'][^>]*content=["\']([^"\']*)["\']', raw_html, re.I) \
        or re.search(r'<meta[^>]+content=["\']([^"\']*)["\'][^>]*(?:property|name)=["\']' + re.escape(name) + r'["\']', raw_html, re.I)
    return html.unescape(m.group(1)).strip() if m else None


def enrich_from_detail(restaurant_name, site_dishes):
    """Опис, вага й фото зі сторінки страви - кодом, без AI: опис із og:description/description,
    фото з og:image, вага - перший «590 г» у тексті поруч із назвою."""
    fetched = 0
    for d in site_dishes:
        if not d.get("url") or (d.get("description") and d.get("weight") and d.get("image_url")):
            continue
        if d["url"] not in DETAIL_CACHE:
            if fetched >= MAX_DETAIL_PAGES:
                break
            fetched += 1
            text, base, og_image = fetch_website_html(d["url"], max_chars=20000)
            raw = fetch_website_html.last_raw or ""
            info = {}
            if text:
                desc = description_from_text(text)
                if not desc:
                    meta = meta_content(raw, "og:description") or meta_content(raw, "description")
                    if meta and len(meta) > 15 and not SALES_WORDS.search(meta):
                        desc = meta
                if desc:
                    info["description"] = desc
                pos = text.find(d["name"])
                weight = WEIGHT_RE.search(text[pos:pos + 2000] if pos >= 0 else text)
                if weight:
                    info["weight"] = f"{weight.group(1)} {weight.group(2)}"
                if og_image:
                    info["image_url"] = resolve_url(base, og_image)
            DETAIL_CACHE[d["url"]] = info
    # опис, однаковий на багатьох сторінках, - загальний текст сайту, а не склад страви
    repeated = {}
    for info in DETAIL_CACHE.values():
        if info.get("description"):
            repeated[info["description"]] = repeated.get(info["description"], 0) + 1
    for d in site_dishes:
        info = DETAIL_CACHE.get(d.get("url") or "", {})
        for key in ("description", "weight", "image_url"):
            if key == "description" and repeated.get(info.get(key), 0) > 2:
                continue
            if not d.get(key) and info.get(key):
                d[key] = info[key]


def parse_price(value):
    try:
        return round(float(str(value).replace(",", ".").replace(" ", "")), 2)
    except (TypeError, ValueError):
        return None


def resolve_url(base_url, maybe_relative):
    if maybe_relative.startswith("http"):
        return maybe_relative
    return urljoin(base_url, maybe_relative)


def upload_photo_to_spaces(image_bytes):
    from PIL import Image
    import io
    import uuid

    file_id = str(uuid.uuid4())
    source = Image.open(io.BytesIO(image_bytes)).convert("RGB")

    s3 = boto3.client(
        "s3",
        region_name=DO_SPACES_REGION,
        endpoint_url=DO_SPACES_ENDPOINT,
        aws_access_key_id=DO_SPACES_KEY,
        aws_secret_access_key=DO_SPACES_SECRET,
    )

    def upload_variant(img, key):
        buf = io.BytesIO()
        img.save(buf, format="JPEG", quality=80)
        buf.seek(0)
        s3.put_object(Bucket=DO_SPACES_BUCKET, Key=key, Body=buf.getvalue(), ContentType="image/jpeg", **PUT_ACL)

    def resized(img, max_height):
        ratio = max_height / img.height
        return img.resize((int(img.width * ratio), max_height))

    upload_variant(source, f"files/{file_id}.jpg")
    upload_variant(resized(source, 1000), f"files/{file_id}-large.jpg")
    upload_variant(resized(source, 600), f"files/{file_id}-medium.jpg")
    upload_variant(resized(source, 300), f"files/{file_id}-small.jpg")

    return file_id


def log_check(db, restaurant_id, notes, closed_detected):
    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO RestaurantSyncLog (RestaurantId, LastCheckedAt, Notes, ClosedDetected)
        VALUES (%s, %s, %s, %s)
        ON DUPLICATE KEY UPDATE LastCheckedAt = VALUES(LastCheckedAt), Notes = VALUES(Notes), ClosedDetected = VALUES(ClosedDetected)
        """,
        (restaurant_id, datetime.now(), notes, 1 if closed_detected else 0),
    )
    db.commit()
    cur.close()


def set_pending(db, restaurant_id, note):
    """Не закриваємо автоматично: Pending ховає ресторан із сайту до перевірки адміном."""
    cur = db.cursor()
    cur.execute("UPDATE Restaurants SET Status = %s WHERE Id = %s", (RESTAURANT_STATUS_PENDING, restaurant_id))
    db.commit()
    cur.close()
    log_check(db, restaurant_id, note, closed_detected=True)
    print(f"  -> {note}")


def process_restaurant(db, restaurant):
    """Обгортка: витрати AI на ресторан записуються в AiUsageLog за будь-якого результату."""
    USAGE.update(input=0, output=0, cost=0.0)
    try:
        _process_restaurant(db, restaurant)
    finally:
        try:
            save_usage(db, restaurant["Id"])
            print(f"  AI: {USAGE['input']} in / {USAGE['output']} out tokens, ${USAGE['cost']:.4f}")
        except Exception as e:
            print(f"  AI usage not saved: {e}")


def _process_restaurant(db, restaurant):
    name = restaurant["Name"]
    print(f"Restaurant: {name}")
    notes = []

    website = restaurant["Website"]
    needs_osm = not website or not restaurant["GpsLatitude"]
    osm_data = osm_lookup(name, restaurant["Address"], restaurant["City"]) if needs_osm else {}

    if not website:
        website = osm_data.get("website")

    if not website:
        verification = verify_restaurant_closure(name, restaurant["Address"], restaurant["City"])

        if verification and verification.get("website"):
            website = verification["website"]
            notes.append("website found via web search verification")
        else:
            reason = (verification or {}).get("reason", "")
            set_pending(db, restaurant["Id"], f"Pending: no website (DB/OSM/web search). {reason}".strip())
            return
    elif is_dead_website(website):
        # Статус і сайт не чіпаємо: DNS сервера не знаходив робочі домени (murakami.ua та ін.),
        # тож це лише позначка в колонці «Меню (бот)» для перевірки адміном
        log_check(db, restaurant["Id"], f"website unreachable (DNS): {website} - check manually", closed_detected=False)
        print(f"  -> website unreachable (DNS): {website}")
        return

    info_updates = {}
    if website != restaurant["Website"]:
        info_updates["Website"] = website
        notes.append("website found via OSM")

    if not restaurant["GpsLatitude"] and osm_data.get("lat"):
        info_updates["GpsLatitude"] = str(osm_data["lat"])
        info_updates["GpsLongitude"] = str(osm_data["lon"])
        notes.append("GPS filled via OSM")

    html, base_url, og_image = fetch_website_html(website)
    menu_url = fetch_website_html.last_menu_url if html else None

    site_info = {}
    if html:
        site_info = extract_restaurant_info(name, html)

    final_hours = site_info.get("opening_hours") or osm_data.get("opening_hours")
    if final_hours and final_hours != restaurant["OpenHours"]:
        info_updates["OpenHours"] = final_hours
        notes.append("hours updated")

    final_phone = site_info.get("phone") or osm_data.get("phone")
    if final_phone and not restaurant["Contacts"]:
        info_updates["Contacts"] = final_phone
        notes.append("phone filled")

    if is_localizable_empty(restaurant["Cuisine"]) and (site_info.get("cuisine_uk") or site_info.get("cuisine_ru")):
        cuisine_uk = site_info.get("cuisine_uk") or site_info.get("cuisine_ru")
        cuisine_ru = site_info.get("cuisine_ru") or site_info.get("cuisine_uk")
        info_updates["Cuisine"] = localizable_json(cuisine_uk, cuisine_ru)
        notes.append("cuisine filled from website")

    current_photos = restaurant["Photos"]
    if (not current_photos or current_photos == "[]") and og_image:
        try:
            img_resp = requests.get(resolve_url(base_url, og_image), timeout=15)
            img_resp.raise_for_status()
            file_id = upload_photo_to_spaces(img_resp.content)
            info_updates["Photos"] = json.dumps([file_id])
            notes.append("photo added from website (og:image)")
        except Exception as e:
            print(f"    Restaurant photo upload failed: {e}")

    if info_updates:
        set_clause = ", ".join(f"{col} = %s" for col in info_updates)
        cur = db.cursor()
        cur.execute(f"UPDATE RestaurantInfo SET {set_clause} WHERE RestaurantId = %s", (*info_updates.values(), restaurant["Id"]))
        db.commit()
        cur.close()

    seo_title_empty = is_localizable_empty(restaurant["SeoTitle"])
    seo_description_empty = is_localizable_empty(restaurant["SeoDescription"])
    seo_keywords_empty = not restaurant["SeoKeywords"]
    if seo_title_empty or seo_description_empty or seo_keywords_empty:
        cuisine_uk_for_seo = site_info.get("cuisine_uk") or get_localizable_value(info_updates.get("Cuisine") or restaurant["Cuisine"], "uk")
        seo_data = generate_seo(name, restaurant["City"], restaurant["Address"], cuisine_uk_for_seo, None)
        seo_updates = {}
        if seo_data.get("title") and seo_title_empty:
            seo_updates["Title"] = localizable_json(seo_data["title"]["uk"], seo_data["title"]["ru"])
        if seo_data.get("description") and seo_description_empty:
            seo_updates["Description"] = localizable_json(seo_data["description"]["uk"], seo_data["description"]["ru"])
        if seo_data.get("keywords") and seo_keywords_empty:
            seo_updates["Keywords"] = f"{seo_data['keywords']['uk']}, {seo_data['keywords']['ru']}"
        if seo_updates:
            set_clause = ", ".join(f"{col} = %s" for col in seo_updates)
            cur = db.cursor()
            cur.execute(f"UPDATE RestaurantSeo SET {set_clause} WHERE ItemId = %s", (*seo_updates.values(), restaurant["Id"]))
            db.commit()
            cur.close()
            notes.append("SEO meta filled")

    if html and is_social_page(base_url or website):
        notes.append("website is a social network page - no menu to read, add the real site in admin")
    elif html:
        notes.append(sync_menu(db, restaurant, menu_url or base_url))

    if LLM_DOWN:
        print("  -> LLM providers unavailable, restaurant left for the next run")
        return

    summary = "; ".join(notes) if notes else "no changes needed"
    log_check(db, restaurant["Id"], summary, closed_detected=False)
    print(f"  -> {summary}")


def sync_menu(db, restaurant, menu_url):
    """Дані страв - лише з меню на сайті ресторану. Збіглі страви: ціна оновлюється (якщо зміна
    не більше ніж удвічі), порожні вага/опис/фото заповнюються. Додати чи прибрати страви -
    лише пропозиції для адміна, і лише коли меню розпізнане впевнено."""
    name = restaurant["Name"]
    site_menu = read_site_menu(name, menu_url)
    our_dishes = fetch_active_dishes(db, restaurant["Id"])
    matches, unmatched_site = match_menu(our_dishes, site_menu, name)
    paired = pair_by_stems([d for d in our_dishes if d["Id"] not in matches], unmatched_site, name)
    matches.update(paired)
    unmatched_site = [d for d in unmatched_site if all(d is not p for p in paired.values())]
    # опис/вага/фото зі сторінок страв - для тих, що додаємо, і для наших без цих даних
    ours_by_id = {d["Id"]: d for d in our_dishes}
    needy = [site for dish_id, site in matches.items()
             if is_localizable_empty(ours_by_id[dish_id]["Description"]) or not ours_by_id[dish_id]["Weight"]
             or not ours_by_id[dish_id]["PhotoLink"] or ours_by_id[dish_id]["PhotoLink"] == "[]"]
    enrich_from_detail(name, needy + unmatched_site)

    updated, price_changes = 0, []
    for dish in our_dishes:
        site = matches.get(dish["Id"])
        if not site:
            continue
        updates = {}

        new_price = parse_price(site.get("price"))
        old_price = float(dish["Price"] or 0)
        if new_price and new_price != old_price and not dish["IsPriceMultiple"]:
            if old_price == 0 or 0.5 <= new_price / old_price <= 2:
                updates["Price"] = new_price
            else:
                price_changes.append((dish, new_price))  # більше ніж удвічі - на підтвердження адміну

        if not dish["Weight"] and site.get("weight"):
            updates["Weight"] = str(site["weight"])[:50]

        site_desc = (site.get("description") or "").strip()
        site_lang = site.get("lang") if site.get("lang") in ("uk", "ru", "en") else "uk"
        if site_desc and is_localizable_empty(dish["Description"]):
            # мовою сайту; інші мови допише DeepL (translate_missing.py)
            updates["Description"] = json.dumps({"Values": [{"Locale": site_lang, "Value": site_desc}]}, ensure_ascii=False)

        has_photo = dish["PhotoLink"] and dish["PhotoLink"] != "[]"
        if not has_photo and site.get("image_url"):
            try:
                img_resp = requests.get(site["image_url"], timeout=15)
                img_resp.raise_for_status()
                updates["PhotoLink"] = json.dumps([upload_photo_to_spaces(img_resp.content)])
            except Exception as e:
                print(f"    Dish photo upload failed: {e}")

        if updates:
            cur = db.cursor()
            cur.execute(f"UPDATE Dishes SET {', '.join(f'{c} = %s' for c in updates)} WHERE Id = %s", (*updates.values(), dish["Id"]))
            if "Price" in updates:
                cur.execute("UPDATE DishVariations SET Price = %s WHERE DishId = %s", (updates["Price"], dish["Id"]))
            db.commit()
            cur.close()
            updated += 1

    share = len(matches) / len(our_dishes) if our_dishes else 1
    stats = f"site {len(site_menu)}, matched {len(matches)}/{len(our_dishes)}, updated {updated}, prices to check {len(price_changes)}"
    recognized = len(site_menu) >= MIN_SITE_DISHES and (
        share >= MIN_MATCH_SHARE or (len(site_menu) >= LARGE_MENU_DISHES and share >= LARGE_MENU_MIN_SHARE))

    if not recognized:
        # ціни збіглих за назвою страв достовірні й без розпізнаного меню
        save_proposals(db, restaurant["Id"], [], [], price_changes)
        return f"menu not recognized ({stats}) at {menu_url}"

    unmatched_ours = [d for d in our_dishes if d["Id"] not in matches]
    save_proposals(db, restaurant["Id"], unmatched_site, unmatched_ours, price_changes)
    return f"menu ok ({stats}); proposals: +{len(unmatched_site)} -{len(unmatched_ours)} price {len(price_changes)}"


def main():
    db = get_db()
    try:
        restaurants = fetch_restaurants_due_for_check(db, RESTAURANTS_PER_RUN)
        if not restaurants:
            print("No restaurants due for check.")
            return

        done = set()
        for restaurant in restaurants:
            if restaurant["Id"] in done:
                continue
            # філії мережі - разом із рестораном: меню з кешу, AI на них майже не витрачається
            for item in [restaurant] + fetch_chain_branches(db, restaurant):
                if item["Id"] in done:
                    continue
                done.add(item["Id"])
                try:
                    process_restaurant(db, item)
                except Exception as e:
                    print(f"ERROR processing {item['Name']}: {e}")
                if LLM_DOWN:
                    break
            if LLM_DOWN:
                print("Stopping: all LLM providers are unavailable (credits/limits). Check API balances.")
                break
    finally:
        db.close()


if __name__ == "__main__":
    main()

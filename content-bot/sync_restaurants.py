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

AI only where code cannot do it: the menu is read by code (schema.org markup, then "name ... price" blocks of the
page text) and AI is a fallback when code finds fewer than MIN_SITE_DISHES dishes (the log says "(AI)");
hours/phone come from schema.org, tel: links and the page text; SEO meta is a template; web search via AI is
used only to check a restaurant with no website at all.

Run daily via cron. Processes a batch of restaurants per run.
One restaurant out of turn: python3 sync_restaurants.py --restaurant <page address or Id>
All restaurants whose menu was not recognized, now: python3 sync_restaurants.py --unrecognized --limit 100
"""
import html
import json
import os
import re
import signal
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

from menu_platforms import clean_description, drop_generic_images, find_platform_link, platform_menu, sure_lang

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
# фото в files/ мають унікальні імена (uuid) і не змінюються - браузер і Cloudflare тримають їх рік
PHOTO_CACHE = "public, max-age=31536000, immutable"

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

RESTAURANTS_PER_RUN = 20
RECHECK_AFTER_DAYS = 30
RECHECK_UNRECOGNIZED_DAYS = 7  # меню не розпізнане чи сайт не відповів - повтор через тиждень
NOMINATIM_USER_AGENT = "TipMyMenuBot/1.0 (+https://tipmymenu.com)"

RESTAURANT_STATUS_ACTIVE = 0
RESTAURANT_STATUS_PENDING = 1
# Зіставлення страв - найбільше викликів, тож дешевша модель
CHEAP_MODEL = "claude-haiku-5-5"
# OpenRouter: назви моделей можна змінити в .env, не чіпаючи код
OPENROUTER_MODEL = os.environ.get("OPENROUTER_MODEL", "anthropic/claude-sonnet-4.5")
OPENROUTER_CHEAP_MODEL = os.environ.get("OPENROUTER_CHEAP_MODEL", "anthropic/claude-haiku-5.5")

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
    "anthropic/claude-haiku-5.5": (0.10, 0.50),
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
        # дешева модель (читання меню, години) - і через OpenRouter дешева, а не Sonnet; якщо OpenRouter не знає
        # такої назви моделі (400/404) - один повтор із Sonnet
        models = [OPENROUTER_CHEAP_MODEL, OPENROUTER_MODEL] if model == CHEAP_MODEL else [OPENROUTER_MODEL]
        for i, or_model in enumerate(models):
            resp = requests.post(
                "https://openrouter.ai/api/v1/chat/completions",
                headers={"Authorization": f"Bearer {OPENROUTER_API_KEY}", "Content-Type": "application/json"},
                json={
                    "model": or_model,
                    "messages": [{"role": "user", "content": prompt}],
                    "max_tokens": max_tokens,
                },
                timeout=90,
            )
            if resp.status_code >= 400:
                print(f"    [LLM] OpenRouter {or_model} HTTP {resp.status_code}: {resp.text[:300]}")
                if resp.status_code in (400, 404) and i + 1 < len(models):
                    continue
            resp.raise_for_status()
            data = resp.json()
            usage = data.get("usage") or {}
            record_usage(or_model, usage.get("prompt_tokens"), usage.get("completion_tokens"))
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

    last_error, answered = None, False
    for i, (name, fn) in enumerate(providers):
        try:
            raw = fn()
        except Exception as e:
            raw, error = None, e
        else:
            try:
                return _extract_json(raw, allow_empty_list)
            except Exception as e:
                # сервіс відповів, але не JSON (PDF, сторінка-заглушка) - він працює, проблема у вхідних даних
                answered, error = True, e
        if error is not None:
            e = error
            last_error = e
            next_name = providers[i + 1][0] if i + 1 < len(providers) else None
            if next_name:
                print(f"    [LLM] {name} failed, trying {next_name}: {e}")
            else:
                print(f"    [LLM] {name} failed too: {e}")

    if not answered:
        # жоден сервіс не відповів (кредити, ліміти) - зупиняємо запуск. Якщо хтось відповів, але без JSON,
        # це проблема сторінки, а не AI: лише цей заклад/сторінка без результату
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
    # коли меню востаннє впевнено звірено з меню закладу - дата «Ціни звірено» на сторінці ресторану
    cur.execute("ALTER TABLE RestaurantSyncLog ADD COLUMN IF NOT EXISTS MenuVerifiedAt DATETIME NULL")
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
               rs.Title AS SeoTitle, rs.Description AS SeoDescription, rs.Keywords AS SeoKeywords,
               ml.Url AS MenuUrl
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
        LEFT JOIN RestaurantMenuLinks ml ON ml.RestaurantId = r.Id"""

# Посилання на меню (Expirenza, ChoiceQR тощо), яке адмін вказує в картці ресторану; таблиця спільна з сайтом
MENU_LINKS_TABLE_SQL = """CREATE TABLE IF NOT EXISTS RestaurantMenuLinks (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    Url VARCHAR(1000) NOT NULL,
    UpdatedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


# Сторінки з кліками в Search Console (оновлює thin_pages.py --gsc --save): лишаються в індексі
# навіть тонкі, а їхні ресторани бот перевіряє першими - описи й фото там дадуть найбільше
KEEP_INDEXED_TABLE_SQL = """CREATE TABLE IF NOT EXISTS SeoKeepIndexed (
    RestaurantUrl VARCHAR(255) NOT NULL,
    DishUrl VARCHAR(255) NOT NULL,
    Clicks INT NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    PRIMARY KEY (RestaurantUrl, DishUrl)
) DEFAULT CHARSET=utf8mb4"""
# кліки ресторану (сума по його сторінках) - для черги перевірки
RESTAURANT_CLICKS_JOIN = """
        LEFT JOIN (SELECT RestaurantUrl, SUM(Clicks) AS Clicks FROM SeoKeepIndexed GROUP BY RestaurantUrl) k
               ON k.RestaurantUrl = LOWER(rs.Url)"""


def fetch_restaurants_due_for_check(db, limit):
    ensure_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(KEEP_INDEXED_TABLE_SQL)
    cur.execute(
        RESTAURANT_COLUMNS + RESTAURANT_CLICKS_JOIN + """
        LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
        WHERE r.Status = %s
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY)
               -- меню не розпізнане / сайт не відповів - повтор раніше: бот читає сайти краще з кожним оновленням
               OR ((l.Notes LIKE '%%not recognized%%' OR l.Notes LIKE '%%unreachable%%' OR l.Notes LIKE '%%timeout%%')
                   AND l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY)))
        ORDER BY IFNULL(k.Clicks, 0) DESC, l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RESTAURANT_STATUS_ACTIVE, RECHECK_AFTER_DAYS, RECHECK_UNRECOGNIZED_DAYS, limit),
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
    # стара назва страви - для пропозицій «перейменувати»
    cur.execute("ALTER TABLE MenuSyncProposals ADD COLUMN IF NOT EXISTS OldName TEXT NULL")
    db.commit()
    cur.close()


def save_proposals(db, restaurant_id, to_add, to_remove, price_changes, renames=()):
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
    renames = [(dish, site) for dish, site in renames if ("rename", match_key(site["name"]), None) not in rejected]
    now = datetime.now()
    for d in to_add:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, Name, NameLang, Category, Price, Weight, Description, ImageUrl, CreatedAt)
               VALUES (%s, %s, 'add', %s, %s, %s, %s, %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, d["name"], d.get("lang"), d.get("category"), d.get("price"),
             d.get("weight"), clean_description(d.get("description")) or None, d.get("image_url"), now),
        )
    for dish in to_remove:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, DishId, Name, Price, CreatedAt)
               VALUES (%s, %s, 'remove', %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, dish["Id"], dish_display_name(dish), dish["Price"], now),
        )
    for dish, site in renames:
        cur.execute(
            """INSERT INTO MenuSyncProposals (Id, RestaurantId, Kind, DishId, Name, OldName, NameLang, Category, Price,
                                              OldPrice, Weight, Description, ImageUrl, CreatedAt)
               VALUES (%s, %s, 'rename', %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)""",
            (str(uuid.uuid4()), restaurant_id, dish["Id"], site["name"], dish_display_name(dish), site.get("lang"),
             site.get("category"), parse_price(site.get("price")), dish["Price"], site.get("weight"),
             clean_description(site.get("description")) or None, site.get("image_url"), now),
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
    """Посилання «Меню» на головній: саме меню зазвичай на окремій сторінці. Меню в
    Expirenza/ChoiceQR має перевагу - воно читається кодом, без AI."""
    platform_link = find_platform_link(raw_html)
    if platform_link:
        return platform_link
    m = MENU_LINK.search(raw_html)
    if not m:
        return None
    url = urljoin(base_url, m.group(1))
    # шаблон JS-сайту без даних («/undefined/menu», «/null/menu») - не сторінка меню
    if re.search(r"/(?:undefined|null)(?:/|$)", urlparse(url).path):
        return None
    return url if url.startswith("http") and url.rstrip("/") != base_url.rstrip("/") else None


SOCIAL_HOSTS = ("facebook.com", "instagram.com", "fb.com", "fb.me", "t.me", "tiktok.com",
                "linktr.ee", "taplink.cc", "taplink.at", "taplink.ws")
# не сайт закладу: карти, каталоги й агрегатори відгуків (sites.google.com - сайт, тож google.com лише з www/maps);
# business.site - сайти Google Business Profile, Google закрив їх у 2024 (переадресовують на карту)
NOT_SITE_HOSTS = ("www.google.com", "maps.google.com", "www.google.com.ua", "maps.google.com.ua", "goo.gl", "g.page",
                  "business.site", "tripadvisor.com", "tripadvisor.ru",
                  "restaurantguru.com", "restaurantguru.ru", "2gis.ua", "2gis.ru", "foursquare.com",
                  "tomato.ua", "glovoapp.com", "bolt.eu", "wolt.com", "youtube.com")


def host_in(url, hosts):
    host = (urlparse(url or "").hostname or "").lower()
    return any(host == h or host.endswith("." + h) for h in hosts)


def is_social_page(url):
    """Сторінки соцмереж віддають боту форму входу - меню там не прочитати."""
    return host_in(url, SOCIAL_HOSTS)


def is_not_site(url):
    """Соцмережа, карта чи каталог - не сайт закладу: у поле «Сайт» не записується."""
    return host_in(url, SOCIAL_HOSTS + NOT_SITE_HOSTS)


# Журнал прибраних сайтів (clean_websites.py): що було в полі «Сайт» і чому прибрано - для відновлення
WEBSITE_CLEANUP_TABLE_SQL = """CREATE TABLE IF NOT EXISTS WebsiteCleanupLog (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    OldWebsite VARCHAR(1000) NOT NULL,
    Reason VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    KEY IX_WebsiteCleanupLog_Restaurant (RestaurantId)
) DEFAULT CHARSET=utf8mb4"""


def website_was_cleaned(db, restaurant_id):
    cur = db.cursor()
    cur.execute(WEBSITE_CLEANUP_TABLE_SQL)
    cur.execute("SELECT 1 FROM WebsiteCleanupLog WHERE RestaurantId = %s LIMIT 1", (restaurant_id,))
    found = cur.fetchone() is not None
    cur.close()
    return found


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


DAYS_OSM = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"]
DAY_WORDS = {"пн": 0, "пон": 0, "вт": 1, "ср": 2, "чт": 3, "пт": 4, "сб": 5, "суб": 5, "нд": 6, "нед": 6, "вс": 6, "воскр": 6,
             "mon": 0, "tue": 1, "wed": 2, "thu": 3, "fri": 4, "sat": 5, "sun": 6, "mo": 0, "tu": 1, "we": 2, "th": 3,
             "fr": 4, "sa": 5, "su": 6}
DAY_RE = r"(пн|пон|вт|ср|чт|пт|сб|суб|нд|нед|вс|воскр|mon|tue|wed|thu|fri|sat|sun|mo|tu|we|th|fr|sa|su)[a-zа-яіїє]*\.?"
TIME_RE = r"(\d{1,2})[:.](\d{2})\s*[-–—]\s*(\d{1,2})[:.](\d{2})"
HOURS_RANGE = re.compile(r"(?<![a-zа-яіїє])" + DAY_RE + r"\s*[-–—]\s*" + DAY_RE + r"\s*[:,]?\s*(?:з\s*|с\s*|from\s*)?" + TIME_RE, re.I)
HOURS_DAY = re.compile(r"(?<![a-zа-яіїє])" + DAY_RE + r"\s*[:,]?\s*(?:з\s*|с\s*)?" + TIME_RE, re.I)
HOURS_DAILY = re.compile(r"(?:щодня|щоденно|ежедневно|daily|без вихідних|без выходных|графік роботи|години роботи|"
                         r"час роботи|время работы|режим роботи|режим работы|працюємо|работаем|open)[^\n\d]{0,40}" + TIME_RE, re.I)
# «10:00 - 22:00 без перерви та вихідних» - щоденний графік, слово після часу
HOURS_DAILY_AFTER = re.compile(TIME_RE + r"[^\d]{0,30}(?:щодня|щоденно|ежедневно|daily|без вихідних|без выходных|"
                               r"без перерви та вихідних|без перерыва и выходных|7/7|24/7)", re.I)
PHONE_LINK = re.compile(r'href=["\']tel:([+\d][\d\s()+-]{8,20})["\']', re.I)


def _hm(h, m):
    h = int(h)
    return f"{'24' if h == 24 else f'{h:02d}'}:{m}" if 0 <= h <= 24 else None


def _osm_days(a, b):
    return DAYS_OSM[a] if a == b else f"{DAYS_OSM[a]}-{DAYS_OSM[b]}"


def hours_from_text(text):
    """Години роботи у форматі OSM з тексту сторінки: «Пн-Пт 10:00-22:00, Сб-Нд 11:00-23:00», «Щодня 10:00-22:00».
    Не впевнені - None (тоді лишаються години з OSM чи бази)."""
    rules = []
    for m in HOURS_RANGE.finditer(text):
        a, b = DAY_WORDS[m.group(1).lower()], DAY_WORDS[m.group(2).lower()]
        start, end = _hm(m.group(3), m.group(4)), _hm(m.group(5), m.group(6))
        if start and end and a <= b and (_osm_days(a, b), start, end) not in rules:
            rules.append((_osm_days(a, b), start, end))
    if not rules:
        for m in HOURS_DAY.finditer(text):
            d = DAY_WORDS[m.group(1).lower()]
            start, end = _hm(m.group(2), m.group(3)), _hm(m.group(4), m.group(5))
            if start and end and all(r[0] != DAYS_OSM[d] for r in rules):
                rules.append((DAYS_OSM[d], start, end))
        if len(rules) < 5:  # окремі дні - лише якщо розписано майже весь тиждень
            rules = []
    if not rules:
        m = HOURS_DAILY.search(text) or HOURS_DAILY_AFTER.search(text)
        if m:
            start, end = _hm(m.group(1), m.group(2)), _hm(m.group(3), m.group(4))
            if start and end:
                rules = [("Mo-Su", start, end)]
    return "; ".join(f"{d} {a}-{b}" for d, a, b in rules) or None


def osm_to_week(text):
    """«Mo-Fr 10:00-22:00; Sa,Su 11:00-23:00» -> JSON OpenHours {"perDay":true,"data":[7 днів з понеділка]} - як
    пишуть адмінка й sync_platform_contacts.py (у полях часу картки текст OSM не показувався); None - формат невідомий."""
    by_day, off = {}, set()
    for part in (text or "").split(";"):
        closed = re.match(r"^\s*([A-Za-z,\- ]+?)\s+(?:off|closed)\s*$", part)
        if closed:
            for spec in closed.group(1).replace(" ", "").split(","):
                ends = spec.split("-")
                if any(e not in DAYS_OSM for e in ends):
                    return None
                off.update(range(DAYS_OSM.index(ends[0]), DAYS_OSM.index(ends[-1]) + 1))
            continue
        m = re.match(r"^\s*([A-Za-z,\- ]+?)\s+(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})\s*$", part)
        if not m:
            return None
        start = f"{int(m.group(2)):02d}:{m.group(3)}"
        end = "00:00" if int(m.group(4)) == 24 else f"{int(m.group(4)):02d}:{m.group(5)}"
        for spec in m.group(1).replace(" ", "").split(","):
            ends = spec.split("-")
            if any(e not in DAYS_OSM for e in ends) or len(ends) > 2:
                return None
            a, b = DAYS_OSM.index(ends[0]), DAYS_OSM.index(ends[-1])
            for d in range(a, b + 1):
                by_day[d] = (start, end)
    # невідомі дні не позначаємо вихідними: графік лише коли розписано весь тиждень (робочі + «off»)
    if not by_day or any(f == t for f, t in by_day.values()) or len(set(by_day) | off) < 7:
        return None
    return [{"from": by_day[i][0], "to": by_day[i][1], "active": True} if i in by_day
            else {"from": "", "to": "", "active": False} for i in range(7)]


def current_week(raw):
    try:
        data = json.loads(raw or "")
        return data.get("data") if isinstance(data, dict) and data.get("perDay") else None
    except ValueError:
        return None


def _jsonld_business(raw_html):
    """Перший вузол schema.org Restaurant / LocalBusiness / FoodEstablishment."""
    def walk(node):
        if isinstance(node, list):
            for x in node:
                r = walk(x)
                if r:
                    return r
        elif isinstance(node, dict):
            types = node.get("@type")
            types = types if isinstance(types, list) else [types]
            if any(t in ("Restaurant", "LocalBusiness", "FoodEstablishment", "CafeOrCoffeeShop", "BarOrPub")
                   for t in types):
                return node
            for v in node.values():
                if isinstance(v, (dict, list)):
                    r = walk(v)
                    if r:
                        return r
        return None

    for block in re.findall(r'<script[^>]+application/ld\+json[^>]*>([\s\S]*?)</script>', raw_html or "", re.I):
        try:
            found = walk(json.loads(block))
        except ValueError:
            continue
        if found:
            return found
    return {}


def extract_restaurant_info(raw_html, text):
    """Години, телефон і кухня з сайту кодом, без AI: розмітка schema.org, посилання tel:, текст сторінки."""
    info = {}
    biz = _jsonld_business(raw_html)
    hours = biz.get("openingHours")
    if isinstance(hours, list):
        hours = "; ".join(h for h in hours if isinstance(h, str))
    if isinstance(hours, str) and re.search(r"\d{1,2}:\d{2}", hours):
        info["opening_hours"] = hours.strip()
    elif isinstance(biz.get("openingHoursSpecification"), list):
        rules = []
        for spec in biz["openingHoursSpecification"]:
            days = spec.get("dayOfWeek") if isinstance(spec, dict) else None
            days = days if isinstance(days, list) else [days]
            for day in days:
                name = str(day or "").rsplit("/", 1)[-1][:2].title()
                if name in DAYS_OSM and spec.get("opens") and spec.get("closes"):
                    rules.append(f"{name} {str(spec['opens'])[:5]}-{str(spec['closes'])[:5]}")
        if rules:
            info["opening_hours"] = "; ".join(rules)
    if not info.get("opening_hours"):
        found = hours_from_text(text or "")
        if found:
            info["opening_hours"] = found
    phone = biz.get("telephone")
    if not phone:
        m = PHONE_LINK.search(raw_html or "")
        phone = m.group(1) if m else None
    if phone and len(re.sub(r"\D", "", str(phone))) >= 9:
        info["phone"] = str(phone).strip()
    cuisine = biz.get("servesCuisine")
    if isinstance(cuisine, list):
        cuisine = ", ".join(c for c in cuisine if isinstance(c, str))
    if isinstance(cuisine, str) and cuisine.strip():
        lang = sure_lang(cuisine)
        if lang == "ru":
            info["cuisine_ru"] = cuisine.strip()
        elif lang == "uk":
            info["cuisine_uk"] = cuisine.strip()
    return info


def _cut(text, limit):
    text = re.sub(r"\s+", " ", text).strip()
    return text if len(text) <= limit else text[:limit - 1].rsplit(" ", 1)[0].rstrip(",.;:- ") + "…"


def generate_seo(name, city_uk, city_ru, address, cuisine_uk, cuisine_ru):
    """SEO-заголовок, опис і ключові слова сторінки ресторану за шаблоном, без AI."""
    address = (address or "").strip()
    city_uk, city_ru = city_uk or "", city_ru or city_uk or ""
    cuisine_uk = (cuisine_uk or "").strip().rstrip(".")
    cuisine_ru = (cuisine_ru or "").strip().rstrip(".")

    def title(suffix, city):
        full = f"{name} — {suffix}, {city}" if city else f"{name} — {suffix}"
        return full if len(full) <= 60 else _cut(f"{name} — {suffix}", 60)

    where_uk = ", ".join(x for x in (city_uk, address) if x)
    where_ru = ", ".join(x for x in (city_ru, address) if x)
    return {
        "title": {"uk": title("меню з цінами", city_uk), "ru": title("меню с ценами", city_ru)},
        "description": {
            "uk": _cut(f"Меню {name}" + (f" ({cuisine_uk.lower()})" if cuisine_uk else "")
                       + ": страви з цінами, фото та оцінками гостей." + (f" {where_uk}." if where_uk else ""), 160),
            "ru": _cut(f"Меню {name}" + (f" ({cuisine_ru.lower()})" if cuisine_ru else "")
                       + ": блюда с ценами, фото и оценками гостей." + (f" {where_ru}." if where_ru else ""), 160),
        },
        "keywords": {
            "uk": ", ".join(x for x in (f"{name} меню", f"{name} ціни", f"{name} {city_uk}".strip(),
                                        f"ресторан {name}", f"{name} відгуки") if x),
            "ru": ", ".join(x for x in (f"{name} меню", f"{name} цены", f"{name} {city_ru}".strip(),
                                        f"ресторан {name}", f"{name} отзывы") if x),
        },
    }


CITY_NAMES = {}


def city_names(db, city_id):
    """(українська, російська) назва міста за Id - для SEO-шаблону."""
    if not CITY_NAMES:
        cur = db.cursor(dictionary=True)
        cur.execute("SELECT CONCAT(Id, '') AS Id, Name FROM Cities")
        for row in cur.fetchall():
            CITY_NAMES[row["Id"]] = (get_localizable_value(row["Name"], "uk"), get_localizable_value(row["Name"], "ru"))
        cur.close()
    return CITY_NAMES.get(str(city_id), ("", ""))


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
MAX_AI_PAGES = 6            # AI читає не більше стількох сторінок меню
PAGES_TIME_LIMIT = 120      # секунд на завантаження сторінок меню одного закладу
RESTAURANT_TIME_LIMIT = 300  # секунд на весь заклад, далі - «timeout», повтор через RECHECK_UNRECOGNIZED_DAYS


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


NAV_MENU_ANCHOR = re.compile(r'<a\b[^>]*>((?:(?!</a>).){1,200})</a>', re.I | re.S)


def menu_nav_links(raw_html, base_url):
    """Сторінки з випадного списку під пунктом «Меню» в навігації сайту: «Меню» часто лише заголовок (href="#"),
    а розділи - окремі сторінки (Arbequina: сніданки, основне меню, напої; 44favorite: «Меню доставки» ->
    /catalog/burgery/, /catalog/pitsa/...). Береться найбільший такий список з того самого сайту, без PDF."""
    raw_html = raw_html or ""
    host = urlparse(base_url or "").netloc
    best = []
    for m in NAV_MENU_ANCHOR.finditer(raw_html):
        text = re.sub(r"<[^>]+>|\s+", " ", m.group(1)).strip()
        if len(text) > 40 or not re.search(r"меню|menu", text, re.I):
            continue
        after = raw_html[m.end():m.end() + 400]
        ul = re.search(r"<ul\b", after, re.I)
        if not ul:
            continue
        start = m.end() + ul.start()
        end = raw_html.find("</ul>", start)
        if end < 0:
            continue
        links = []
        for href in re.findall(r'<a[^>]+href=["\']([^"\'#]+)["\']', raw_html[start:end], re.I):
            url = urljoin(base_url, href).rstrip("/")
            if urlparse(url).netloc == host and not url.lower().endswith(".pdf") and url not in links:
                links.append(url)
        if len(links) > len(best):
            best = links
    # список російською (/ru/catalog/...), а на сайті є ті самі сторінки українською - беремо українські
    all_links = {urljoin(base_url, h).rstrip("/") for h in re.findall(r'<a[^>]+href=["\']([^"\'#]+)["\']', raw_html, re.I)}
    best = [u.replace("/ru/", "/", 1) if "/ru/" in u and u.replace("/ru/", "/", 1) in all_links else u for u in best]
    return list(dict.fromkeys(best))[:MAX_MENU_SUBPAGES]


CYRILLIC = re.compile(r"[а-яёіїєґ]", re.I)


def split_bilingual(name):
    """«Оливки / Olives» -> («Оливки», «Olives»): сайти пишуть назву двома мовами через « / », а в базі вона
    одна - цілим рядком страва не зіставлялась (Arbequina: matched 0/53). Ділимо лише на дві частини, де одна
    кирилицею, а друга без неї; «Соус BRAVA / чімічурі / айолі» (варіанти) лишається як є."""
    parts = [p.strip() for p in re.split(r"\s+/\s+", name or "")]
    if len(parts) == 2 and all(parts):
        cyr = [bool(CYRILLIC.search(p)) for p in parts]
        if cyr == [True, False]:
            return parts[0], parts[1]
        if cyr == [False, True]:
            return parts[1], parts[0]
    return name, None


# --- меню зі сторінки сайту кодом, без AI -------------------------------------------------------------
# ціна з валютою: «₴750», «355.00 грн.», «2100 / 1050 ₴», «120 UAH»
PRICE_LINE = re.compile(r"^(?:₴|грн\.?|uah)?\s*(\d{1,3}(?:[  ]\d{3})+|\d{1,6})(?:[.,]\d{1,2})?\s*(?:₴|грн\.?|uah)?"
                        r"(?:\s*/\s*\d+(?:[.,]\d{1,2})?\s*(?:₴|грн\.?|uah)?)?$", re.I)
CURRENCY = re.compile(r"₴|грн|uah", re.I)
WEIGHT_LINE = re.compile(r"^\d+(?:[.,]\d+)?(?:\s*/\s*\d+(?:[.,]\d+)?)*\s*(?:г|гр|g|gr|кг|kg|мл|ml|л|l|шт|pcs)\.?$", re.I)
MARKER_LINE = re.compile(r"^\[(img|link) ([^\]\s]+)[^\]]*\]\s*(.*)$")
NOT_NAME = re.compile(r"^(?:в кошик|у кошик|до кошика|купити|замовити|додати|знижка\b.*|new!?|хіт|hit|детальніше|"
                      r"в корзину|заказать|подробнее|order|add to cart)$", re.I)


PRICE_WEIGHT_TAIL = re.compile(r"^(.*?\d.*?)\s+(\d+(?:[.,]\d+)?\s*(?:г|гр|g|gr|мл|ml)\.?(?:\s*/\s*\d+(?:[.,]\d+)?\s*(?:г|гр|g|gr|мл|ml)?\.?)*)$", re.I)


def _line_price(line):
    """(ціна, вага) з рядка ціни або (None, None). Вага буває в тому самому рядку: «₴2100 / 1050 1300г/650г»."""
    line = line.strip()
    weight = None
    tail = PRICE_WEIGHT_TAIL.match(line)
    if tail and CURRENCY.search(tail.group(1)):
        line, weight = tail.group(1).strip(), tail.group(2)
    m = PRICE_LINE.match(line) if len(line) <= 30 and CURRENCY.search(line) else None
    if not m:
        return None, None
    value = float(re.sub(r"[ \u00a0]", "", m.group(1)))
    return (value, weight) if value > 0 else (None, None)


def menu_from_text(text, base_url):
    """Страви з тексту сторінки (page_text): від кожного рядка з ціною - вгору до попередньої ціни:
    найближчий короткий рядок - назва, довгий - опис, «400 гр.» - вага, [img]/[link] - фото й сторінка страви.
    Розділ - короткий рядок без ціни перед першою стравою розділу («Тапас / Tapas»)."""
    lines = [l.strip() for l in text.split("\n") if l.strip()]
    dishes, prev_price, category, base_name = [], -1, None, None
    for i, line in enumerate(lines):
        price, weight = _line_price(line)
        if price is None:
            continue
        block = lines[max(prev_price + 1, i - 8):i]
        whole_block = i - 8 <= prev_price + 1  # блок від попередньої ціни, не обрізаний
        prev_price = i
        name = image = url = None
        desc = []
        name_idx = None
        for j in range(len(block) - 1, -1, -1):
            b = block[j]
            marker = MARKER_LINE.match(b)
            if marker:
                kind, target, rest = marker.groups()
                rest = rest.strip()
                # [link] / [img] тієї самої картки: без тексту або з назвою страви («NEW! GREEN БУРГЕР»)
                if name is not None and rest and name.lower() not in rest.lower():
                    break
                if kind == "img" and not image:
                    image = target
                if kind == "link" and not url:
                    url = target
                if not rest or name is not None:
                    continue
                b = rest
            if WEIGHT_LINE.match(b):
                weight = weight or b
            elif NOT_NAME.match(b) or not re.search(r"[^\W\d_]", b):
                continue
            elif name is None and len(b) <= 90:
                name, name_idx = b, j
            elif name is None:
                desc.insert(0, b)
            else:
                break
        if not name:
            continue
        # розділ: перший короткий рядок без цифр перед назвою («Тапас / Tapas», «Десерти»)
        # обрізаний блок (перша страва після шапки сторінки) - лише рядок безпосередньо над назвою
        for head in (block[:name_idx or 0] if whole_block else block[max((name_idx or 0) - 1, 0):name_idx or 0]):
            if MARKER_LINE.match(head) or NOT_NAME.match(head):
                continue
            if len(head) <= 40 and not re.search(r"\d", head) and name.lower() not in head.lower():
                category = split_bilingual(head)[0]
            break
        name = re.sub(r"^[-–—•]\s*", "", name)
        # варіант попередньої страви: «Домашній салат» -> «- з бринзою» = «Домашній салат з бринзою»
        if name[:1].islower() and base_name:
            name = f"{split_bilingual(base_name)[0]} {split_bilingual(name)[0]}"
        else:
            base_name = name
        dishes.append({"name": name, "lang": None, "category": category, "price": price,
                       "weight": weight, "description": " ".join(desc) or None,
                       "image_url": image, "url": url})
    return dishes


def menu_from_jsonld(raw_html):
    """Страви з розмітки schema.org (Product / MenuItem з ціною) - її ставлять магазини й частина сайтів меню."""
    found = []

    def walk(node):
        if isinstance(node, list):
            for x in node:
                walk(x)
        elif isinstance(node, dict):
            types = node.get("@type")
            types = types if isinstance(types, list) else [types]
            if any(t in ("Product", "MenuItem") for t in types) and node.get("name"):
                offers = node.get("offers") or {}
                offers = offers[0] if isinstance(offers, list) and offers else offers
                price = parse_price((offers or {}).get("price") if isinstance(offers, dict) else None)
                if price:
                    image = node.get("image")
                    image = image[0] if isinstance(image, list) and image else image
                    found.append({"name": html.unescape(str(node["name"])).strip(), "lang": None, "category": None,
                                  "price": price, "weight": None,
                                  "description": html.unescape(str(node.get("description") or "")).strip() or None,
                                  "image_url": image if isinstance(image, str) else None, "url": node.get("url")})
            for v in node.values():
                if isinstance(v, (dict, list)):
                    walk(v)

    for block in re.findall(r'<script[^>]+application/ld\+json[^>]*>([\s\S]*?)</script>', raw_html or "", re.I):
        try:
            walk(json.loads(block))
        except ValueError:
            continue
    return found


# --- меню з JSON у сторінці (сайти на Next.js/Nuxt/React: страви приходять даними, а не HTML) -------------
JSON_SCRIPT = re.compile(r'<script[^>]*type=["\']application/(?:json|ld\+json)["\'][^>]*>([\s\S]*?)</script>', re.I)
JS_STATE = re.compile(r'window\.(?:__[A-Z_]+__|initialState|INITIAL_STATE|__data)\s*=\s*(\{[\s\S]*?\})\s*;?\s*</script>', re.I)
NAME_KEYS = ("name", "title", "productName", "dishName", "label")
PRICE_KEYS = ("price", "cost", "priceValue", "price_value", "amount", "basePrice")
DESC_KEYS = ("description", "desc", "composition", "ingredients", "shortDescription")
WEIGHT_KEYS = ("weight", "portion", "volume", "output", "size")
IMAGE_KEYS = ("image", "img", "photo", "picture", "imageUrl", "image_url", "photoUrl")


def _json_value(item, keys):
    for k in keys:
        v = item.get(k)
        if isinstance(v, dict):  # {"uk": "...", "ru": "..."} чи {"amount": 120}
            v = v.get("uk") or v.get("ua") or v.get("amount") or v.get("value") or next(iter(v.values()), None)
        if isinstance(v, list) and v and isinstance(v[0], (str, dict)):
            v = v[0] if isinstance(v[0], str) else (v[0].get("url") or v[0].get("src"))
        if v not in (None, "", []):
            return v
    return None


def _json_dish(item, category):
    name = _json_value(item, NAME_KEYS)
    price = parse_price(_json_value(item, PRICE_KEYS))
    if not isinstance(name, str) or not name.strip() or not price or len(name) > 120:
        return None
    desc = _json_value(item, DESC_KEYS)
    weight = _json_value(item, WEIGHT_KEYS)
    if isinstance(desc, str):
        desc = html.unescape(re.sub(r"<[^>]+>", " ", desc)).strip()
        if WEIGHT_LINE.match(desc):  # «<p>350 г</p>» в описі - це вага
            weight, desc = weight or desc, None
    image = _json_value(item, IMAGE_KEYS)
    return {"name": html.unescape(re.sub(r"<[^>]+>", " ", name)).strip(), "lang": None, "category": category,
            "price": price, "weight": str(weight) if isinstance(weight, (int, float, str)) and weight else None,
            "description": desc if isinstance(desc, str) and desc else None,
            "image_url": image if isinstance(image, str) and re.match(r"(https?:)?//|/", image) else None, "url": None}


def menu_from_embedded_json(raw_html):
    """Страви з JSON, вбудованого в сторінку: списки об'єктів з назвою й ціною, де б вони не лежали.
    Розділ - назва батьківського об'єкта зі списком страв («{name: 'Супи', products: [...]}»).
    Ціни в копійках (усі > 5000 і кратні 100) діляться на 100."""
    blobs = []
    for text in JSON_SCRIPT.findall(raw_html or "") + JS_STATE.findall(raw_html or ""):
        try:
            blobs.append(json.loads(text.strip()))
        except ValueError:
            continue
    best = []

    def walk(node, category=None, depth=0):
        nonlocal best
        if depth > 12:
            return
        if isinstance(node, dict):
            title = _json_value(node, NAME_KEYS)
            for v in node.values():
                if isinstance(v, list):
                    dishes = [d for d in (_json_dish(x, title if isinstance(title, str) else category)
                                          for x in v if isinstance(x, dict)) if d]
                    if len(dishes) >= 3 and len(dishes) >= len(v) * 0.6:
                        best.extend(dishes)
                        continue
                if isinstance(v, (dict, list)):
                    walk(v, title if isinstance(title, str) else category, depth + 1)
        elif isinstance(node, list):
            dishes = [d for d in (_json_dish(x, category) for x in node if isinstance(x, dict)) if d]
            if len(dishes) >= 3 and len(dishes) >= len(node) * 0.6:
                best.extend(dishes)
                return
            for x in node:
                walk(x, category, depth + 1)

    for blob in blobs:
        walk(blob)
    seen, dishes = set(), []
    for d in best:
        key = (normalize_name(d["name"]), d["price"])
        if key not in seen:
            seen.add(key)
            dishes.append(d)
    if dishes and all(d["price"] > 5000 and d["price"] % 100 == 0 for d in dishes):
        for d in dishes:
            d["price"] = d["price"] / 100
    return dishes


SITE_MENU_PARTIAL = {}  # menu_url -> True: прочитано не все меню (ліміт часу / сторінок) - не пропонуємо прибирати
SITE_MENU_SOURCE = {}  # menu_url -> "code" | "AI": чим прочитано меню (для журналу)


SITE_MENU_CACHE = {}  # меню мережі читається один раз за запуск, а не для кожної філії


def read_site_menu(restaurant_name, menu_url, force_ai=False):
    """Усі страви зі сторінки меню та її підсторінок, без повторів за назвою.
    force_ai - прочитати сторінки AI, навіть якщо код щось знайшов (меню з коду не збіглося з нашим)."""
    key = (menu_url, force_ai)
    if key not in SITE_MENU_CACHE:
        SITE_MENU_CACHE[key] = _read_site_menu(restaurant_name, menu_url, force_ai)
        SITE_MENU_SOURCE[key] = SITE_MENU_SOURCE.get(menu_url)
        SITE_MENU_PARTIAL[key] = SITE_MENU_PARTIAL.pop(menu_url, False)
    SITE_MENU_SOURCE[menu_url] = SITE_MENU_SOURCE[key]
    SITE_MENU_PARTIAL[menu_url] = SITE_MENU_PARTIAL[key]
    # копія: sync_menu доповнює страви описом і фото
    return copy.deepcopy(SITE_MENU_CACHE[key])


def _read_site_menu(restaurant_name, menu_url, force_ai=False):
    qr_menu = platform_menu(menu_url)
    if qr_menu is not None:
        SITE_MENU_SOURCE[menu_url] = "QR"
        return qr_menu
    html_text, base, _ = fetch_website_html(menu_url, max_chars=400000)
    if not html_text:
        return []
    pages = [(html_text, base, fetch_website_html.last_raw)]
    links = anchor_links(fetch_website_html.last_raw, base)
    subpages = menu_subpages(fetch_website_html.last_raw, menu_url, base)
    # розділи з випадного списку «Меню» (на сторінці меню може бути лише PDF, а страви - у «Меню доставки»)
    for url in menu_nav_links(fetch_website_html.last_raw, base):
        if url not in subpages and url.rstrip("/") not in (menu_url.rstrip("/"), (base or "").rstrip("/")):
            subpages.append(url)
    started = time.time()
    SITE_MENU_PARTIAL[menu_url] = len(subpages) > MAX_MENU_SUBPAGES
    subpages = [u for u in subpages if not re.search(r"\.pdf$", urlparse(u).path, re.I)]
    for url in subpages[:MAX_MENU_SUBPAGES]:
        if time.time() - started > PAGES_TIME_LIMIT:
            SITE_MENU_PARTIAL[menu_url] = True
            print(f"    menu pages: time limit {PAGES_TIME_LIMIT}s, read {len(pages)} of {len(subpages) + 1}")
            break
        # повний текст сторінки для коду; AI (якщо знадобиться) отримає перші 40 000 символів
        sub_html, sub_base, _ = fetch_website_html(url, max_chars=400000)
        if sub_html:
            pages.append((sub_html, sub_base, fetch_website_html.last_raw))
            links.update(anchor_links(fetch_website_html.last_raw, sub_base))

    # спершу кодом: розмітка schema.org, потім «назва ... ціна» в тексті; AI - лише якщо код майже нічого не знайшов
    by_code = []
    for page_html, page_base, raw in pages:
        # кодом: розмітка schema.org, JSON у сторінці (Next.js/Nuxt), потім «назва ... ціна» в тексті
        page_items = menu_from_jsonld(raw) or menu_from_embedded_json(raw) or menu_from_text(page_html, page_base)
        # сторінка = розділ меню (44favorite: /catalog/burgery/) - розділ із заголовка сторінки
        h1 = re.search(r"<h1\b[^>]*>([\s\S]*?)</h1>", raw or "", re.I)
        heading = html.unescape(re.sub(r"<[^>]+>|\s+", " ", h1.group(1))).strip() if h1 else ""
        for d in page_items:
            d["category"] = d["category"] or (heading if 0 < len(heading) <= 60 else None)
        by_code += [(d, page_base) for d in page_items]
    if not force_ai and len({normalize_name(d["name"]) for d, _ in by_code}) >= MIN_SITE_DISHES:
        SITE_MENU_SOURCE[menu_url] = "code"
        extracted = by_code
    else:
        SITE_MENU_SOURCE[menu_url] = "AI"
        # AI - не більше MAX_AI_PAGES сторінок: на великих сайтах (Pesto - десятки розділів) інакше десятки хвилин
        if len(pages) > MAX_AI_PAGES:
            SITE_MENU_PARTIAL[menu_url] = True
        extracted = [(d, page_base) for page_html, page_base, _ in pages[:MAX_AI_PAGES]
                     for d in extract_menu(restaurant_name, page_base, page_html[:40000])]

    menu, seen = [], set()
    for d, page_base in extracted:
        d["name"], name_en = split_bilingual(d["name"])
        if name_en:
            d["name_en"] = name_en
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


# українські й російські літери, що різняться в однакових назвах: «Антіпасті» = «Антипасти», «Сир» = «Сыр»
MATCH_LETTERS = str.maketrans("іїєыэ", "ииеие")


def match_key(value, noise_words=()):
    """Назва для зіставлення: без розміру, ваги, дужок і назви ресторану («Піца барбекю 30 см (Pesto)»
    -> «піца барбекю»). Для дедуплікації меню використовується normalize_name - там розміри важливі."""
    words = normalize_name(SIZE_NOISE.sub(" ", value or "")).split()
    return " ".join(w for w in words if w not in noise_words)


def menu_key(value, noise_words=()):
    """match_key, де і/и, е/є тощо однакові: назва в базі буває російською, а на сайті українською."""
    return match_key(value, noise_words).translate(MATCH_LETTERS)


def match_menu(our_dishes, site_menu, restaurant_name=""):
    """Зіставлення за назвою будь-якою мовою: точний збіг або дуже схожа (ratio >= 0.88)."""
    noise = set(normalize_name(restaurant_name).split())
    site_keys = [menu_key(d["name"], noise) for d in site_menu]
    matches, used = {}, set()
    for dish in our_dishes:
        names = {menu_key(get_localizable_value(dish["Name"], l), noise) for l in ("uk", "ru", "en")} - {""}
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
STOP_WORDS = {"з", "із", "зі", "с", "со", "та", "і", "и", "в", "у", "на", "по", "для", "салат", "суп", "соус",
              "and", "with", "or"}
# закінчення й і/и між мовами та написаннями: «лимонний тарт» = «лимонна тарта», «різотто» = «ризото»
STEM_ENDING = re.compile(r"[аяоеєиіїйуюь]+$")
STEM_LETTERS = str.maketrans("іїєё", "ииее")


def name_stems(value, noise_words=()):
    """Основи слів назви (перші 5 літер без закінчення) без службових слів, розмірів і назви ресторану."""
    return {(STEM_ENDING.sub("", w) or w).translate(STEM_LETTERS)[:5]
            for w in match_key(value, noise_words).split() if w not in STOP_WORDS and not w.isdigit()}


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


RENAME_PRICE_DIFF = 0.15


def pair_renames(to_add, to_remove, restaurant_name=""):
    """Третє коло: страва, яку заклад перейменував («Тигрова креветка» -> «Тигрова креветка з головою»), інакше
    стала б парою «прибрати» + «додати». Пара - коли основи слів однієї назви містяться в іншій і різниця - одне
    слово, а ціни відрізняються не більше ніж на 15%. Сам бот нічого не перейменовує: пара йде у «Звіти»
    пропозицією «перейменувати», адмін підтверджує. Повертає (пари, решта to_add, решта to_remove)."""
    noise = set(normalize_name(restaurant_name).split())
    candidates = []
    for i, site in enumerate(to_add):
        theirs = name_stems(site["name"], noise)
        site_price = parse_price(site.get("price"))
        for dish in to_remove:
            our_price = float(dish["Price"] or 0)
            if not site_price or not our_price or abs(site_price - our_price) / max(site_price, our_price) > RENAME_PRICE_DIFF:
                continue
            for lang in ("uk", "ru", "en"):
                ours = name_stems(get_localizable_value(dish["Name"], lang), noise)
                small, big = sorted((ours, theirs), key=len)
                if len(small) >= 2 and small <= big and len(big) - len(small) <= 1:
                    candidates.append((len(small) / len(big), i, dish["Id"], dish))
                    break
    pairs, used_site, used_ours = [], set(), set()
    for _, i, dish_id, dish in sorted(candidates, key=lambda c: c[0], reverse=True):
        if i not in used_site and dish_id not in used_ours:
            pairs.append((dish, to_add[i]))
            used_site.add(i)
            used_ours.add(dish_id)
    return (pairs, [d for i, d in enumerate(to_add) if i not in used_site],
            [d for d in to_remove if d["Id"] not in used_ours])


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
        s3.put_object(Bucket=DO_SPACES_BUCKET, Key=key, Body=buf.getvalue(), ContentType="image/jpeg",
                      CacheControl=PHOTO_CACHE, **PUT_ACL)

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


def restaurant_was_restored(db, restaurant_id):
    """Адмін повернув ресторан на сайт (hidden_restaurants.py --restore): бот його більше не ховає."""
    cur = db.cursor()
    cur.execute("SHOW TABLES LIKE 'RestaurantRestoreLog'")
    found = cur.fetchone() is not None
    if found:
        cur.execute("SELECT 1 FROM RestaurantRestoreLog WHERE RestaurantId = %s LIMIT 1", (restaurant_id,))
        found = cur.fetchone() is not None
    cur.close()
    return found


def set_pending(db, restaurant_id, note):
    """Не закриваємо автоматично: Pending ховає ресторан із сайту до перевірки адміном."""
    if restaurant_was_restored(db, restaurant_id):
        log_check(db, restaurant_id, f"not hidden (restored by admin): {note}", closed_detected=False)
        print(f"  -> {note} - not hidden: restored by admin")
        return
    cur = db.cursor()
    cur.execute("UPDATE Restaurants SET Status = %s WHERE Id = %s", (RESTAURANT_STATUS_PENDING, restaurant_id))
    db.commit()
    cur.close()
    log_check(db, restaurant_id, note, closed_detected=True)
    print(f"  -> {note}")


class RestaurantTimeout(Exception):
    pass


def _on_timeout(signum, frame):
    raise RestaurantTimeout()


def process_restaurant(db, restaurant):
    """Обгортка: витрати AI на ресторан записуються в AiUsageLog за будь-якого результату."""
    USAGE.update(input=0, output=0, cost=0.0)
    # ліміт часу на заклад: великий сайт (десятки сторінок, AI) не блокує весь запуск
    signal.signal(signal.SIGALRM, _on_timeout)
    signal.alarm(RESTAURANT_TIME_LIMIT)
    try:
        _process_restaurant(db, restaurant)
    except RestaurantTimeout:
        print(f"  -> timeout {RESTAURANT_TIME_LIMIT}s - skipped, retry in {RECHECK_UNRECOGNIZED_DAYS} days")
        try:
            db.rollback()
            log_check(db, restaurant["Id"], f"timeout {RESTAURANT_TIME_LIMIT}s (site too big/slow)", closed_detected=False)
        except Exception as e:
            print(f"  timeout not logged: {e}")
    finally:
        signal.alarm(0)
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
    if not website and restaurant.get("MenuUrl"):
        # сайту немає, але адмін указав меню (Expirenza, ChoiceQR): лише меню, ресторан не в Pending
        summary = sync_menu(db, restaurant, restaurant["MenuUrl"])
        log_check(db, restaurant["Id"], summary, closed_detected=False)
        print(f"  -> {summary}")
        return

    needs_osm = not website or not restaurant["GpsLatitude"]
    osm_data = osm_lookup(name, restaurant["Address"], restaurant["City"]) if needs_osm else {}

    if not website and not is_not_site(osm_data.get("website")):
        website = osm_data.get("website")

    if not website and website_was_cleaned(db, restaurant["Id"]):
        # сайт прибрав clean_websites.py (соцмережа, мертвий домен): ресторан не ховаємо і AI не питаємо -
        # меню з'явиться, щойно адмін підтвердить посилання Expirenza/ChoiceQR у «Звітах»
        log_check(db, restaurant["Id"], "no website (cleaned) - waiting for menu link", closed_detected=False)
        print("  -> no website (cleaned), waiting for menu link")
        return

    if not website:
        verification = verify_restaurant_closure(name, restaurant["Address"], restaurant["City"])

        if verification and verification.get("website") and not is_not_site(verification["website"]):
            website = verification["website"]
            notes.append("website found via web search verification")
        else:
            reason = (verification or {}).get("reason", "")
            set_pending(db, restaurant["Id"], f"Pending: no website (DB/OSM/web search). {reason}".strip())
            return
    elif is_dead_website(website):
        # Статус і сайт не чіпаємо: DNS сервера не знаходив робочі домени (murakami.ua та ін.),
        # тож це лише позначка в колонці «Меню (бот)» для перевірки адміном.
        # Посилання на меню з адмінки від сайту не залежить - меню з нього читаємо й тут
        if restaurant.get("MenuUrl"):
            summary = sync_menu(db, restaurant, restaurant["MenuUrl"])
            log_check(db, restaurant["Id"], f"website unreachable (DNS): {website}; {summary}", closed_detected=False)
            print(f"  -> website unreachable (DNS), menu link: {summary}")
            return
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
        site_info = extract_restaurant_info(fetch_website_html.last_raw, html)

    final_hours = site_info.get("opening_hours") or osm_data.get("opening_hours")
    week = osm_to_week(final_hours)
    # графік по днях JSON, як в адмінці; порівнюється за днями - інакше «hours updated» щоразу
    if week and week != current_week(restaurant["OpenHours"]):
        info_updates["OpenHours"] = json.dumps({"perDay": True, "data": week}, ensure_ascii=False)
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

    # title і description сторінка будує сама з даних меню, коли в RestaurantSeo порожньо (SeoText) - їх не
    # заповнюємо, щоб не перекрити кращий автоматичний текст («Меню X, Київ: ціни 2026»); лише ключові слова
    seo_title_empty = False
    seo_description_empty = False
    seo_keywords_empty = not restaurant["SeoKeywords"]
    if seo_title_empty or seo_description_empty or seo_keywords_empty:
        cuisine_json = info_updates.get("Cuisine") or restaurant["Cuisine"]
        city_uk, city_ru = city_names(db, restaurant["City"])
        seo_data = generate_seo(name, city_uk, city_ru, restaurant["Address"],
                                site_info.get("cuisine_uk") or get_localizable_value(cuisine_json, "uk"),
                                site_info.get("cuisine_ru") or get_localizable_value(cuisine_json, "ru"))
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

    if restaurant.get("MenuUrl"):
        # посилання на меню з адмінки має перевагу над знайденим на сайті
        notes.append(sync_menu(db, restaurant, restaurant["MenuUrl"]))
    elif html and is_social_page(base_url or website):
        notes.append("website is a social network page - no menu to read, add the real site in admin")
    elif html:
        notes.append(sync_menu(db, restaurant, menu_url or base_url))

    if LLM_DOWN:
        print("  -> LLM providers unavailable, restaurant left for the next run")
        return

    summary = "; ".join(notes) if notes else "no changes needed"
    log_check(db, restaurant["Id"], summary, closed_detected=False)
    print(f"  -> {summary}")


ASTRAL = re.compile(r"[\U00010000-\U0010FFFF\uFE0F\u200D]")


def no_emoji(value):
    """Без emoji та інших 4-байтових символів (колонки utf8, не utf8mb4) і без зайвих пробілів після них."""
    return re.sub(r"\s{2,}", " ", ASTRAL.sub("", value)).strip()


def sync_menu(db, restaurant, menu_url, force_ai=False):
    """Дані страв - лише з меню на сайті ресторану. Збіглі страви: ціна оновлюється (якщо зміна
    не більше ніж удвічі), порожні вага/опис/фото заповнюються. Додати чи прибрати страви -
    лише пропозиції для адміна, і лише коли меню розпізнане впевнено."""
    name = restaurant["Name"]
    if re.search(r"\.pdf$", urlparse(menu_url or "").path, re.I):
        # меню в PDF не читаємо (ні кодом, ні AI) - лише позначка в журналі для адміна
        return f"menu is PDF - not read: {menu_url}"
    site_menu = read_site_menu(name, menu_url, force_ai)
    for d in site_menu:
        # emoji (4-байтові символи) таблиця Dishes не приймає: «Incorrect string value '\xF0\x9F...'»
        for key in ("name", "description", "weight", "category"):
            if isinstance(d.get(key), str):
                d[key] = no_emoji(d[key])
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
    drop_generic_images(site_menu)

    updated, price_changes = 0, []
    for dish in our_dishes:
        site = matches.get(dish["Id"])
        if not site:
            continue
        updates = {}

        new_price = parse_price(site.get("price"))
        old_price = float(dish["Price"] or 0)
        # ціна «від» (кілька розмірів) на сервісі - не порівнюємо з ціною порції
        if new_price and new_price != old_price and not dish["IsPriceMultiple"] and not site.get("price_from"):
            # з QR-меню (Expirenza, ChoiceQR, RestoApp, ps.me) ціна - від самого закладу: пишемо одразу;
            # з сайту - зміна більше ніж удвічі йде на підтвердження (там можна сплутати порцію чи страву)
            if old_price == 0 or SITE_MENU_SOURCE.get(menu_url) == "QR" or 0.5 <= new_price / old_price <= 2:
                updates["Price"] = new_price
            else:
                price_changes.append((dish, new_price))  # більше ніж удвічі - на підтвердження адміну

        if not dish["Weight"] and site.get("weight"):
            updates["Weight"] = str(site["weight"])[:50]

        site_desc = clean_description(site.get("description"))
        # мова - за текстом опису, а не за назвою страви (назва латиницею, опис українською)
        site_lang = sure_lang(site_desc) or (site.get("lang") if site.get("lang") in ("uk", "ru", "en") else "uk")
        if site_desc and is_localizable_empty(dish["Description"]):
            # інші мови допише DeepL (translate_missing.py)
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
    stats = f"site {len(site_menu)} ({SITE_MENU_SOURCE.get(menu_url, '?')}), matched {len(matches)}/{len(our_dishes)}, updated {updated}, prices to check {len(price_changes)}"
    # QR-меню (Expirenza, ChoiceQR, RestoApp, ps.me) - структуровані дані самого закладу: розпізнане за кількістю страв,
    # без частки збігу - наше меню могло бути з чужого QR-меню (Alibi: 5/180 після заміни посилання). Додати/прибрати -
    # усе одно лише пропозиції адміну. Сайти, прочитані кодом чи AI, - як і раніше, лише з достатнім збігом
    from_qr = SITE_MENU_SOURCE.get(menu_url) == "QR"
    recognized = len(site_menu) >= MIN_SITE_DISHES and (
        from_qr or share >= MIN_MATCH_SHARE or (len(site_menu) >= LARGE_MENU_DISHES and share >= LARGE_MENU_MIN_SHARE))

    if not recognized and not force_ai and SITE_MENU_SOURCE.get(menu_url) == "code" and not LLM_DOWN:
        # код знайшов «страви», але меню не збіглося з нашим (кнопки, акції замість страв) - один раз читаємо AI
        print(f"    menu by code not recognized ({stats}) - retry with AI")
        return sync_menu(db, restaurant, menu_url, force_ai=True)

    if not recognized:
        # ціни збіглих за назвою страв достовірні й без розпізнаного меню
        save_proposals(db, restaurant["Id"], [], [], price_changes)
        return f"menu not recognized ({stats}) at {menu_url}"

    partial = SITE_MENU_PARTIAL.get(menu_url, False)
    # меню прочитане не все (Pesto: AI - 6 сторінок із десятків) - страв, яких не побачили, не пропонуємо прибрати
    unmatched_ours = [] if partial else [d for d in our_dishes if d["Id"] not in matches]
    renames, unmatched_site, unmatched_ours = pair_renames(unmatched_site, unmatched_ours, name)
    save_proposals(db, restaurant["Id"], unmatched_site, unmatched_ours, price_changes, renames)
    if not partial:
        cur = db.cursor()
        cur.execute("""INSERT INTO RestaurantSyncLog (RestaurantId, LastCheckedAt, MenuVerifiedAt) VALUES (%s, NOW(), NOW())
                       ON DUPLICATE KEY UPDATE MenuVerifiedAt = NOW()""", (restaurant["Id"],))
        db.commit()
        cur.close()
    # «from <адреса>» - звідки прочитано меню (адмінка «Розсилка» показує джерело)
    return (f"menu {'partial' if partial else 'ok'} ({stats}); proposals: +{len(unmatched_site)} -{len(unmatched_ours)} "
            f"price {len(price_changes)} rename {len(renames)} from {menu_url}")


def fetch_restaurants_by_slug(db, slugs):
    """Заклади за адресою сторінки (RestaurantSeo.Url) або Id - перевірка поза чергою."""
    cur = db.cursor(dictionary=True)
    rows = []
    for slug in slugs:
        cur.execute(RESTAURANT_COLUMNS + " WHERE rs.Url = %s OR r.Id = %s LIMIT 1", (slug, slug))
        row = cur.fetchone()
        if row:
            rows.append(row)
        else:
            print(f"{slug}: заклад не знайдено")
    cur.close()
    return rows


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--restaurant", action="append", default=[], metavar="URL",
                        help="адреса сторінки закладу або Id - перевірити поза чергою (можна кілька)")
    parser.add_argument("--unrecognized", action="store_true",
                        help="одразу всі заклади, де меню не розпізнане (до --limit)")
    parser.add_argument("--limit", type=int, default=RESTAURANTS_PER_RUN, help="скільки закладів за запуск")
    args = parser.parse_args()
    db = get_db()
    try:
        cur = db.cursor()
        cur.execute(MENU_LINKS_TABLE_SQL)
        cur.close()
        if args.restaurant:
            ensure_log_table(db)
            restaurants = fetch_restaurants_by_slug(db, args.restaurant)
        elif args.unrecognized:
            ensure_log_table(db)
            cur = db.cursor(dictionary=True)
            cur.execute(RESTAURANT_COLUMNS + """ JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
                        WHERE r.Status = %s AND l.Notes LIKE '%%not recognized%%'
                        ORDER BY l.LastCheckedAt LIMIT %s""", (RESTAURANT_STATUS_ACTIVE, args.limit))
            restaurants = cur.fetchall()
            cur.close()
        else:
            restaurants = fetch_restaurants_due_for_check(db, args.limit)
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

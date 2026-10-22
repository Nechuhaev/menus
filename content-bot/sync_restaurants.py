#!/usr/bin/env python3
"""
Unified restaurant + dish sync, without Google Places API (too costly — ~$230/day
at our volume). Everything now comes from the restaurant's own website, fetched once
per restaurant and reused for both restaurant-level and dish-level enrichment.

OpenStreetMap (Nominatim) is used only for: (a) GPS coordinates, and (b) a free,
best-effort check for a `website` tag. A restaurant with no website (or a dead one -
DNS error, 404/410) is never closed automatically: it is set to Pending (hidden from
the site) and logged, so an admin reviews it; a dead website is removed from the card.

Dishes: price, weight, photo and description are taken ONLY from the restaurant's own
menu page (a "menu" link on the homepage, or the homepage itself) - nothing is
generated. Description is stored in the language of the site; translation is done
separately via DeepL (translate_missing.py).

Run daily via cron. Processes a batch of restaurants per run.
"""
import json
import os
import re
import time
from datetime import datetime
from urllib.parse import urljoin

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


def _extract_json(raw_text):
    """Strip markdown code fences and parse JSON. Raises on empty/invalid content —
    some free-tier models return HTTP 200 with an empty or garbage body, which must
    be treated as a failure so the chain moves on to the next provider."""
    if not raw_text or not raw_text.strip():
        raise ValueError("empty response")
    cleaned = re.sub(r"^```json\s*|\s*```$", "", raw_text.strip())
    if not cleaned:
        raise ValueError("empty response after stripping code fences")
    parsed = json.loads(cleaned)
    if not parsed:
        raise ValueError("parsed to an empty JSON object/array")
    return parsed


def call_llm_json(prompt, max_tokens, model="claude-sonnet-4-5"):
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
        return resp.json()["choices"][0]["message"]["content"]

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
                "max_tokens": max_tokens,
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
            return _extract_json(fn())
        except Exception as e:
            last_error = e
            next_name = providers[i + 1][0] if i + 1 < len(providers) else None
            if next_name:
                print(f"    [LLM] {name} failed, trying {next_name}: {e}")
            else:
                print(f"    [LLM] {name} failed too: {e}")

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


def fetch_restaurants_due_for_check(db, limit):
    ensure_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT r.Id, r.Name, r.City, r.Address,
               ri.Website, ri.OpenHours, ri.Contacts, ri.Photos, ri.Cuisine,
               ri.GpsLatitude, ri.GpsLongitude,
               rs.Title AS SeoTitle, rs.Description AS SeoDescription, rs.Keywords AS SeoKeywords
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
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


def fetch_needy_dishes(db, restaurant_id):
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT Id, Name, Description, Price, Weight, WeightType, PhotoLink
        FROM Dishes
        WHERE RestaurantId = %s
        """,
        (restaurant_id,),
    )
    rows = cur.fetchall()
    cur.close()
    # порожній JSON опису ({"Values":[]}) - теж «без опису», тому фільтр тут, а не в SQL
    return [d for d in rows
            if not d["Price"] or not d["Weight"] or is_localizable_empty(d["Description"])
            or not d["PhotoLink"] or d["PhotoLink"] == "[]"]

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


def is_dead_website(url):
    """Сайт точно не працює: домен не існує або сторінки немає (404/410).
    Тайм-аути й 5xx - тимчасові, за них сайт не видаляємо."""
    try:
        resp = requests.get(url, timeout=15, headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"})
        return resp.status_code in (404, 410)
    except requests.exceptions.ConnectionError as e:
        return "Name or service not known" in str(e) or "NameResolutionError" in str(e) or "getaddrinfo failed" in str(e)
    except Exception:
        return False


def fetch_website_html(url, max_chars=12000):
    try:
        resp = requests.get(
            url, timeout=15, headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"}
        )
        resp.raise_for_status()
        raw_html = resp.text
        fetch_website_html.last_menu_url = find_menu_url(raw_html, resp.url)
        og_image_match = re.search(
            r'<meta[^>]+property=["\']og:image["\'][^>]+content=["\']([^"\']+)["\']', raw_html
        )
        og_image = og_image_match.group(1) if og_image_match else None

        html = re.sub(r"<script[\s\S]*?</script>|<style[\s\S]*?</style>", " ", raw_html)
        html = re.sub(r"\s+", " ", html).strip()
        return html[:max_chars], resp.url, og_image
    except Exception as e:
        fetch_website_html.last_menu_url = None
        print(f"    Website fetch failed for {url}: {e}")
        return None, None, None


RESTAURANT_INFO_PROMPT = """Ось HTML-фрагмент сайту ресторану "{restaurant_name}":

{html}

Витягни (тільки те, що ЯВНО є на сторінці, не вигадуй):
- години роботи
- контактний телефон
- тип кухні / короткий опис — окремо українською (cuisine_uk) і російською (cuisine_ru)

Поверни ЛИШЕ валідний JSON:
{{"opening_hours": "..." or null, "phone": "..." or null, "cuisine_uk": "..." or null, "cuisine_ru": "..." or null}}
"""


def extract_restaurant_info(restaurant_name, html):
    try:
        return call_llm_json(RESTAURANT_INFO_PROMPT.format(restaurant_name=restaurant_name, html=html), max_tokens=500)
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
        return call_llm_json(prompt, max_tokens=600)
    except Exception as e:
        print(f"    SEO generation failed: {e}")
        return {}


MATCH_PROMPT = """Ось HTML-фрагмент сторінки меню ресторану "{restaurant_name}" (базовий URL: {base_url}):

{html}

Ось список страв цього ресторану в нашій базі, яким бракує даних:
{dish_list}

Для КОЖНОЇ страви зі списку спробуй знайти на сторінці ТОЧНО ЦЮ страву (за назвою, можливі незначні відмінності у формулюванні) і витягни:
- ціну (тільки число, як вказано на сторінці)
- грамотуру/вихід (наприклад "250 г", як вказано на сторінці)
- URL зображення, якщо він явно прив'язаний саме до цієї страви (src з тегу img поруч із назвою)
- опис або склад страви ДОСЛІВНО, як на сторінці (без змін і без перекладу), і мову цього тексту: "uk", "ru" або "en"

ВАЖЛИВО: якщо конкретної страви немає на сторінці, або дані не вказані явно — постав null для цієї страви (але однаково включи її в масив). НЕ вигадуй ціну, грамотуру, фото чи опис. Це критично: неточна ціна чи вага введе відвідувачів в оману.

Якщо сторінка взагалі не є меню ресторану (наприклад форма входу, помилка, редирект на інший сервіс) — все одно поверни масив з усіма id зі списку страв, де всі поля null.

Поверни ЛИШЕ валідний JSON, без жодного тексту до чи після, масив об'єктів у тому ж порядку, що й список страв:
[
  {{"dish_id": "...", "price": число або null, "weight": "..." або null, "image_url": "..." або null, "description": "..." або null, "description_lang": "uk" або "ru" або "en" або null}}
]
"""


def chunked(items, size):
    for i in range(0, len(items), size):
        yield items[i:i + size]


def match_dishes_on_site(restaurant_name, base_url, html, dishes):
    all_matches = {}
    for chunk in chunked(dishes, 25):
        dish_list = "\n".join(f"- id={d['Id']}: {get_localizable_value(d['Name'], 'uk')}" for d in chunk)
        try:
            prompt = MATCH_PROMPT.format(restaurant_name=restaurant_name, base_url=base_url, html=html, dish_list=dish_list)
        except Exception as e:
            print(f"    Prompt formatting failed for a batch: {e}")
            continue
        try:
            for m in call_llm_json(prompt, max_tokens=6000, model=CHEAP_MODEL):
                if m.get("dish_id"):
                    all_matches[m["dish_id"]] = m
        except Exception as e:
            print(f"    Site matching failed for a batch: {e}")
    return all_matches


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


def set_pending(db, restaurant_id, note, clear_website=False):
    """Не закриваємо автоматично: Pending ховає ресторан із сайту до перевірки адміном."""
    cur = db.cursor()
    cur.execute("UPDATE Restaurants SET Status = %s WHERE Id = %s", (RESTAURANT_STATUS_PENDING, restaurant_id))
    if clear_website:
        cur.execute("UPDATE RestaurantInfo SET Website = NULL WHERE RestaurantId = %s", (restaurant_id,))
    db.commit()
    cur.close()
    log_check(db, restaurant_id, note, closed_detected=True)
    print(f"  -> {note}")


def process_restaurant(db, restaurant):
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
        set_pending(db, restaurant["Id"], f"Pending: website is dead ({website}), removed from the card", clear_website=True)
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

    dishes = fetch_needy_dishes(db, restaurant["Id"])
    if dishes and html:
        menu_html, menu_base = html, base_url
        if menu_url:
            # сторінка меню довша за головну - беремо більше тексту (Haiku дешевий)
            fetched, fetched_base, _ = fetch_website_html(menu_url, max_chars=40000)
            if fetched:
                menu_html, menu_base = fetched, fetched_base
                notes.append(f"menu page: {menu_url}")
        site_matches = match_dishes_on_site(name, menu_base, menu_html, dishes)

        dish_updates_count = 0
        for dish in dishes:
            match = site_matches.get(dish["Id"], {})
            dish_updates = {}

            if (not dish["Price"] or dish["Price"] == 0) and match.get("price"):
                dish_updates["Price"] = match["price"]

            if not dish["Weight"] and match.get("weight"):
                dish_updates["Weight"] = match["weight"]

            # опис лише з сайту, мовою сайту; інші мови допише DeepL (translate_missing.py)
            site_desc = (match.get("description") or "").strip()
            site_lang = match.get("description_lang") if match.get("description_lang") in ("uk", "ru", "en") else "uk"
            if site_desc and is_localizable_empty(dish["Description"]):
                dish_updates["Description"] = json.dumps({"Values": [{"Locale": site_lang, "Value": site_desc}]}, ensure_ascii=False)

            has_photo = dish["PhotoLink"] and dish["PhotoLink"] != "[]"
            if not has_photo and match.get("image_url"):
                try:
                    img_resp = requests.get(resolve_url(menu_base, match["image_url"]), timeout=15)
                    img_resp.raise_for_status()
                    file_id = upload_photo_to_spaces(img_resp.content)
                    dish_updates["PhotoLink"] = json.dumps([file_id])
                except Exception as e:
                    print(f"    Dish photo upload failed: {e}")

            if dish_updates:
                set_clause = ", ".join(f"{col} = %s" for col in dish_updates)
                cur = db.cursor()
                cur.execute(f"UPDATE Dishes SET {set_clause} WHERE Id = %s", (*dish_updates.values(), dish["Id"]))
                db.commit()
                cur.close()
                dish_updates_count += 1

        notes.append(f"{dish_updates_count}/{len(dishes)} dishes updated")

    summary = "; ".join(notes) if notes else "no changes needed"
    log_check(db, restaurant["Id"], summary, closed_detected=False)
    print(f"  -> {summary}")


def main():
    db = get_db()
    try:
        restaurants = fetch_restaurants_due_for_check(db, RESTAURANTS_PER_RUN)
        if not restaurants:
            print("No restaurants due for check.")
            return

        for restaurant in restaurants:
            try:
                process_restaurant(db, restaurant)
            except Exception as e:
                print(f"ERROR processing {restaurant['Name']}: {e}")
    finally:
        db.close()


if __name__ == "__main__":
    main()

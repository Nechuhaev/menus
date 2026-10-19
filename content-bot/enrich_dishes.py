#!/usr/bin/env python3
"""
Dish enrichment: for dishes missing price/weight/description/photo, fetches the
restaurant's own website menu page and asks Claude to match each dish by name and
extract only what's genuinely present on the page — price and weight are NEVER
guessed (wrong price/weight on a menu is a real customer-facing problem), only
copied when found on the restaurant's own site. Description can be generated
generically from the dish name when the site has nothing, since that's just
marketing copy, not a factual claim. Photo: taken from the site if an image is
clearly associated with that dish name; otherwise the dish stays without a photo
(Google Places photos usually show the interior, not the dish).

Run daily via cron. Processes a batch of restaurants (with all their needy dishes,
chunked into groups of 25 per Claude call to stay within safe output limits) per run.
"""
import json
import os
import re
from datetime import datetime

import boto3
import mysql.connector
import requests
from anthropic import Anthropic
from dotenv import load_dotenv

load_dotenv()

CLAUDE_API_KEY = os.environ["CLAUDE_API_KEY"]
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

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)


def get_db():
    return mysql.connector.connect(
        host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME
    )


def ensure_log_table(db):
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS DishEnrichLog (
            RestaurantId VARCHAR(255) PRIMARY KEY,
            LastCheckedAt DATETIME NOT NULL,
            Notes TEXT NULL
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


def fetch_restaurants_with_needy_dishes(db, limit):
    ensure_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT r.Id, r.Name, r.City, ri.Website, ri.Cuisine
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        LEFT JOIN DishEnrichLog l ON l.RestaurantId = r.Id
        WHERE r.Status = 0
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL 30 DAY))
          AND EXISTS (
              SELECT 1 FROM Dishes d
              WHERE d.RestaurantId = r.Id
                AND (d.Price = 0 OR d.Weight IS NULL OR d.Weight = ''
                     OR d.Description IS NULL OR d.PhotoLink IS NULL OR d.PhotoLink = '[]')
          )
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (limit,),
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
          AND (Price = 0 OR Weight IS NULL OR Weight = ''
               OR Description IS NULL OR PhotoLink IS NULL OR PhotoLink = '[]')
        """,
        (restaurant_id,),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def fetch_website_html(url, max_chars=12000):
    try:
        resp = requests.get(
            url, timeout=15, headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"}
        )
        resp.raise_for_status()
        html = re.sub(r"<script[\s\S]*?</script>|<style[\s\S]*?</style>", " ", resp.text)
        html = re.sub(r"\s+", " ", html).strip()
        return html[:max_chars], resp.url
    except Exception as e:
        print(f"    Website fetch failed for {url}: {e}")
        return None, None


MATCH_PROMPT = """Ось HTML-фрагмент сторінки меню ресторану "{restaurant_name}" (базовий URL: {base_url}):

{html}

Ось список страв цього ресторану в нашій базі, яким бракує даних:
{dish_list}

Для КОЖНОЇ страви зі списку спробуй знайти на сторінці ТОЧНО ЦЮ страву (за назвою, можливі незначні відмінності у формулюванні) і витягни:
- ціну (тільки число, як вказано на сторінці)
- грамотуру/вихід (наприклад "250 г", як вказано на сторінці)
- URL зображення, якщо він явно прив'язаний саме до цієї страви (src з тегу img поруч із назвою)

ВАЖЛИВО: якщо конкретної страви немає на сторінці, або дані не вказані явно — постав null для цієї страви (але однаково включи її в масив). НЕ вигадуй ціну, грамотуру чи фото. Це критично: неточна ціна чи вага введе відвідувачів в оману.

Якщо сторінка взагалі не є меню ресторану (наприклад форма входу, помилка, редирект на інший сервіс) — все одно поверни масив з усіма id зі списку страв, де всі поля null.

Поверни ЛИШЕ валідний JSON, без жодного тексту до чи після, масив об'єктів у тому ж порядку, що й список страв:
[
  {{"dish_id": "...", "price": число або null, "weight": "..." або null, "image_url": "..." або null}}
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
            prompt = MATCH_PROMPT.format(
                restaurant_name=restaurant_name, base_url=base_url, html=html, dish_list=dish_list
            )
        except Exception as e:
            print(f"    Prompt formatting failed for a batch (likely stray {{}} in page HTML): {e}")
            continue

        try:
            message = anthropic_client.messages.create(
                model="claude-sonnet-4-5",
                max_tokens=2000,
                messages=[{"role": "user", "content": prompt}],
            )
            raw = message.content[0].text.strip()
            raw = re.sub(r"^```json\s*|\s*```$", "", raw)
            for m in json.loads(raw):
                if m.get("dish_id"):
                    all_matches[m["dish_id"]] = m
        except Exception as e:
            print(f"    Site matching failed for a batch: {e}")
            try:
                print(f"    DEBUG stop_reason={message.stop_reason} content_blocks={len(message.content)} raw_len={len(raw)} raw_preview={raw[:300]!r}")
            except Exception:
                pass
    return all_matches


DESCRIPTION_PROMPT = """Напиши коротку апетитну маркетингову назву-опис для кожної страви зі списку нижче (ресторан "{restaurant_name}", кухня: {cuisine}).

Список страв:
{dish_list}

Для кожної страви — 1-2 речення українською і 1-2 речення російською, привабливо, без перебільшень. Це загальний опис на основі назви (не претензія на точний рецепт закладу).

Поверни ЛИШЕ валідний JSON, масив у тому ж порядку:
[
  {{"dish_id": "...", "description_uk": "...", "description_ru": "..."}}
]
"""


def generate_descriptions_batch(restaurant_name, cuisine, dishes_needing_description):
    all_results = {}
    for chunk in chunked(dishes_needing_description, 25):
        dish_list = "\n".join(
            f"- id={d['Id']}: {get_localizable_value(d['Name'], 'uk') or get_localizable_value(d['Name'], 'ru')}"
            for d in chunk
        )
        try:
            message = anthropic_client.messages.create(
                model="claude-sonnet-4-5",
                max_tokens=4000,
                messages=[{
                    "role": "user",
                    "content": DESCRIPTION_PROMPT.format(restaurant_name=restaurant_name, cuisine=cuisine or "змішана", dish_list=dish_list),
                }],
            )
            raw = message.content[0].text.strip()
            raw = re.sub(r"^```json\s*|\s*```$", "", raw)
            for r in json.loads(raw):
                if r.get("dish_id"):
                    all_results[r["dish_id"]] = r
        except Exception as e:
            print(f"    Batch description generation failed for a batch: {e}")
    return all_results


def resolve_url(base_url, maybe_relative):
    if maybe_relative.startswith("http"):
        return maybe_relative
    from urllib.parse import urljoin
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


def log_check(db, restaurant_id, notes):
    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO DishEnrichLog (RestaurantId, LastCheckedAt, Notes)
        VALUES (%s, %s, %s)
        ON DUPLICATE KEY UPDATE LastCheckedAt = VALUES(LastCheckedAt), Notes = VALUES(Notes)
        """,
        (restaurant_id, datetime.now(), notes),
    )
    db.commit()
    cur.close()


def process_restaurant(db, restaurant):
    dishes = fetch_needy_dishes(db, restaurant["Id"])
    if not dishes:
        log_check(db, restaurant["Id"], "no needy dishes")
        return

    print(f"Restaurant: {restaurant['Name']} ({len(dishes)} dish(es) need data)")

    site_matches = {}
    base_url = None
    if restaurant["Website"]:
        html, base_url = fetch_website_html(restaurant["Website"])
        if html:
            site_matches = match_dishes_on_site(restaurant["Name"], base_url, html, dishes)

    dishes_needing_description = [d for d in dishes if is_localizable_empty(d["Description"])]
    description_batch = {}
    if dishes_needing_description:
        cuisine_text = get_localizable_value(restaurant.get("Cuisine"), "uk")
        description_batch = generate_descriptions_batch(restaurant["Name"], cuisine_text, dishes_needing_description)

    updated_count = 0
    for dish in dishes:
        dish_name_uk = get_localizable_value(dish["Name"], "uk") or get_localizable_value(dish["Name"], "ru")
        match = site_matches.get(dish["Id"], {})

        updates = {}

        if (not dish["Price"] or dish["Price"] == 0) and match.get("price"):
            updates["Price"] = match["price"]

        if not dish["Weight"] and match.get("weight"):
            updates["Weight"] = match["weight"]

        if is_localizable_empty(dish["Description"]):
            desc = description_batch.get(dish["Id"])
            if desc and desc.get("description_uk"):
                updates["Description"] = localizable_json(desc["description_uk"], desc.get("description_ru", desc["description_uk"]))

        current_photos = dish["PhotoLink"]
        has_photo = current_photos and current_photos != "[]"
        if not has_photo:
            image_bytes = None
            if match.get("image_url") and base_url:
                try:
                    img_resp = requests.get(resolve_url(base_url, match["image_url"]), timeout=15)
                    img_resp.raise_for_status()
                    image_bytes = img_resp.content
                except Exception as e:
                    print(f"    Dish image download failed: {e}")

            # Лише фото самої страви з сайту ресторану. Фото з Google Places не беремо:
            # там зазвичай інтер'єр або фасад, а не страва.
            if image_bytes:
                try:
                    file_id = upload_photo_to_spaces(image_bytes)
                    updates["PhotoLink"] = json.dumps([file_id])
                except Exception as e:
                    print(f"    Dish photo upload failed: {e}")

        if updates:
            set_clause = ", ".join(f"{col} = %s" for col in updates)
            cur = db.cursor()
            cur.execute(f"UPDATE Dishes SET {set_clause} WHERE Id = %s", (*updates.values(), dish["Id"]))
            db.commit()
            cur.close()
            updated_count += 1
            print(f"  {dish_name_uk}: {', '.join(updates.keys())}")

    log_check(db, restaurant["Id"], f"{updated_count}/{len(dishes)} dishes updated")


def main():
    db = get_db()
    try:
        restaurants = fetch_restaurants_with_needy_dishes(db, RESTAURANTS_PER_RUN)
        if not restaurants:
            print("No restaurants with needy dishes due for check.")
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

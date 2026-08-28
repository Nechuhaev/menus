#!/usr/bin/env python3
"""
Restaurant actualization: checks each active restaurant against Google Places API
(business status, hours, contacts, photos) and, when the restaurant has a website,
cross-checks/enriches with data scraped from the site itself (Places data isn't always
accurate). Closes permanently-closed places instead of deleting them, and fills in only
missing/stale fields — never overwrites curated marketing copy (Cuisine/ShortInfo/FullInfo)
that's already present.

Run weekly via cron. Processes a batch per run to control API cost.
"""
import json
import os
import re
import time
from datetime import datetime

import boto3
import mysql.connector
import requests
from anthropic import Anthropic
from dotenv import load_dotenv

load_dotenv()

CLAUDE_API_KEY = os.environ["CLAUDE_API_KEY"]
GOOGLE_PLACES_API_KEY = os.environ["GOOGLE_PLACES_API_KEY"]
DO_SPACES_KEY = os.environ["DO_SPACES_KEY"]
DO_SPACES_SECRET = os.environ["DO_SPACES_SECRET"]
DO_SPACES_BUCKET = os.environ.get("DO_SPACES_BUCKET", "cdn-tipmymenus-com")

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

BATCH_SIZE = 20
RECHECK_AFTER_DAYS = 30

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)

RESTAURANT_STATUS_ACTIVE = 0
RESTAURANT_STATUS_CLOSED = 3


def get_db():
    return mysql.connector.connect(
        host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME
    )


def ensure_sync_log_table(db):
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


def fetch_restaurants_due_for_check(db, batch_size):
    ensure_sync_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT r.Id, r.Name, r.Address, r.City,
               ri.Id AS InfoId, ri.Website, ri.OpenHours, ri.Contacts, ri.Photos,
               ri.Cuisine, ri.ShortInfo, ri.GpsLatitude, ri.GpsLongitude,
               rs.Id AS SeoId, rs.Title AS SeoTitle, rs.Description AS SeoDescription, rs.Keywords AS SeoKeywords
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
        LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
        WHERE r.Status = %s
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY))
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RESTAURANT_STATUS_ACTIVE, RECHECK_AFTER_DAYS, batch_size),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def places_text_search(name, address, city):
    url = "https://places.googleapis.com/v1/places:searchText"
    headers = {
        "Content-Type": "application/json",
        "X-Goog-Api-Key": GOOGLE_PLACES_API_KEY,
        "X-Goog-FieldMask": ",".join([
            "places.id",
            "places.displayName",
            "places.businessStatus",
            "places.websiteUri",
            "places.regularOpeningHours.weekdayDescriptions",
            "places.internationalPhoneNumber",
            "places.location",
            "places.photos",
            "places.formattedAddress",
        ]),
    }
    body = {"textQuery": f"{name}, {address}, {city}"}
    resp = requests.post(url, headers=headers, json=body, timeout=20)
    resp.raise_for_status()
    places = resp.json().get("places", [])
    return places[0] if places else None


def download_places_photo(photo_name, max_width=800):
    url = f"https://places.googleapis.com/v1/{photo_name}/media"
    resp = requests.get(url, params={"maxWidthPx": max_width, "key": GOOGLE_PLACES_API_KEY}, timeout=20)
    resp.raise_for_status()
    return resp.content


def upload_photo_to_spaces(image_bytes):
    from PIL import Image
    import io
    import uuid

    file_id = str(uuid.uuid4())
    source = Image.open(io.BytesIO(image_bytes)).convert("RGB")

    s3 = boto3.client(
        "s3",
        region_name="fra1",
        endpoint_url="https://fra1.digitaloceanspaces.com",
        aws_access_key_id=DO_SPACES_KEY,
        aws_secret_access_key=DO_SPACES_SECRET,
    )

    def upload_variant(img, key):
        buf = io.BytesIO()
        img.save(buf, format="JPEG", quality=80)
        buf.seek(0)
        s3.put_object(Bucket=DO_SPACES_BUCKET, Key=key, Body=buf.getvalue(), ACL="public-read", ContentType="image/jpeg")

    def resized(img, max_height):
        ratio = max_height / img.height
        return img.resize((int(img.width * ratio), max_height))

    upload_variant(source, f"files/{file_id}.jpg")
    upload_variant(resized(source, 1000), f"files/{file_id}-large.jpg")
    upload_variant(resized(source, 600), f"files/{file_id}-medium.jpg")
    upload_variant(resized(source, 300), f"files/{file_id}-small.jpg")

    return file_id


def fetch_website_text(url, max_chars=6000):
    try:
        resp = requests.get(
            url, timeout=15, headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"}
        )
        resp.raise_for_status()
        text = re.sub(r"<script[\s\S]*?</script>|<style[\s\S]*?</style>", " ", resp.text)
        text = re.sub(r"<[^>]+>", " ", text)
        text = re.sub(r"\s+", " ", text).strip()
        return text[:max_chars]
    except Exception as e:
        print(f"    Website fetch failed for {url}: {e}")
        return None


EXTRACT_PROMPT = """Ось текст, витягнутий із сайту ресторану "{name}".

{text}

Витягни з цього тексту (якщо інформації немає на сторінці — постав null, не вигадуй):
- години роботи (у форматі "Пн-Нд 10:00-22:00" або подібному, якщо вказано по днях — так і залиш; мовно-нейтрально, одне значення)
- тип кухні / короткий опис закладу — окремо українською (cuisine_uk) і російською (cuisine_ru), 1 фраза кожною мовою
- контактний телефон

Поверни ЛИШЕ валідний JSON:
{{
  "opening_hours": "..." or null,
  "cuisine_uk": "..." or null,
  "cuisine_ru": "..." or null,
  "phone": "..." or null
}}
"""


def extract_from_website(name, text):
    if not text:
        return {}
    try:
        message = anthropic_client.messages.create(
            model="claude-sonnet-4-5",
            max_tokens=500,
            messages=[{"role": "user", "content": EXTRACT_PROMPT.format(name=name, text=text)}],
        )
        raw = message.content[0].text.strip()
        raw = re.sub(r"^```json\s*|\s*```$", "", raw)
        return json.loads(raw)
    except Exception as e:
        print(f"    Website extraction failed: {e}")
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
        message = anthropic_client.messages.create(
            model="claude-sonnet-4-5",
            max_tokens=600,
            messages=[{
                "role": "user",
                "content": SEO_PROMPT.format(name=name, city=city, address=address, cuisine=cuisine_text),
            }],
        )
        raw = message.content[0].text.strip()
        raw = re.sub(r"^```json\s*|\s*```$", "", raw)
        return json.loads(raw)
    except Exception as e:
        print(f"    SEO generation failed: {e}")
        return {}


def is_localizable_empty(json_str):
    """SeoTitle/SeoDescription often already contain an empty '{"Values":[]}' placeholder
    rather than NULL, so a plain truthiness check on the raw string is not enough."""
    if not json_str:
        return True
    try:
        parsed = json.loads(json_str)
        values = parsed.get("Values", [])
        return not any((v.get("Value") or "").strip() for v in values)
    except (json.JSONDecodeError, TypeError, AttributeError):
        return True


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


def process_restaurant(db, restaurant):
    name = restaurant["Name"]
    print(f"Checking: {name} ({restaurant['Address']})")

    place = None
    try:
        place = places_text_search(name, restaurant["Address"], restaurant["City"])
    except Exception as e:
        print(f"  Places search failed: {e}")

    if not place:
        log_check(db, restaurant["Id"], "Not found on Google Places", closed_detected=False)
        print("  Not found on Places, skipped")
        return

    business_status = place.get("businessStatus")
    if business_status == "CLOSED_PERMANENTLY":
        cur = db.cursor()
        cur.execute("UPDATE Restaurants SET Status = %s WHERE Id = %s", (RESTAURANT_STATUS_CLOSED, restaurant["Id"]))
        db.commit()
        cur.close()
        log_check(db, restaurant["Id"], "Marked Closed: CLOSED_PERMANENTLY on Google Places", closed_detected=True)
        print("  -> Marked as CLOSED (permanently closed on Google Places)")
        return

    places_website = place.get("websiteUri")
    places_hours = ", ".join(place.get("regularOpeningHours", {}).get("weekdayDescriptions", [])) or None
    places_phone = place.get("internationalPhoneNumber")
    places_location = place.get("location", {})

    website_url = restaurant["Website"] or places_website
    website_data = {}
    if website_url:
        website_text = fetch_website_text(website_url)
        website_data = extract_from_website(name, website_text)

    updates = {}
    notes = []

    final_hours = website_data.get("opening_hours") or places_hours
    if final_hours and final_hours != restaurant["OpenHours"]:
        updates["OpenHours"] = final_hours
        notes.append("hours updated")

    final_phone = website_data.get("phone") or places_phone
    if final_phone and not restaurant["Contacts"]:
        updates["Contacts"] = final_phone
        notes.append("phone filled")

    if places_website and not restaurant["Website"]:
        updates["Website"] = places_website
        notes.append("website filled")

    if is_localizable_empty(restaurant["Cuisine"]) and (website_data.get("cuisine_uk") or website_data.get("cuisine_ru")):
        cuisine_uk = website_data.get("cuisine_uk") or website_data.get("cuisine_ru")
        cuisine_ru = website_data.get("cuisine_ru") or website_data.get("cuisine_uk")
        updates["Cuisine"] = json.dumps(
            {"Values": [{"Locale": "uk", "Value": cuisine_uk}, {"Locale": "ru", "Value": cuisine_ru}]},
            ensure_ascii=False,
        )
        notes.append("cuisine filled from website")

    if not restaurant["GpsLatitude"] and places_location.get("latitude"):
        updates["GpsLatitude"] = str(places_location["latitude"])
        updates["GpsLongitude"] = str(places_location["longitude"])
        notes.append("GPS filled")

    if not restaurant["Photos"] and place.get("photos"):
        try:
            photo_ids = []
            for photo in place["photos"][:3]:
                image_bytes = download_places_photo(photo["name"])
                file_id = upload_photo_to_spaces(image_bytes)
                photo_ids.append(file_id)
            if photo_ids:
                updates["Photos"] = json.dumps(photo_ids)
                notes.append(f"{len(photo_ids)} photos added")
        except Exception as e:
            print(f"    Photo upload failed: {e}")

    if updates:
        set_clause = ", ".join(f"{col} = %s" for col in updates)
        cur = db.cursor()
        cur.execute(f"UPDATE RestaurantInfo SET {set_clause} WHERE RestaurantId = %s", (*updates.values(), restaurant["Id"]))
        db.commit()
        cur.close()

    seo_title_empty = is_localizable_empty(restaurant["SeoTitle"])
    seo_description_empty = is_localizable_empty(restaurant["SeoDescription"])
    seo_keywords_empty = not restaurant["SeoKeywords"]
    seo_needs_filling = seo_title_empty or seo_description_empty or seo_keywords_empty
    if seo_needs_filling:
        cuisine_uk_for_seo = website_data.get("cuisine_uk")
        cuisine_ru_for_seo = website_data.get("cuisine_ru")
        seo_data = generate_seo(name, restaurant["City"], restaurant["Address"], cuisine_uk_for_seo, cuisine_ru_for_seo)
        seo_updates = {}
        if seo_data.get("title") and seo_title_empty:
            seo_updates["Title"] = json.dumps(
                {"Values": [{"Locale": "uk", "Value": seo_data["title"]["uk"]}, {"Locale": "ru", "Value": seo_data["title"]["ru"]}]},
                ensure_ascii=False,
            )
        if seo_data.get("description") and seo_description_empty:
            seo_updates["Description"] = json.dumps(
                {"Values": [{"Locale": "uk", "Value": seo_data["description"]["uk"]}, {"Locale": "ru", "Value": seo_data["description"]["ru"]}]},
                ensure_ascii=False,
            )
        if seo_data.get("keywords") and seo_keywords_empty:
            seo_updates["Keywords"] = f"{seo_data['keywords']['uk']}, {seo_data['keywords']['ru']}"

        if seo_updates:
            set_clause = ", ".join(f"{col} = %s" for col in seo_updates)
            cur = db.cursor()
            cur.execute(f"UPDATE RestaurantSeo SET {set_clause} WHERE ItemId = %s", (*seo_updates.values(), restaurant["Id"]))
            db.commit()
            cur.close()
            notes.append("SEO meta filled")

    summary = "; ".join(notes) if notes else "no changes needed"
    log_check(db, restaurant["Id"], summary, closed_detected=False)
    print(f"  -> {summary}")


def main():
    db = get_db()
    try:
        restaurants = fetch_restaurants_due_for_check(db, BATCH_SIZE)
        if not restaurants:
            print("No restaurants due for sync check.")
            return

        print(f"Checking {len(restaurants)} restaurant(s)...")
        for restaurant in restaurants:
            try:
                process_restaurant(db, restaurant)
            except Exception as e:
                print(f"  ERROR processing {restaurant['Name']}: {e}")
            time.sleep(1)
    finally:
        db.close()


if __name__ == "__main__":
    main()

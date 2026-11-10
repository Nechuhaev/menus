#!/usr/bin/env python3
"""
Translates ru->uk for entities where the Ukrainian locale is missing/empty but
Russian is present. Priority scope (this run): restaurants shown on the homepage
(ShowOnIndex=1), their dishes, and the most recent blog articles. The rest of the
database is a separate later pass, not covered here.

Переклад - через DeepL API (DEEPL_API_KEY у .env), не через LLM. Якщо DeepL визначив,
що текст уже український, він лишається як є. Не більше DEEPL_MAX_CHARS символів за запуск.

Run manually for now (not yet scheduled in cron).
"""
import argparse
import json
import os
import re
from datetime import datetime

import mysql.connector
import requests
from dotenv import load_dotenv

load_dotenv()

DEEPL_API_KEY = os.environ["DEEPL_API_KEY"]
DEEPL_URL = "https://api-free.deepl.com" if DEEPL_API_KEY.endswith(":fx") else "https://api.deepl.com"
DEEPL_MAX_CHARS = int(os.environ.get("DEEPL_MAX_CHARS", "15000"))

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

RECENT_PAGES_LIMIT = 30
DISHES_PER_RUN_LIMIT = 30
FULL_RESTAURANT_BATCH_SIZE = 10
FULL_PAGE_BATCH_SIZE = 10
RECHECK_AFTER_DAYS = 90
deepl_chars_spent = 0


def get_db():
    return mysql.connector.connect(
        host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME
    )


def is_locale_missing(json_str, locale):
    if not json_str:
        return True
    try:
        parsed = json.loads(json_str)
        for v in parsed.get("Values", []):
            if v.get("Locale") == locale:
                return not (v.get("Value") or "").strip()
        return True
    except (json.JSONDecodeError, TypeError, AttributeError):
        return True


def parse_localizable(json_str):
    """{"Values": [...]} з поля LocalizableString. Простий текст (не JSON) сайт вважає російським
    (LocalizableString.FromJson, defaultLocale «ru») - так само й тут, щоб не загубити його при записі."""
    if not json_str:
        return {"Values": []}
    try:
        parsed = json.loads(json_str)
    except (json.JSONDecodeError, TypeError):
        parsed = None
    if not isinstance(parsed, dict):
        return {"Values": [{"Locale": "ru", "Value": str(json_str)}]}
    if not isinstance(parsed.get("Values"), list):
        parsed["Values"] = []
    return parsed


def get_locale_value(json_str, locale):
    for v in parse_localizable(json_str)["Values"]:
        if isinstance(v, dict) and v.get("Locale") == locale:
            return v.get("Value") or ""
    return ""


def set_locale_value(json_str, locale, new_value):
    """Update one locale's value within an existing localizable JSON string,
    preserving whatever the other locale(s) already have."""
    values = parse_localizable(json_str)["Values"]
    found = False
    for v in values:
        if v.get("Locale") == locale:
            v["Value"] = new_value
            found = True
    if not found:
        values.append({"Locale": locale, "Value": new_value})
    return json.dumps({"Values": values}, ensure_ascii=False)


def translate_fields(field_dict, max_tokens=None):
    """field_dict: {field_name: russian_text}. Returns {field_name: ukrainian_text} or {}.
    max_tokens лишився від LLM-версії і не використовується."""
    global deepl_chars_spent
    if not field_dict:
        return {}
    names = list(field_dict)
    size = sum(len(field_dict[n]) for n in names)
    if deepl_chars_spent + size > DEEPL_MAX_CHARS:
        return {}  # бюджет запуску вичерпано - решта наступного разу

    try:
        r = requests.post(
            f"{DEEPL_URL}/v2/translate",
            headers={"Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}"},
            json={"text": [field_dict[n] for n in names], "target_lang": "UK",
                  "tag_handling": "html", "preserve_formatting": True},
            timeout=60,
        )
        r.raise_for_status()
        translations = r.json()["translations"]
    except Exception as e:
        print(f"    Translation failed: {e}")
        return {}
    deepl_chars_spent += size
    return {n: field_dict[n] if t.get("detected_source_language") == "UK" else t["text"]
            for n, t in zip(names, translations)}


def translate_restaurant_info(db, restaurant_id, cuisine, short_info, full_info):
    fields = {}
    if is_locale_missing(cuisine, "uk") and get_locale_value(cuisine, "ru"):
        fields["cuisine"] = get_locale_value(cuisine, "ru")
    if is_locale_missing(short_info, "uk") and get_locale_value(short_info, "ru"):
        fields["short_info"] = get_locale_value(short_info, "ru")
    if is_locale_missing(full_info, "uk") and get_locale_value(full_info, "ru"):
        fields["full_info"] = get_locale_value(full_info, "ru")

    if not fields:
        return False

    translated = translate_fields(fields, max_tokens=8000)
    if not translated:
        return False

    updates = {}
    if "cuisine" in translated:
        updates["Cuisine"] = set_locale_value(cuisine, "uk", translated["cuisine"])
    if "short_info" in translated:
        updates["ShortInfo"] = set_locale_value(short_info, "uk", translated["short_info"])
    if "full_info" in translated:
        updates["FullInfo"] = set_locale_value(full_info, "uk", translated["full_info"])

    if updates:
        set_clause = ", ".join(f"{col} = %s" for col in updates)
        cur = db.cursor()
        cur.execute(f"UPDATE RestaurantInfo SET {set_clause} WHERE RestaurantId = %s", (*updates.values(), restaurant_id))
        db.commit()
        cur.close()
        return True
    return False


def translate_restaurant_seo(db, restaurant_id, title, description):
    fields = {}
    if is_locale_missing(title, "uk") and get_locale_value(title, "ru"):
        fields["title"] = get_locale_value(title, "ru")
    if is_locale_missing(description, "uk") and get_locale_value(description, "ru"):
        fields["description"] = get_locale_value(description, "ru")

    if not fields:
        return False

    translated = translate_fields(fields, max_tokens=1000)
    if not translated:
        return False

    updates = {}
    if "title" in translated:
        updates["Title"] = set_locale_value(title, "uk", translated["title"])
    if "description" in translated:
        updates["Description"] = set_locale_value(description, "uk", translated["description"])

    if updates:
        set_clause = ", ".join(f"{col} = %s" for col in updates)
        cur = db.cursor()
        cur.execute(f"UPDATE RestaurantSeo SET {set_clause} WHERE ItemId = %s", (*updates.values(), restaurant_id))
        db.commit()
        cur.close()
        return True
    return False


def process_priority_restaurants(db):
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT r.Id, r.Name, ri.Cuisine, ri.ShortInfo, ri.FullInfo,
               rs.Title AS SeoTitle, rs.Description AS SeoDescription
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
        WHERE r.Status = 0 AND ri.ShowOnIndex = 1
        """
    )
    restaurants = cur.fetchall()
    cur.close()

    print(f"Checking {len(restaurants)} homepage restaurant(s) for missing uk translation...")
    for r in restaurants:
        info_updated = translate_restaurant_info(db, r["Id"], r["Cuisine"], r["ShortInfo"], r["FullInfo"])
        seo_updated = translate_restaurant_seo(db, r["Id"], r["SeoTitle"], r["SeoDescription"])
        if info_updated or seo_updated:
            print(f"  {r['Name']}: translated to uk")
    return [r["Id"] for r in restaurants]


# лише російські літери: такий «український» текст насправді російський (імпорт з QR-меню)
RUSSIAN_ONLY = re.compile(r"[ыэъё]", re.I)
UKRAINIAN_ONLY = re.compile(r"[іїєґ]", re.I)
RUSSIAN_WORDS = re.compile(r"(?<![\w'])(и|из|с|со|под|или|из-под)(?![\w'])", re.I)


def looks_russian(text):
    """«Десерт из слоеного теста» без ы/э/ъ/ё теж російський: немає і/ї/є/ґ, зате є «и», «из», «с»."""
    return bool(RUSSIAN_ONLY.search(text) or (not UKRAINIAN_ONLY.search(text) and RUSSIAN_WORDS.search(text)))


def dish_source(json_str):
    """Російський текст для перекладу в uk або None. Береться ru, а якщо в полі uk
    лежить російський текст (ы/э/ъ/ё) - він сам (ru тоді доповнюється ним же)."""
    uk = get_locale_value(json_str, "uk").strip()
    ru = get_locale_value(json_str, "ru").strip()
    if uk and not looks_russian(uk):
        return None
    return ru or (uk if uk else None)


def translate_dish(db, dish_id, name, short_description, ingredients, description):
    fields = {}
    sources = {"name": name, "short_description": short_description, "ingredients": ingredients, "description": description}
    for key, value in sources.items():
        text = dish_source(value)
        if text:
            fields[key] = text

    if not fields:
        return False

    translated = translate_fields(fields, max_tokens=3000)
    if not translated:
        return False

    updates = {}
    columns = {"name": "Name", "short_description": "ShortDescription", "ingredients": "Ingredients", "description": "Description"}
    for key, column in columns.items():
        if key not in translated:
            continue
        value = sources[key]
        # російський текст стояв у полі uk, а ru порожнє - він переходить у ru
        if not get_locale_value(value, "ru").strip():
            value = set_locale_value(value, "ru", fields[key])
        updates[column] = set_locale_value(value, "uk", translated[key])

    if updates:
        set_clause = ", ".join(f"{col} = %s" for col in updates)
        cur = db.cursor()
        cur.execute(f"UPDATE Dishes SET {set_clause} WHERE Id = %s", (*updates.values(), dish_id))
        db.commit()
        cur.close()
        return True
    return False


def process_dishes_for_restaurants(db, restaurant_ids):
    if not restaurant_ids:
        return

    cur = db.cursor(dictionary=True)
    format_ids = ", ".join(["%s"] * len(restaurant_ids))
    cur.execute(
        f"""
        SELECT Id, Name, ShortDescription, Ingredients, Description
        FROM Dishes
        WHERE RestaurantId IN ({format_ids})
        """,
        tuple(restaurant_ids),
    )
    dishes = cur.fetchall()
    cur.close()

    print(f"Checking {len(dishes)} dish(es) from homepage restaurants for missing uk translation...")
    translated_count = 0
    for d in dishes:
        if translate_dish(db, d["Id"], d["Name"], d["ShortDescription"], d["Ingredients"], d["Description"]):
            translated_count += 1
    print(f"  {translated_count}/{len(dishes)} dishes translated")


def translate_page(db, page_id, title, short_content, content, seo_title, seo_description, seo_keywords):
    fields = {}
    if is_locale_missing(title, "uk") and get_locale_value(title, "ru"):
        fields["title"] = get_locale_value(title, "ru")
    if is_locale_missing(short_content, "uk") and get_locale_value(short_content, "ru"):
        fields["short_content"] = get_locale_value(short_content, "ru")
    if is_locale_missing(content, "uk") and get_locale_value(content, "ru"):
        fields["content"] = get_locale_value(content, "ru")
    if is_locale_missing(seo_title, "uk") and get_locale_value(seo_title, "ru"):
        fields["seo_title"] = get_locale_value(seo_title, "ru")
    if is_locale_missing(seo_description, "uk") and get_locale_value(seo_description, "ru"):
        fields["seo_description"] = get_locale_value(seo_description, "ru")
    if is_locale_missing(seo_keywords, "uk") and get_locale_value(seo_keywords, "ru"):
        fields["seo_keywords"] = get_locale_value(seo_keywords, "ru")

    if not fields:
        return False

    translated = translate_fields(fields, max_tokens=16000)
    if not translated:
        return False

    updates = {}
    if "title" in translated:
        updates["Title"] = set_locale_value(title, "uk", translated["title"])
    if "short_content" in translated:
        updates["ShortContent"] = set_locale_value(short_content, "uk", translated["short_content"])
    if "content" in translated:
        updates["Content"] = set_locale_value(content, "uk", translated["content"])
    if "seo_title" in translated:
        updates["SeoTitle"] = set_locale_value(seo_title, "uk", translated["seo_title"])
    if "seo_description" in translated:
        updates["SeoDescription"] = set_locale_value(seo_description, "uk", translated["seo_description"])
    if "seo_keywords" in translated:
        updates["SeoKeywords"] = set_locale_value(seo_keywords, "uk", translated["seo_keywords"])

    if updates:
        set_clause = ", ".join(f"{col} = %s" for col in updates)
        cur = db.cursor()
        cur.execute(f"UPDATE Pages SET {set_clause} WHERE Id = %s", (*updates.values(), page_id))
        db.commit()
        cur.close()
        return True
    return False


def process_recent_pages(db):
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT Id, Url, Title, ShortContent, Content, SeoTitle, SeoDescription, SeoKeywords
        FROM Pages
        WHERE Category = 1 AND Status = 1
        ORDER BY Timestamp DESC
        LIMIT %s
        """,
        (RECENT_PAGES_LIMIT,),
    )
    pages = cur.fetchall()
    cur.close()

    print(f"Checking {len(pages)} recent blog article(s) for missing uk translation...")
    for p in pages:
        if translate_page(db, p["Id"], p["Title"], p["ShortContent"], p["Content"], p["SeoTitle"], p["SeoDescription"], p["SeoKeywords"]):
            print(f"  {p['Url']}: translated to uk")


def ensure_translation_log_table(db):
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS TranslationSyncLog (
            EntityType VARCHAR(50) NOT NULL,
            EntityId VARCHAR(255) NOT NULL,
            LastCheckedAt DATETIME NOT NULL,
            Notes TEXT NULL,
            PRIMARY KEY (EntityType, EntityId)
        )
        """
    )
    db.commit()
    cur.close()


def log_translation_check(db, entity_type, entity_id, notes):
    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO TranslationSyncLog (EntityType, EntityId, LastCheckedAt, Notes)
        VALUES (%s, %s, %s, %s)
        ON DUPLICATE KEY UPDATE LastCheckedAt = VALUES(LastCheckedAt), Notes = VALUES(Notes)
        """,
        (entity_type, entity_id, datetime.now(), notes),
    )
    db.commit()
    cur.close()


def fetch_restaurants_for_full_pass(db, limit):
    ensure_translation_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT r.Id, r.Name, ri.Cuisine, ri.ShortInfo, ri.FullInfo,
               rs.Title AS SeoTitle, rs.Description AS SeoDescription
        FROM Restaurants r
        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
        LEFT JOIN TranslationSyncLog l ON l.EntityType = 'Restaurant' AND l.EntityId = r.Id
        WHERE r.Status = 0
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY))
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RECHECK_AFTER_DAYS, limit),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def fetch_all_dishes_for_restaurant(db, restaurant_id):
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT Id, Name, ShortDescription, Ingredients, Description
        FROM Dishes
        WHERE RestaurantId = %s
        """,
        (restaurant_id,),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def process_full_restaurant_batch(db):
    restaurants = fetch_restaurants_for_full_pass(db, FULL_RESTAURANT_BATCH_SIZE)
    if not restaurants:
        print("No restaurants due for full-base translation check.")
        return

    print(f"[Full pass] Checking {len(restaurants)} restaurant(s)...")
    for r in restaurants:
        info_updated = translate_restaurant_info(db, r["Id"], r["Cuisine"], r["ShortInfo"], r["FullInfo"])
        seo_updated = translate_restaurant_seo(db, r["Id"], r["SeoTitle"], r["SeoDescription"])

        dishes = fetch_all_dishes_for_restaurant(db, r["Id"])
        dish_translated_count = 0
        for d in dishes:
            if translate_dish(db, d["Id"], d["Name"], d["ShortDescription"], d["Ingredients"], d["Description"]):
                dish_translated_count += 1

        summary = f"info={info_updated}; seo={seo_updated}; dishes {dish_translated_count}/{len(dishes)}"
        log_translation_check(db, "Restaurant", r["Id"], summary)
        print(f"  {r['Name']}: {summary}")


def fetch_pages_for_full_pass(db, limit):
    ensure_translation_log_table(db)
    cur = db.cursor(dictionary=True)
    cur.execute(
        """
        SELECT p.Id, p.Url, p.Title, p.ShortContent, p.Content, p.SeoTitle, p.SeoDescription, p.SeoKeywords
        FROM Pages p
        LEFT JOIN TranslationSyncLog l ON l.EntityType = 'Page' AND l.EntityId = p.Id
        WHERE p.Category = 1 AND p.Status = 1
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY))
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RECHECK_AFTER_DAYS, limit),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def process_full_page_batch(db):
    pages = fetch_pages_for_full_pass(db, FULL_PAGE_BATCH_SIZE)
    if not pages:
        print("No blog articles due for full-base translation check.")
        return

    print(f"[Full pass] Checking {len(pages)} blog article(s)...")
    for p in pages:
        updated = translate_page(db, p["Id"], p["Title"], p["ShortContent"], p["Content"], p["SeoTitle"], p["SeoDescription"], p["SeoKeywords"])
        log_translation_check(db, "Page", p["Id"], "translated" if updated else "no changes needed")
        if updated:
            print(f"  {p['Url']}: translated to uk")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--restaurant", action="append", metavar="ID",
                        help="лише страви цього ресторану: Id або адреса сторінки (можна кілька), без решти проходів")
    args = parser.parse_args()
    db = get_db()
    try:
        if args.restaurant:
            # Id або адреса сторінки ресторану (chachapuri з /uk/kyiv/restaurant/chachapuri)
            ids = []
            cur = db.cursor()
            for value in args.restaurant:
                cur.execute("SELECT CONCAT(ItemId, '') FROM RestaurantSeo WHERE Url = %s LIMIT 1", (value,))
                row = cur.fetchone()
                ids.append(row[0] if row else value)
            cur.close()
            process_dishes_for_restaurants(db, ids)
            print(f"DeepL: {deepl_chars_spent} символів")
            return
        restaurant_ids = process_priority_restaurants(db)
        process_dishes_for_restaurants(db, restaurant_ids)
        process_recent_pages(db)

        process_full_restaurant_batch(db)
        process_full_page_batch(db)
    finally:
        db.close()


if __name__ == "__main__":
    main()

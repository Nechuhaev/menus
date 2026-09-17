#!/usr/bin/env python3
"""
Translates ru->uk for entities where the Ukrainian locale is missing/empty but
Russian is present. Priority scope (this run): restaurants shown on the homepage
(ShowOnIndex=1), their dishes, and the most recent blog articles. The rest of the
database is a separate later pass, not covered here.

LLM calls use the same 5-tier fallback as the other content-bot scripts:
Claude -> OpenRouter -> Groq -> Cloudflare Workers AI -> Gemini.

Run manually for now (not yet scheduled in cron).
"""
import json
import os
import re
import time
from datetime import datetime

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

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

RECENT_PAGES_LIMIT = 30

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


def call_llm_json(prompt, max_tokens):
    """Try Claude, then OpenRouter, then Groq, then Cloudflare Workers AI, then
    Gemini directly (Gemini last — its free tier caps at only 20 requests/day).
    Each provider's raw response is validated as usable JSON before being accepted,
    so a 200 OK with empty/garbage content doesn't get treated as success."""

    def try_claude():
        message = anthropic_client.messages.create(
            model="claude-sonnet-4-5",
            max_tokens=max_tokens,
            messages=[{"role": "user", "content": prompt}],
        )
        return message.content[0].text

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


def get_locale_value(json_str, locale):
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


def set_locale_value(json_str, locale, new_value):
    """Update one locale's value within an existing localizable JSON string,
    preserving whatever the other locale(s) already have."""
    try:
        parsed = json.loads(json_str) if json_str else {"Values": []}
    except (json.JSONDecodeError, TypeError):
        parsed = {"Values": []}
    values = parsed.get("Values", [])
    found = False
    for v in values:
        if v.get("Locale") == locale:
            v["Value"] = new_value
            found = True
    if not found:
        values.append({"Locale": locale, "Value": new_value})
    return json.dumps({"Values": values}, ensure_ascii=False)


TRANSLATE_PROMPT = """Переклади наступні тексти з російської мови на українську. Якщо в тексті є HTML-теги — залиш їх без змін, перекладай лише текстовий вміст. Не додавай нічого зайвого, не скорочуй.

{fields}

Поверни ЛИШЕ валідний JSON з тими самими ключами полів:
{{
{example}
}}
"""


def translate_fields(field_dict, max_tokens=4000):
    """field_dict: {field_name: russian_text}. Returns {field_name: ukrainian_text} or {}."""
    if not field_dict:
        return {}

    fields_text = "\n\n".join(f'--- {name} ---\n{text}' for name, text in field_dict.items())
    example = ",\n".join(f'  "{name}": "..."' for name in field_dict.keys())

    try:
        prompt = TRANSLATE_PROMPT.format(fields=fields_text, example=example)
        result = call_llm_json(prompt, max_tokens=max_tokens)
    except Exception as e:
        print(f"    Translation failed: {e}")
        result = {}
    time.sleep(2)
    return result


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


def translate_dish(db, dish_id, name, short_description, ingredients, description):
    fields = {}
    if is_locale_missing(name, "uk") and get_locale_value(name, "ru"):
        fields["name"] = get_locale_value(name, "ru")
    if is_locale_missing(short_description, "uk") and get_locale_value(short_description, "ru"):
        fields["short_description"] = get_locale_value(short_description, "ru")
    if is_locale_missing(ingredients, "uk") and get_locale_value(ingredients, "ru"):
        fields["ingredients"] = get_locale_value(ingredients, "ru")
    if is_locale_missing(description, "uk") and get_locale_value(description, "ru"):
        fields["description"] = get_locale_value(description, "ru")

    if not fields:
        return False

    translated = translate_fields(fields, max_tokens=3000)
    if not translated:
        return False

    updates = {}
    if "name" in translated:
        updates["Name"] = set_locale_value(name, "uk", translated["name"])
    if "short_description" in translated:
        updates["ShortDescription"] = set_locale_value(short_description, "uk", translated["short_description"])
    if "ingredients" in translated:
        updates["Ingredients"] = set_locale_value(ingredients, "uk", translated["ingredients"])
    if "description" in translated:
        updates["Description"] = set_locale_value(description, "uk", translated["description"])

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
    db = get_db()
    try:
        restaurant_ids = process_priority_restaurants(db)
        process_dishes_for_restaurants(db, restaurant_ids)
        process_recent_pages(db)

        process_full_restaurant_batch(db)
        process_full_page_batch(db)
    finally:
        db.close()


if __name__ == "__main__":
    main()

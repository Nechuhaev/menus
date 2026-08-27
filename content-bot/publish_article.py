#!/usr/bin/env python3
"""
Content combine: plans topics via Serpstat, generates bilingual articles via Claude,
picks photos via Unsplash, publishes to the Pages table (Blog/News category).

Run daily via cron at 10:00 Europe/Kyiv.
"""
import json
import os
import re
import time
import uuid
from datetime import datetime

import boto3
import mysql.connector
import requests
from anthropic import Anthropic
from dotenv import load_dotenv

load_dotenv()

CLAUDE_API_KEY = os.environ["CLAUDE_API_KEY"]
SERPSTAT_API_KEY = os.environ["SERPSTAT_API_KEY"]
UNSPLASH_ACCESS_KEY = os.environ["UNSPLASH_ACCESS_KEY"]
DO_SPACES_KEY = os.environ["DO_SPACES_KEY"]
DO_SPACES_SECRET = os.environ["DO_SPACES_SECRET"]
DO_SPACES_BUCKET = os.environ.get("DO_SPACES_BUCKET", "cdn-tipmymenus-com")

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

SITE_THEME = "ресторани, меню, доставка їжі, відгуки на заклади громадського харчування в Києві та Україні"
SEED_KEYWORDS = ["ресторани київ", "меню ресторану", "де поїсти в києві", "доставка їжі"]

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)


def get_db():
    return mysql.connector.connect(
        host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME
    )


def ensure_content_plan_table(db):
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS ContentPlan (
            Id VARCHAR(255) PRIMARY KEY,
            Keyword VARCHAR(500) NOT NULL,
            Used TINYINT(1) NOT NULL DEFAULT 0,
            PageId VARCHAR(255) NULL,
            CreatedAt DATETIME NOT NULL,
            UsedAt DATETIME NULL
        )
        """
    )
    db.commit()
    cur.close()


def fetch_serpstat_keywords(seed_keyword, limit=30):
    """Pull related keyword suggestions from Serpstat for a seed keyword."""
    url = "https://api.serpstat.com/v4"
    payload = {
        "id": str(uuid.uuid4()),
        "method": "SerpstatKeywordProcedure.getKeywords",
        "params": {
            "keyword": seed_keyword,
            "se": "g_ua",
            "size": limit,
        },
    }
    resp = requests.post(f"{url}?token={SERPSTAT_API_KEY}", json=payload, timeout=30)
    resp.raise_for_status()
    data = resp.json()
    if "result" not in data or "data" not in data["result"]:
        return []
    return [row["keyword"] for row in data["result"]["data"]]


def build_content_plan(db, target_size=20):
    ensure_content_plan_table(db)
    cur = db.cursor()
    cur.execute("SELECT COUNT(*) FROM ContentPlan WHERE Used = 0")
    (remaining,) = cur.fetchone()
    cur.close()

    if remaining >= 3:
        return

    all_keywords = set()
    for seed in SEED_KEYWORDS:
        try:
            kws = fetch_serpstat_keywords(seed, limit=20)
            all_keywords.update(kws)
        except Exception as e:
            print(f"Serpstat fetch failed for '{seed}': {e}")

    if not all_keywords:
        return

    cur = db.cursor()
    cur.execute("SELECT Keyword FROM ContentPlan")
    existing = {row[0] for row in cur.fetchall()}
    cur.close()

    new_keywords = [k for k in all_keywords if k not in existing][:target_size]

    cur = db.cursor()
    for kw in new_keywords:
        cur.execute(
            "INSERT INTO ContentPlan (Id, Keyword, Used, CreatedAt) VALUES (%s, %s, 0, %s)",
            (str(uuid.uuid4()), kw, datetime.now()),
        )
    db.commit()
    cur.close()
    print(f"Added {len(new_keywords)} new keywords to content plan")


def pick_next_keyword(db):
    cur = db.cursor()
    cur.execute("SELECT Id, Keyword FROM ContentPlan WHERE Used = 0 ORDER BY CreatedAt ASC LIMIT 1")
    row = cur.fetchone()
    cur.close()
    return row


def mark_keyword_used(db, plan_id, page_id):
    cur = db.cursor()
    cur.execute(
        "UPDATE ContentPlan SET Used = 1, PageId = %s, UsedAt = %s WHERE Id = %s",
        (page_id, datetime.now(), plan_id),
    )
    db.commit()
    cur.close()


ARTICLE_PROMPT = """Ти — контент-редактор для сайту tipmymenu.com про ресторани, меню та їжу в Україні.

Напиши статтю на тему, повʼязану з ключовим запитом: "{keyword}"

Вимоги:
- Дві мовні версії: українська (uk) та російська (ru)
- Обсяг кожної версії: 2000-25000 символів, структурований HTML (заголовки h2/h3, списки, абзаци)
- Природний стиль, корисний контент для читача, що цікавиться ресторанами/їжею в Україні, без "води"
- Обов'язково природно, органічно використай ключове слово і 2-3 споріднені фрази кілька разів по тексту
- Короткий опис (short content) — 1-2 речення анонсу статті, окремо для кожної мови
- SEO title (до 60 символів), SEO description (до 160 символів), SEO keywords (5-8 фраз через кому) — окремо для кожної мови

Поверни ЛИШЕ валідний JSON, без жодного тексту до чи після, у такому форматі:
{{
  "title": {{"uk": "...", "ru": "..."}},
  "short_content": {{"uk": "...", "ru": "..."}},
  "content": {{"uk": "<p>...</p>", "ru": "<p>...</p>"}},
  "seo_title": {{"uk": "...", "ru": "..."}},
  "seo_description": {{"uk": "...", "ru": "..."}},
  "seo_keywords": {{"uk": "...", "ru": "..."}},
  "photo_search_query": "1-3 англійських слова для пошуку фото на Unsplash за темою статті"
}}
"""


def generate_article(keyword):
    message = anthropic_client.messages.create(
        model="claude-sonnet-4-5",
        max_tokens=16000,
        messages=[{"role": "user", "content": ARTICLE_PROMPT.format(keyword=keyword)}],
    )
    raw = message.content[0].text.strip()
    raw = re.sub(r"^```json\s*|\s*```$", "", raw.strip())
    return json.loads(raw)


def search_unsplash_photos(query, count=3):
    url = "https://api.unsplash.com/search/photos"
    headers = {"Authorization": f"Client-ID {UNSPLASH_ACCESS_KEY}"}
    resp = requests.get(url, headers=headers, params={"query": query, "per_page": count}, timeout=30)
    resp.raise_for_status()
    results = resp.json().get("results", [])
    return [r["urls"]["regular"] for r in results[:count]]


def download_and_store_photo(image_url):
    """Download an Unsplash photo and upload it to the DigitalOcean Spaces bucket the app
    already reads images from (files/{id}(-large|-medium|-small).jpg), so it renders through
    the site's existing Img.Url() helper with no other code changes needed."""
    resp = requests.get(image_url, timeout=30)
    resp.raise_for_status()

    file_id = str(uuid.uuid4())
    from PIL import Image
    import io

    source = Image.open(io.BytesIO(resp.content)).convert("RGB")

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

    upload_variant(source, f"files/{file_id}.jpg")
    upload_variant(_resized(source, 1000), f"files/{file_id}-large.jpg")
    upload_variant(_resized(source, 600), f"files/{file_id}-medium.jpg")
    upload_variant(_resized(source, 300), f"files/{file_id}-small.jpg")

    return file_id


def _resized(img, max_height):
    ratio = max_height / img.height
    new_width = int(img.width * ratio)
    return img.resize((new_width, max_height))


TRANSLIT_MAP = {
    "а": "a", "б": "b", "в": "v", "г": "h", "ґ": "g", "д": "d", "е": "e", "є": "ie",
    "ж": "zh", "з": "z", "и": "y", "і": "i", "ї": "i", "й": "y", "к": "k", "л": "l",
    "м": "m", "н": "n", "о": "o", "п": "p", "р": "r", "с": "s", "т": "t", "у": "u",
    "ф": "f", "х": "kh", "ц": "ts", "ч": "ch", "ш": "sh", "щ": "shch", "ь": "",
    "ъ": "", "ы": "y", "э": "e", "ю": "iu", "я": "ia", "ё": "yo",
}


def translit(text):
    text = text.lower().strip()
    result = "".join(TRANSLIT_MAP.get(ch, ch) for ch in text)
    result = re.sub(r"[^a-z0-9\s-]", "", result)
    result = re.sub(r"\s+", "-", result.strip())
    return result


def unique_url(db, title):
    base = translit(title) or f"article-{uuid.uuid4().hex[:8]}"
    cur = db.cursor()
    cur.execute("SELECT Url FROM Pages")
    existing = {row[0] for row in cur.fetchall()}
    cur.close()

    candidate = base
    attempt = 1
    while candidate in existing:
        candidate = f"{base}-{attempt}"
        attempt += 1
    return candidate


def localizable_json(uk_value, ru_value):
    return json.dumps(
        {"Values": [{"Locale": "uk", "Value": uk_value}, {"Locale": "ru", "Value": ru_value}]},
        ensure_ascii=False,
    )


def publish_article(db, article, photo_filename):
    page_id = str(uuid.uuid4())
    url = unique_url(db, article["title"]["uk"])

    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO Pages
            (Id, Category, Content, Photo, SeoDescription, SeoKeywords, SeoText, SeoTitle, ShortContent, Status, Timestamp, Title, Url)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
        """,
        (
            page_id,
            1,
            localizable_json(article["content"]["uk"], article["content"]["ru"]),
            photo_filename,
            localizable_json(article["seo_description"]["uk"], article["seo_description"]["ru"]),
            localizable_json(article["seo_keywords"]["uk"], article["seo_keywords"]["ru"]),
            localizable_json("", ""),
            localizable_json(article["seo_title"]["uk"], article["seo_title"]["ru"]),
            localizable_json(article["short_content"]["uk"], article["short_content"]["ru"]),
            1,
            datetime.now(),
            localizable_json(article["title"]["uk"], article["title"]["ru"]),
            url,
        ),
    )
    db.commit()
    cur.close()
    return page_id


def main():
    db = get_db()
    try:
        build_content_plan(db)
        next_item = pick_next_keyword(db)
        if not next_item:
            print("No keywords available in content plan, skipping today.")
            return

        plan_id, keyword = next_item
        print(f"Generating article for keyword: {keyword}")

        article = generate_article(keyword)

        photo_query = article.get("photo_search_query", keyword)
        photo_urls = search_unsplash_photos(photo_query, count=1)
        photo_filename = None
        if photo_urls:
            photo_filename = download_and_store_photo(photo_urls[0])

        page_id = publish_article(db, article, photo_filename)
        mark_keyword_used(db, plan_id, page_id)

        print(f"Published page {page_id} for keyword '{keyword}'")
    finally:
        db.close()


if __name__ == "__main__":
    main()

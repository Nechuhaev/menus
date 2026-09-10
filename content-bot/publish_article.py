#!/usr/bin/env python3
"""
Content combine: plans topics via Serpstat, generates bilingual articles via Claude
(falling back to OpenRouter, then Gemini directly if Claude is unavailable), picks
photos via Unsplash (falling back to Pexels, then Pixabay), and publishes to the
Pages table (Blog/News category) with internal links to related existing articles.

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
OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY")
GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
CLOUDFLARE_ACCOUNT_ID = os.environ.get("CLOUDFLARE_ACCOUNT_ID")
CLOUDFLARE_API_TOKEN = os.environ.get("CLOUDFLARE_API_TOKEN")
GEMINI_KEY = os.environ.get("GEMINI_KEY")
SERPSTAT_API_KEY = os.environ["SERPSTAT_API_KEY"]
UNSPLASH_ACCESS_KEY = os.environ.get("UNSPLASH_ACCESS_KEY")
PEXELS_KEY = os.environ.get("PEXELS_KEY")
PIXABAY_KEY = os.environ.get("PIXABAY_KEY")
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


def call_llm(prompt, max_tokens):
    """Try Claude first; fall back to OpenRouter, then Groq, then Cloudflare
    Workers AI, then Gemini directly (Gemini last — its free tier caps at only
    20 requests/day, the most restrictive of the five)."""
    last_error = None

    try:
        message = anthropic_client.messages.create(
            model="claude-sonnet-4-5",
            max_tokens=max_tokens,
            messages=[{"role": "user", "content": prompt}],
        )
        return message.content[0].text
    except Exception as e:
        last_error = e
        print(f"    [LLM] Claude failed, trying OpenRouter: {e}")

    if OPENROUTER_API_KEY:
        try:
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
            content = resp.json()["choices"][0]["message"]["content"]
            if not content:
                raise ValueError("OpenRouter returned empty content")
            return content
        except Exception as e:
            last_error = e
            print(f"    [LLM] OpenRouter failed, trying Groq: {e}")

    if GROQ_API_KEY:
        try:
            resp = requests.post(
                "https://api.groq.com/openai/v1/chat/completions",
                headers={"Authorization": f"Bearer {GROQ_API_KEY}", "Content-Type": "application/json"},
                json={
                    "model": "llama-3.3-70b-versatile",
                    "messages": [{"role": "user", "content": prompt}],
                    "max_tokens": max_tokens,
                },
                timeout=60,
            )
            if resp.status_code >= 400:
                print(f"    [LLM] Groq HTTP {resp.status_code}: {resp.text[:300]}")
            resp.raise_for_status()
            content = resp.json()["choices"][0]["message"]["content"]
            if not content:
                raise ValueError("Groq returned empty content")
            return content
        except Exception as e:
            last_error = e
            print(f"    [LLM] Groq failed, trying Cloudflare: {e}")

    if CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN:
        try:
            resp = requests.post(
                f"https://api.cloudflare.com/client/v4/accounts/{CLOUDFLARE_ACCOUNT_ID}/ai/v1/chat/completions",
                headers={"Authorization": f"Bearer {CLOUDFLARE_API_TOKEN}", "Content-Type": "application/json"},
                json={
                    "model": "@cf/meta/llama-3.3-70b-instruct",
                    "messages": [{"role": "user", "content": prompt}],
                    "max_tokens": max_tokens,
                },
                timeout=60,
            )
            if resp.status_code >= 400:
                print(f"    [LLM] Cloudflare HTTP {resp.status_code}: {resp.text[:300]}")
            resp.raise_for_status()
            content = resp.json()["choices"][0]["message"]["content"]
            if not content:
                raise ValueError("Cloudflare returned empty content")
            return content
        except Exception as e:
            last_error = e
            print(f"    [LLM] Cloudflare failed, trying Gemini: {e}")

    if GEMINI_KEY:
        try:
            from google import genai
            from google.genai import types as genai_types
            gemini_client = genai.Client(api_key=GEMINI_KEY)
            response = gemini_client.models.generate_content(
                model="gemini-3.6-flash",
                contents=prompt,
                config=genai_types.GenerateContentConfig(max_output_tokens=max_tokens + 500),
            )
            return response.text
        except Exception as e:
            last_error = e
            print(f"    [LLM] Gemini failed too: {e}")

    raise last_error


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
    raw = call_llm(ARTICLE_PROMPT.format(keyword=keyword), max_tokens=16000).strip()
    raw = re.sub(r"^```json\s*|\s*```$", "", raw)
    return json.loads(raw)


def search_unsplash_photo(query):
    if not UNSPLASH_ACCESS_KEY:
        return None
    try:
        url = "https://api.unsplash.com/search/photos"
        headers = {"Authorization": f"Client-ID {UNSPLASH_ACCESS_KEY}"}
        resp = requests.get(url, headers=headers, params={"query": query, "per_page": 1}, timeout=30)
        resp.raise_for_status()
        results = resp.json().get("results", [])
        if not results:
            return None
        image_resp = requests.get(results[0]["urls"]["regular"], timeout=30)
        image_resp.raise_for_status()
        return image_resp.content
    except Exception as e:
        print(f"    Unsplash search failed: {e}")
        return None


def search_pexels_photo(query):
    if not PEXELS_KEY:
        return None
    try:
        resp = requests.get(
            "https://api.pexels.com/v1/search",
            headers={"Authorization": PEXELS_KEY},
            params={"query": query, "per_page": 1},
            timeout=30,
        )
        resp.raise_for_status()
        photos = resp.json().get("photos", [])
        if not photos:
            return None
        image_resp = requests.get(photos[0]["src"]["large"], timeout=30)
        image_resp.raise_for_status()
        return image_resp.content
    except Exception as e:
        print(f"    Pexels search failed: {e}")
        return None


def search_pixabay_photo(query):
    if not PIXABAY_KEY:
        return None
    try:
        resp = requests.get(
            "https://pixabay.com/api/",
            params={"key": PIXABAY_KEY, "q": query, "image_type": "photo", "per_page": 3},
            timeout=30,
        )
        resp.raise_for_status()
        hits = resp.json().get("hits", [])
        if not hits:
            return None
        image_resp = requests.get(hits[0]["largeImageURL"], timeout=30)
        image_resp.raise_for_status()
        return image_resp.content
    except Exception as e:
        print(f"    Pixabay search failed: {e}")
        return None


def get_article_photo(query):
    """Unsplash first, then Pexels, then Pixabay, so a missing/rate-limited key on
    one provider doesn't leave the article without a photo."""
    for search_fn, name in (
        (search_unsplash_photo, "Unsplash"),
        (search_pexels_photo, "Pexels"),
        (search_pixabay_photo, "Pixabay"),
    ):
        image_bytes = search_fn(query)
        if image_bytes:
            return image_bytes
        print(f"    {name} had no result for '{query}', trying next source")
    return None


def upload_photo_to_spaces(image_bytes):
    from PIL import Image
    import io

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


def fetch_published_articles(db, exclude_title=None):
    """Fetch existing published blog articles (Url + bilingual titles) for internal linking."""
    cur = db.cursor()
    cur.execute("SELECT Url, Title FROM Pages WHERE Category = 1 AND Status = 1")
    rows = cur.fetchall()
    cur.close()

    articles = []
    for url, title_json in rows:
        try:
            parsed = json.loads(title_json)
            titles = {v["Locale"]: v["Value"] for v in parsed.get("Values", [])}
        except (json.JSONDecodeError, TypeError):
            continue
        if titles.get("uk") == exclude_title:
            continue
        articles.append({"url": url, "titles": titles})
    return articles


STOPWORDS = {
    "де", "як", "що", "для", "від", "в", "на", "з", "і", "та", "або", "цей", "яка", "який",
    "где", "как", "что", "для", "от", "в", "на", "с", "и", "или", "этот", "какая", "какой",
    "the", "a", "an", "in", "on", "of", "for", "to",
}


def _keywords(text):
    words = re.findall(r"[а-щьюяєіїґ'a-z]+", text.lower())
    return {w for w in words if len(w) > 3 and w not in STOPWORDS}


def pick_related_articles(candidates, new_title_uk, new_keywords_uk, count=3):
    """Score existing articles by keyword overlap with the new article's title/keywords."""
    target_words = _keywords(new_title_uk) | _keywords(new_keywords_uk or "")
    scored = []
    for article in candidates:
        title_uk = article["titles"].get("uk", "")
        overlap = len(_keywords(title_uk) & target_words)
        if overlap > 0:
            scored.append((overlap, article))
    scored.sort(key=lambda x: x[0], reverse=True)
    return [a for _, a in scored[:count]]


LINK_SENTENCES = {
    "uk": 'Читайте також: <a href="/uk/news/{url}">{title}</a>.',
    "ru": 'Читайте также: <a href="/ru/news/{url}">{title}</a>.',
}


def insert_internal_links(html_content, related_articles, locale):
    """Insert 2-3 natural internal links after paragraphs spread through the article body."""
    if not related_articles:
        return html_content

    paragraphs = html_content.split("</p>")
    if len(paragraphs) < 3:
        return html_content

    usable_positions = list(range(1, len(paragraphs) - 1))
    step = max(1, len(usable_positions) // (len(related_articles) + 1))
    insertion_points = usable_positions[step - 1 :: step][: len(related_articles)]

    for idx, pos in zip(insertion_points, related_articles):
        title = pos["titles"].get(locale) or pos["titles"].get("uk", "")
        sentence = LINK_SENTENCES[locale].format(url=pos["url"], title=title)
        paragraphs[idx] += f"</p><p>{sentence}</p>"

    return "</p>".join(paragraphs)


def localizable_json(uk_value, ru_value):
    return json.dumps(
        {"Values": [{"Locale": "uk", "Value": uk_value}, {"Locale": "ru", "Value": ru_value}]},
        ensure_ascii=False,
    )


def publish_article(db, article, photo_filename):
    page_id = str(uuid.uuid4())
    url = unique_url(db, article["title"]["uk"])

    candidates = fetch_published_articles(db, exclude_title=article["title"]["uk"])
    related = pick_related_articles(candidates, article["title"]["uk"], article["seo_keywords"].get("uk"))

    content_uk = insert_internal_links(article["content"]["uk"], related, "uk")
    content_ru = insert_internal_links(article["content"]["ru"], related, "ru")

    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO Pages
            (Id, Category, Content, Photo, SeoDescription, SeoKeywords, SeoText, SeoTitle, ShortContent, Status, Timestamp, Title, Url)
        VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)
        """,
        (
            page_id,
            1,  # PageCategory.News
            localizable_json(content_uk, content_ru),
            photo_filename,
            localizable_json(article["seo_description"]["uk"], article["seo_description"]["ru"]),
            localizable_json(article["seo_keywords"]["uk"], article["seo_keywords"]["ru"]),
            localizable_json("", ""),
            localizable_json(article["seo_title"]["uk"], article["seo_title"]["ru"]),
            localizable_json(article["short_content"]["uk"], article["short_content"]["ru"]),
            1,  # PageStatus.Published
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
        image_bytes = get_article_photo(photo_query)
        photo_filename = upload_photo_to_spaces(image_bytes) if image_bytes else None

        page_id = publish_article(db, article, photo_filename)
        mark_keyword_used(db, plan_id, page_id)

        print(f"Published page {page_id} for keyword '{keyword}'")
    finally:
        db.close()


if __name__ == "__main__":
    main()

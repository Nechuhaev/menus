#!/usr/bin/env python3
"""
Content re-optimization: audits already-published blog articles against an SEO checklist
(title/description length, keyword presence, heading structure, internal links, photo
presence) and fixes whatever falls short. LLM calls try Claude, then OpenRouter, then
Gemini directly. A missing photo is filled via Unsplash, then Pexels, then Pixabay.
Missing internal links are inserted using real links to related existing articles
(not left to the LLM to invent).

Run weekly via cron.
"""
import json
import os
import re
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
GEMINI_KEY = os.environ.get("GEMINI_KEY")
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

BATCH_SIZE = 5
RECHECK_AFTER_DAYS = 30

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)


def call_llm(prompt, max_tokens):
    """Try Claude first; fall back to OpenRouter, then Gemini directly."""
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
            print(f"    [LLM] OpenRouter failed, trying Gemini: {e}")

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


def ensure_optimization_log_table(db):
    cur = db.cursor()
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS ContentOptimizationLog (
            PageId VARCHAR(255) PRIMARY KEY,
            LastCheckedAt DATETIME NOT NULL,
            IssuesFound TEXT NULL,
            Fixed TINYINT(1) NOT NULL DEFAULT 0
        )
        """
    )
    db.commit()
    cur.close()


def parse_localizable(json_str):
    if not json_str:
        return {}
    try:
        parsed = json.loads(json_str)
        return {v["Locale"]: v["Value"] for v in parsed.get("Values", [])}
    except (json.JSONDecodeError, TypeError):
        return {}


def localizable_json(values_by_locale):
    return json.dumps(
        {"Values": [{"Locale": k, "Value": v} for k, v in values_by_locale.items()]},
        ensure_ascii=False,
    )


def fetch_articles_due_for_check(db, batch_size):
    ensure_optimization_log_table(db)
    cur = db.cursor()
    cur.execute(
        """
        SELECT p.Id, p.Title, p.Content, p.SeoTitle, p.SeoDescription, p.SeoKeywords, p.Url, p.Photo
        FROM Pages p
        LEFT JOIN ContentOptimizationLog l ON l.PageId = p.Id
        WHERE p.Category = 1 AND p.Status = 1
          AND (l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL %s DAY))
        ORDER BY l.LastCheckedAt IS NOT NULL, l.LastCheckedAt ASC
        LIMIT %s
        """,
        (RECHECK_AFTER_DAYS, batch_size),
    )
    rows = cur.fetchall()
    cur.close()
    return rows


def fetch_published_articles(db, exclude_url=None):
    cur = db.cursor()
    cur.execute("SELECT Url, Title FROM Pages WHERE Category = 1 AND Status = 1")
    rows = cur.fetchall()
    cur.close()

    articles = []
    for url, title_json in rows:
        if url == exclude_url:
            continue
        titles = parse_localizable(title_json)
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


def pick_related_articles(candidates, title_uk, count=3):
    target_words = _keywords(title_uk)
    scored = []
    for article in candidates:
        overlap = len(_keywords(article["titles"].get("uk", "")) & target_words)
        if overlap > 0:
            scored.append((overlap, article))
    scored.sort(key=lambda x: x[0], reverse=True)
    return [a for _, a in scored[:count]]


LINK_SENTENCES = {
    "uk": 'Читайте також: <a href="/uk/news/{url}">{title}</a>.',
    "ru": 'Читайте также: <a href="/ru/news/{url}">{title}</a>.',
}


def insert_internal_links(html_content, related_articles, locale):
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


def search_unsplash_photo(query):
    if not UNSPLASH_ACCESS_KEY:
        return None
    try:
        resp = requests.get(
            "https://api.unsplash.com/search/photos",
            headers={"Authorization": f"Client-ID {UNSPLASH_ACCESS_KEY}"},
            params={"query": query, "per_page": 1},
            timeout=30,
        )
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


def strip_html(html):
    text = re.sub(r"<[^>]+>", " ", html or "")
    return re.sub(r"\s+", " ", text).strip()


def audit_locale(seo_title, seo_description, seo_keywords, content_html):
    issues = []
    plain_text = strip_html(content_html)

    if not seo_title or not (30 <= len(seo_title) <= 60):
        issues.append(f"seo_title length is {len(seo_title or '')} chars (target 30-60)")
    if not seo_description or not (120 <= len(seo_description) <= 160):
        issues.append(f"seo_description length is {len(seo_description or '')} chars (target 120-160)")
    if not seo_keywords or len(seo_keywords.split(",")) < 3:
        issues.append("seo_keywords has fewer than 3 keyword phrases")
    if "<h2" not in content_html and "<h3" not in content_html:
        issues.append("no H2/H3 subheadings found in content")
    if len(plain_text) < 2000:
        issues.append(f"content is only {len(plain_text)} chars (target 2000+)")
    if content_html.count("<a href") < 1:
        issues.append("no internal/outbound links found in content")

    return issues


FIX_PROMPT = """Ти — SEO-редактор для сайту tipmymenu.com. Ось стаття, яка не повністю відповідає SEO-чеклисту.

Заголовок: {title}
Поточний SEO title: {seo_title}
Поточний SEO description: {seo_description}
Поточні SEO keywords: {seo_keywords}

Знайдені проблеми:
{issues}

Поточний контент (HTML):
{content}

Виправ ЛИШЕ те, що зазначено в проблемах (окрім відсутності внутрішніх посилань — це буде додано окремо автоматично, не турбуйся про це) — не переписуй статтю з нуля, збережи стиль і сенс, онови тільки метадані та за потреби додай/поправ структуру (h2/h3), не змінюючи фактичний зміст.

Поверни ЛИШЕ валідний JSON без жодного тексту до чи після:
{{
  "seo_title": "...",
  "seo_description": "...",
  "seo_keywords": "...",
  "content": "<p>...</p>"
}}
"""


def fix_article_locale(title, seo_title, seo_description, seo_keywords, content_html, issues):
    prompt = FIX_PROMPT.format(
        title=title,
        seo_title=seo_title or "(відсутній)",
        seo_description=seo_description or "(відсутній)",
        seo_keywords=seo_keywords or "(відсутні)",
        issues="\n".join(f"- {i}" for i in issues),
        content=content_html,
    )
    raw = call_llm(prompt, max_tokens=16000).strip()
    raw = re.sub(r"^```json\s*|\s*```$", "", raw)
    return json.loads(raw)


def log_check(db, page_id, issues_summary, fixed):
    cur = db.cursor()
    cur.execute(
        """
        INSERT INTO ContentOptimizationLog (PageId, LastCheckedAt, IssuesFound, Fixed)
        VALUES (%s, %s, %s, %s)
        ON DUPLICATE KEY UPDATE LastCheckedAt = VALUES(LastCheckedAt), IssuesFound = VALUES(IssuesFound), Fixed = VALUES(Fixed)
        """,
        (page_id, datetime.now(), issues_summary, 1 if fixed else 0),
    )
    db.commit()
    cur.close()


def optimize_article(db, page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url, photo):
    titles = parse_localizable(title_json)
    contents = parse_localizable(content_json)
    seo_titles = parse_localizable(seo_title_json)
    seo_descriptions = parse_localizable(seo_description_json)
    seo_keywords_map = parse_localizable(seo_keywords_json)

    all_issues = {}
    for locale in ("uk", "ru"):
        issues = audit_locale(
            seo_titles.get(locale, ""),
            seo_descriptions.get(locale, ""),
            seo_keywords_map.get(locale, ""),
            contents.get(locale, ""),
        )
        if issues:
            all_issues[locale] = issues

    needs_photo = not photo
    needs_links = any("no internal/outbound links" in i for issues in all_issues.values() for i in issues)

    if not all_issues and not needs_photo:
        log_check(db, page_id, "no issues found", fixed=False)
        print(f"  {url}: OK, no issues")
        return

    print(f"  {url}: issues found -> {all_issues}; needs_photo={needs_photo}")

    new_seo_titles = dict(seo_titles)
    new_seo_descriptions = dict(seo_descriptions)
    new_seo_keywords = dict(seo_keywords_map)
    new_contents = dict(contents)

    for locale, issues in all_issues.items():
        try:
            fix = fix_article_locale(
                titles.get(locale, ""),
                seo_titles.get(locale, ""),
                seo_descriptions.get(locale, ""),
                seo_keywords_map.get(locale, ""),
                contents.get(locale, ""),
                issues,
            )
            new_seo_titles[locale] = fix.get("seo_title", seo_titles.get(locale, ""))
            new_seo_descriptions[locale] = fix.get("seo_description", seo_descriptions.get(locale, ""))
            new_seo_keywords[locale] = fix.get("seo_keywords", seo_keywords_map.get(locale, ""))
            new_contents[locale] = fix.get("content", contents.get(locale, ""))
        except Exception as e:
            print(f"    Failed to fix locale {locale}: {e}")

    if needs_links:
        candidates = fetch_published_articles(db, exclude_url=url)
        related = pick_related_articles(candidates, titles.get("uk", ""))
        if related:
            for locale in ("uk", "ru"):
                new_contents[locale] = insert_internal_links(new_contents.get(locale, ""), related, locale)

    new_photo = photo
    if needs_photo:
        query = seo_keywords_map.get("uk", "") or titles.get("uk", "") or "restaurant food"
        query = query.split(",")[0].strip() or "restaurant food"
        image_bytes = get_article_photo(query)
        if image_bytes:
            try:
                new_photo = upload_photo_to_spaces(image_bytes)
            except Exception as e:
                print(f"    Photo upload failed: {e}")

    cur = db.cursor()
    cur.execute(
        """
        UPDATE Pages
        SET SeoTitle = %s, SeoDescription = %s, SeoKeywords = %s, Content = %s, Photo = %s, Timestamp = %s
        WHERE Id = %s
        """,
        (
            localizable_json(new_seo_titles),
            localizable_json(new_seo_descriptions),
            localizable_json(new_seo_keywords),
            localizable_json(new_contents),
            new_photo,
            datetime.now(),
            page_id,
        ),
    )
    db.commit()
    cur.close()

    summary_parts = [f"{loc}: {', '.join(iss)}" for loc, iss in all_issues.items()]
    if needs_photo:
        summary_parts.append("photo added" if new_photo != photo else "photo search failed")
    log_check(db, page_id, "; ".join(summary_parts), fixed=True)


def main():
    db = get_db()
    try:
        articles = fetch_articles_due_for_check(db, BATCH_SIZE)
        if not articles:
            print("No articles due for re-optimization check.")
            return

        print(f"Checking {len(articles)} article(s)...")
        for page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url, photo in articles:
            optimize_article(db, page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url, photo)
    finally:
        db.close()


if __name__ == "__main__":
    main()

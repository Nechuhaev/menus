#!/usr/bin/env python3
"""
Content re-optimization: audits already-published blog articles against an SEO checklist
(title/description length, keyword presence, heading structure, internal links) and asks
Claude to fix whatever falls short.

Run weekly via cron.
"""
import json
import os
import re
from datetime import datetime

import mysql.connector
from anthropic import Anthropic
from dotenv import load_dotenv

load_dotenv()

CLAUDE_API_KEY = os.environ["CLAUDE_API_KEY"]
DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

BATCH_SIZE = 5
RECHECK_AFTER_DAYS = 30

anthropic_client = Anthropic(api_key=CLAUDE_API_KEY)


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
        SELECT p.Id, p.Title, p.Content, p.SeoTitle, p.SeoDescription, p.SeoKeywords, p.Url
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


def strip_html(html):
    text = re.sub(r"<[^>]+>", " ", html or "")
    return re.sub(r"\s+", " ", text).strip()


def audit_locale(title, seo_title, seo_description, seo_keywords, content_html):
    """Rule-based checks, no AI call needed for detection."""
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

Виправ ЛИШЕ те, що зазначено в проблемах — не переписуй статтю з нуля, збережи стиль і сенс, онови тільки метадані та за потреби додай/поправ структуру (h2/h3), не змінюючи фактичний зміст.

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
    message = anthropic_client.messages.create(
        model="claude-sonnet-4-5",
        max_tokens=16000,
        messages=[{"role": "user", "content": prompt}],
    )
    raw = message.content[0].text.strip()
    raw = re.sub(r"^```json\s*|\s*```$", "", raw.strip())
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


def optimize_article(db, page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url):
    titles = parse_localizable(title_json)
    contents = parse_localizable(content_json)
    seo_titles = parse_localizable(seo_title_json)
    seo_descriptions = parse_localizable(seo_description_json)
    seo_keywords_map = parse_localizable(seo_keywords_json)

    all_issues = {}
    for locale in ("uk", "ru"):
        issues = audit_locale(
            titles.get(locale, ""),
            seo_titles.get(locale, ""),
            seo_descriptions.get(locale, ""),
            seo_keywords_map.get(locale, ""),
            contents.get(locale, ""),
        )
        if issues:
            all_issues[locale] = issues

    if not all_issues:
        log_check(db, page_id, "no issues found", fixed=False)
        print(f"  {url}: OK, no issues")
        return

    print(f"  {url}: issues found -> {all_issues}")

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

    cur = db.cursor()
    cur.execute(
        """
        UPDATE Pages
        SET SeoTitle = %s, SeoDescription = %s, SeoKeywords = %s, Content = %s, Timestamp = %s
        WHERE Id = %s
        """,
        (
            localizable_json(new_seo_titles),
            localizable_json(new_seo_descriptions),
            localizable_json(new_seo_keywords),
            localizable_json(new_contents),
            datetime.now(),
            page_id,
        ),
    )
    db.commit()
    cur.close()

    summary = "; ".join(f"{loc}: {', '.join(iss)}" for loc, iss in all_issues.items())
    log_check(db, page_id, summary, fixed=True)


def main():
    db = get_db()
    try:
        articles = fetch_articles_due_for_check(db, BATCH_SIZE)
        if not articles:
            print("No articles due for re-optimization check.")
            return

        print(f"Checking {len(articles)} article(s)...")
        for page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url in articles:
            optimize_article(db, page_id, title_json, content_json, seo_title_json, seo_description_json, seo_keywords_json, url)
    finally:
        db.close()


if __name__ == "__main__":
    main()

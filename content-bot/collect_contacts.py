"""Контакти власників для розсилки: email і Telegram із сайтів ресторанів, без AI.

Поле Email у ресторанах здебільшого порожнє. Скрипт відкриває сайт ресторану (головну й до двох сторінок
«Контакти»/«Про нас»), бере адреси з mailto: і тексту та посилання t.me / telegram.me і пише їх у
RestaurantContacts. Restaurants.Email не змінюється. Адмінка «Розсилка» бере email звідси й з поля Email,
а Telegram-контакти показує для ручного надсилання (бот не може написати людині першим).

Запуск:  python3 collect_contacts.py --dry-run          # показати знайдене, нічого не записувати
         python3 collect_contacts.py                    # записати (вже перевірені сайти пропускаються)
         python3 collect_contacts.py --recheck          # перевірити всі сайти заново
"""
import argparse
import html
import re
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from urllib.parse import urljoin, urlparse

import requests

from sync_restaurants import get_db

TABLE_SQL = """CREATE TABLE IF NOT EXISTS RestaurantContacts (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    Kind VARCHAR(16) NOT NULL,
    Value VARCHAR(255) NOT NULL,
    SourceUrl TEXT NULL,
    CreatedAt DATETIME NOT NULL,
    UNIQUE KEY UX_RestaurantContacts (RestaurantId, Kind, Value)
) DEFAULT CHARSET=utf8mb4"""
CHECKED_SQL = """CREATE TABLE IF NOT EXISTS RestaurantContactsChecked (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    CheckedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""

HEADERS = {"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0; +https://tipmymenu.com)"}
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,24}")
TELEGRAM = re.compile(r"(?:https?://)?(?:t\.me|telegram\.me)/([A-Za-z0-9_]{5,32})(?![A-Za-z0-9_/])", re.I)
# службові адреси й «адреси» з імен файлів (logo@2x.png), конструкторів сайтів і трекерів
BAD_EMAIL = re.compile(r"\.(png|jpe?g|gif|svg|webp|css|js)$|@(example|domain|email|sentry|wixpress|sentry-next|"
                       r"godaddy|mysite)\.|^(no-?reply|noreply|test|user|name|email|your)@", re.I)
BAD_TELEGRAM = {"share", "joinchat", "addstickers", "iv", "proxy", "socks", "telegram", "durov"}
CONTACT_LINK = re.compile(r"contact|kontakt|контакт|about|про-нас|o-nas|о-нас", re.I)
MAX_EXTRA_PAGES = 2


def get(url):
    try:
        r = requests.get(url, timeout=15, headers=HEADERS)
        if r.status_code == 200 and "html" in r.headers.get("Content-Type", "html"):
            return r.text
    except requests.RequestException:
        pass
    return ""


def extract(page, url):
    text = html.unescape(page)
    emails = {m.lower().rstrip(".") for m in EMAIL.findall(text) if not BAD_EMAIL.search(m)}
    telegram = {m for m in TELEGRAM.findall(text) if m.lower() not in BAD_TELEGRAM and not m.lower().endswith("bot")}
    return {("email", e, url) for e in emails} | {("telegram", "t.me/" + t, url) for t in telegram}


def contacts_of(site):
    """Контакти з головної сторінки сайту й до двох сторінок «Контакти»/«Про нас» того ж домену."""
    base = site if site.startswith("http") else "https://" + site
    page = get(base)
    if not page:
        return set()
    found = extract(page, base)
    host = urlparse(base).netloc.lower()
    links = []
    for href in re.findall(r'href=["\']([^"\'#]+)', page):
        url = urljoin(base, html.unescape(href))
        if urlparse(url).netloc.lower() == host and CONTACT_LINK.search(url) and url not in links:
            links.append(url)
    for url in links[:MAX_EXTRA_PAGES]:
        found |= extract(get(url), url)
    return found


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--recheck", action="store_true", help="перевірити й ті сайти, що вже перевірені")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(TABLE_SQL)
    cur.execute(CHECKED_SQL)
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, ri.Website FROM Restaurants r
                   JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   LEFT JOIN RestaurantContactsChecked c ON c.RestaurantId = r.Id
                   WHERE r.Status = 0 AND ri.Website IS NOT NULL AND ri.Website <> ''"""
                + ("" if args.recheck else " AND c.RestaurantId IS NULL"))
    rows = cur.fetchall()
    # мережа з одним сайтом - сайт читається один раз
    sites = sorted({r["Website"].strip() for r in rows})
    print(f"ресторанів із сайтом до перевірки: {len(rows)}, різних сайтів: {len(sites)}")
    with ThreadPoolExecutor(8) as pool:
        by_site = dict(zip(sites, pool.map(contacts_of, sites)))

    now = datetime.now()
    write = db.cursor()
    stats = {"email": 0, "telegram": 0}
    with_any = 0
    for r in rows:
        found = by_site.get(r["Website"].strip(), set())
        with_any += bool(found)
        for kind, value, source in sorted(found):
            stats[kind] += 1
            if args.dry_run:
                print(f"  {r['Name'][:40]}: {kind} {value}")
            else:
                write.execute("""INSERT IGNORE INTO RestaurantContacts (RestaurantId, Kind, Value, SourceUrl, CreatedAt)
                                 VALUES (%s, %s, %s, %s, %s)""", (r["Id"], kind, value, source[:1000], now))
        if not args.dry_run:
            write.execute("""INSERT INTO RestaurantContactsChecked (RestaurantId, CheckedAt) VALUES (%s, %s)
                             ON DUPLICATE KEY UPDATE CheckedAt = VALUES(CheckedAt)""", (r["Id"], now))
    if not args.dry_run:
        db.commit()
    db.close()
    print(f"\nз контактами: {with_any} із {len(rows)}; email: {stats['email']}, telegram: {stats['telegram']}"
          f"{' (dry-run, нічого не записано)' if args.dry_run else ''}")


if __name__ == "__main__":
    main()

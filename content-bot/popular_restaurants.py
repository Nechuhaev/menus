"""«Популярні заклади міста» на головній - список з Search Console (API, сервісний акаунт), без AI.

Беруться сторінки ресторанів, що за останні 4 тижні мають багато показів у Google, але стоять на позиціях 5-12:
до першої п'ятірки їм бракує небагато, а посилання з головної (найсильнішої сторінки сайту) підтягує їх вище.
Покази uk- і ru-сторінки ресторану складаються, позиція - середня, зважена за показами. На місто - до TOP_PER_CITY
закладів, від найбільших показів. Пише таблицю PopularRestaurants (сайт кешує її на 10 хвилин).

Налаштування (один раз): сервісний акаунт Google з ключем JSON, доданий у Search Console користувачем
(«Обмежений» доступ); шлях до ключа - GSC_KEY_FILE у .env (за замовчуванням /root/gsc-key.json),
ресурс - GSC_SITE (за замовчуванням https://tipmymenu.com/). Пакет: venv/bin/pip install google-auth

Запуск:  python3 popular_restaurants.py --dry-run   # показати список, нічого не записувати
         python3 popular_restaurants.py             # записати (cron раз на тиждень)
"""
import argparse
import os
import re
from collections import defaultdict
from datetime import date, timedelta
from urllib.parse import quote, unquote, urlparse

from google.auth.transport.requests import AuthorizedSession
from google.oauth2 import service_account

from sync_restaurants import get_db

KEY_FILE = os.environ.get("GSC_KEY_FILE", "/root/gsc-key.json")
SITE = os.environ.get("GSC_SITE", "https://tipmymenu.com/")
MIN_POSITION, MAX_POSITION = 4.5, 12.5
MIN_IMPRESSIONS = 100
TOP_PER_CITY = 8
# /uk/kyiv/restaurant/musafyr - сторінка ресторану (без страви)
RESTAURANT_PAGE = re.compile(r"^/(?:uk|ru|en)/[^/]+/restaurant/([^/]+)/?$")

TABLE_SQL = """CREATE TABLE IF NOT EXISTS PopularRestaurants (
    CityId VARCHAR(255) NOT NULL,
    RestaurantId VARCHAR(255) NOT NULL,
    Position INT NOT NULL,
    Impressions INT NOT NULL,
    Clicks INT NOT NULL,
    AvgPosition DECIMAL(6,2) NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    PRIMARY KEY (CityId, RestaurantId)
) DEFAULT CHARSET=utf8mb4"""


def gsc_pages():
    """[(адреса, кліки, покази, позиція)] за останні 28 днів (без 3 останніх - дані ще неповні)."""
    credentials = service_account.Credentials.from_service_account_file(
        KEY_FILE, scopes=["https://www.googleapis.com/auth/webmasters.readonly"])
    session = AuthorizedSession(credentials)
    url = f"https://searchconsole.googleapis.com/webmasters/v3/sites/{quote(SITE, safe='')}/searchAnalytics/query"
    end = date.today() - timedelta(days=3)
    rows, start_row = [], 0
    while True:
        resp = session.post(url, json={"startDate": str(end - timedelta(days=27)), "endDate": str(end),
                                       "dimensions": ["page"], "rowLimit": 25000, "startRow": start_row}, timeout=60)
        resp.raise_for_status()
        batch = resp.json().get("rows", [])
        rows += [(r["keys"][0], r["clicks"], r["impressions"], r["position"]) for r in batch]
        if len(batch) < 25000:
            return rows
        start_row += 25000


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    stats = defaultdict(lambda: [0, 0, 0.0])  # slug -> кліки, покази, сума позиція*покази
    for page, clicks, impressions, position in gsc_pages():
        m = RESTAURANT_PAGE.match(unquote(urlparse(page).path))
        if m:
            s = stats[m.group(1).lower()]
            s[0] += clicks
            s[1] += impressions
            s[2] += position * impressions

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(TABLE_SQL)
    cur.execute("""SELECT LOWER(rs.Url) AS Slug, LOWER(CONCAT(r.Id, '')) AS Id, r.Name, CONCAT(r.City, '') AS City
                   FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id WHERE r.Status = 0""")
    restaurants = {row["Slug"]: row for row in cur.fetchall()}

    by_city = defaultdict(list)
    for slug, (clicks, impressions, weighted) in stats.items():
        r = restaurants.get(slug)
        position = weighted / impressions if impressions else 0
        if r and impressions >= MIN_IMPRESSIONS and MIN_POSITION <= position <= MAX_POSITION:
            by_city[r["City"]].append((impressions, clicks, position, r))

    rows = []
    for city, items in by_city.items():
        for rank, (impressions, clicks, position, r) in enumerate(sorted(items, key=lambda x: -x[0])[:TOP_PER_CITY], 1):
            rows.append((city, r["Id"], rank, impressions, clicks, round(position, 2)))
            print(f"  {city[:8]} {rank}. {r['Name'][:40]:<40} покази {impressions:>6}  кліки {clicks:>4}  позиція {position:.1f}")
    print(f"\nсторінок ресторанів у Search Console: {len(stats)}; у списку: {len(rows)} (міст: {len(by_city)})")

    if args.dry_run:
        print("нічого не записано (--dry-run)")
    else:
        write = db.cursor()
        write.execute("DELETE FROM PopularRestaurants")
        write.executemany("""INSERT INTO PopularRestaurants (CityId, RestaurantId, Position, Impressions, Clicks, AvgPosition, UpdatedAt)
                             VALUES (%s, %s, %s, %s, %s, %s, NOW())""", rows)
        db.commit()
        print("записано; на головній з'явиться протягом 10 хв (і після кешу Cloudflare)")
    db.close()


if __name__ == "__main__":
    main()

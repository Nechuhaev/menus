"""Щоденна статистика сторінок для адмінки (SEO-дашборд, «Страва + місто»), кодом, без AI.

- Search Console: кліки, покази, позиція кожної сторінки за кожен день -> SeoPageDays.
- GA4 (Data API, той самий сервісний акаунт, що й для Search Console; його треба додати в GA4 як «Глядач»):
  перегляди й сеанси кожної сторінки за кожен день -> GaPageDays. Без доступу до GA4 - лише попередження.

Search Console дораховує останні 2-3 дні, тож щоразу перезаписуються останні --days днів (за замовчуванням 7).

Запуск:  python3 seo_daily.py              # cron щодня: останні 7 днів
         python3 seo_daily.py --days 90    # перший запуск: історія за 90 днів
Змінні в .env: GSC_KEY_FILE, GSC_SITE (як для gsc.py), GA4_PROPERTY (за замовчуванням 359387752).
"""
import argparse
import os
from datetime import date, timedelta

import gsc
from sync_restaurants import get_db

GA4_PROPERTY = os.environ.get("GA4_PROPERTY", "359387752")
GSC_LAG_DAYS = 3  # останні дні Search Console ще неповні

GSC_SQL = """CREATE TABLE IF NOT EXISTS SeoPageDays (
    Day DATE NOT NULL,
    Page VARCHAR(700) NOT NULL,
    Clicks INT NOT NULL,
    Impressions INT NOT NULL,
    Position DECIMAL(6,2) NOT NULL,
    PRIMARY KEY (Day, Page),
    KEY IX_SeoPageDays_Page (Page)
) DEFAULT CHARSET=utf8mb4"""
GA_SQL = """CREATE TABLE IF NOT EXISTS GaPageDays (
    Day DATE NOT NULL,
    Path VARCHAR(700) NOT NULL,
    Views INT NOT NULL,
    Sessions INT NOT NULL,
    PRIMARY KEY (Day, Path),
    KEY IX_GaPageDays_Path (Path)
) DEFAULT CHARSET=utf8mb4"""


def gsc_days(days):
    """[(день, сторінка, кліки, покази, позиція)] за days днів до сьогодні - GSC_LAG_DAYS."""
    return [(keys[0], keys[1][:700], int(clicks), int(impressions), round(position, 2))
            for keys, clicks, impressions, position in gsc.query(["date", "page"], days=days, end_offset=GSC_LAG_DAYS)]


def ga_days(days):
    """[(день, шлях, перегляди, сеанси)] з GA4 за days днів до вчора."""
    from google.auth.transport.requests import AuthorizedSession
    from google.oauth2 import service_account

    credentials = service_account.Credentials.from_service_account_file(
        gsc.KEY_FILE, scopes=["https://www.googleapis.com/auth/analytics.readonly"])
    session = AuthorizedSession(credentials)
    end = date.today() - timedelta(days=1)
    url = f"https://analyticsdata.googleapis.com/v1beta/properties/{GA4_PROPERTY}:runReport"
    rows, offset, limit = [], 0, 100000
    while True:
        resp = session.post(url, json={
            "dateRanges": [{"startDate": str(end - timedelta(days=days - 1)), "endDate": str(end)}],
            "dimensions": [{"name": "date"}, {"name": "pagePath"}],
            "metrics": [{"name": "screenPageViews"}, {"name": "sessions"}],
            "limit": limit, "offset": offset}, timeout=120)
        resp.raise_for_status()
        data = resp.json()
        for r in data.get("rows", []):
            d, path = (v["value"] for v in r["dimensionValues"])
            views, sessions = (int(float(v["value"])) for v in r["metricValues"])
            rows.append((f"{d[:4]}-{d[4:6]}-{d[6:]}", path.split("?")[0][:700], views, sessions))
        offset += limit
        if offset >= int(data.get("rowCount", 0)):
            return rows


def save(db, table, columns, rows, first_day):
    cur = db.cursor()
    cur.execute(f"DELETE FROM {table} WHERE Day >= %s", (first_day,))
    placeholders = ", ".join(["%s"] * len(columns))
    # той самий шлях кілька разів (параметри адреси відрізано) - сумуємо
    merged = {}
    for row in rows:
        key = row[:2]
        merged[key] = tuple(a + b for a, b in zip(merged[key], row[2:])) if key in merged and table == "GaPageDays" else row[2:]
    data = [k + v for k, v in merged.items()]
    for i in range(0, len(data), 5000):
        cur.executemany(f"INSERT INTO {table} ({', '.join(columns)}) VALUES ({placeholders})", data[i:i + 5000])
    db.commit()
    cur.close()
    return len(data)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--days", type=int, default=7, help="скільки останніх днів перезаписати")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor()
    cur.execute(GSC_SQL)
    cur.execute(GA_SQL)
    cur.close()

    gsc_first = date.today() - timedelta(days=GSC_LAG_DAYS + args.days - 1)
    n = save(db, "SeoPageDays", ("Day", "Page", "Clicks", "Impressions", "Position"), gsc_days(args.days), gsc_first)
    print(f"Search Console: {n} рядків (день x сторінка) з {gsc_first}")

    try:
        ga_first = date.today() - timedelta(days=args.days)
        n = save(db, "GaPageDays", ("Day", "Path", "Views", "Sessions"), ga_days(args.days), ga_first)
        print(f"GA4: {n} рядків (день x сторінка) з {ga_first}")
    except Exception as e:
        print(f"GA4 пропущено: {str(e)[:300]}\n  додайте сервісний акаунт із GSC_KEY_FILE у GA4 "
              f"(Адміністратор -> Керування доступом до ресурсу -> «Глядач») для ресурсу {GA4_PROPERTY}")
    db.close()


if __name__ == "__main__":
    main()

"""Тижнева статистика сторінок з Search Console (API) - для звіту «SEO» в адмінці, без AI.

Кожна сторінка за тиждень: кліки, покази, середня позиція. Тиждень - 7 днів, що закінчуються за 3 дні до
запуску (останні дні Search Console ще дораховує). Рядки тижня перезаписуються, історія лишається:
адмінка порівнює останній тиждень з попереднім (ріст/падіння, нові сторінки, види сторінок).

Запуск:  python3 seo_weekly.py              # останній тиждень (cron раз на тиждень)
         python3 seo_weekly.py --weeks 8    # перший запуск: ще й 7 попередніх тижнів
"""
import argparse
from datetime import date, timedelta

import gsc
from sync_restaurants import get_db

TABLE_SQL = """CREATE TABLE IF NOT EXISTS SeoPageWeeks (
    WeekEnd DATE NOT NULL,
    Page VARCHAR(700) NOT NULL,
    Clicks INT NOT NULL,
    Impressions INT NOT NULL,
    Position DECIMAL(6,2) NOT NULL,
    PRIMARY KEY (WeekEnd, Page),
    KEY IX_SeoPageWeeks_Page (Page)
) DEFAULT CHARSET=utf8mb4"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weeks", type=int, default=1, help="скільки останніх тижнів записати")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor()
    cur.execute(TABLE_SQL)
    for week in range(args.weeks):
        offset = 3 + 7 * week
        week_end = date.today() - timedelta(days=offset)
        rows = [(week_end, page[:700], clicks, impressions, round(position, 2))
                for page, clicks, impressions, position in gsc.pages(days=7, end_offset=offset)]
        cur.execute("DELETE FROM SeoPageWeeks WHERE WeekEnd = %s", (week_end,))
        cur.executemany("INSERT INTO SeoPageWeeks (WeekEnd, Page, Clicks, Impressions, Position) VALUES (%s, %s, %s, %s, %s)", rows)
        db.commit()
        print(f"тиждень до {week_end}: сторінок {len(rows)}, кліків {sum(r[2] for r in rows)}, показів {sum(r[3] for r in rows)}")
    db.close()


if __name__ == "__main__":
    main()

"""Sitemap «повернутих» сторінок (/sitemap-recrawl.xml) - щоб Google швидше переобійшов їх, без AI.

Сторінки, які мали покази в Google, а потім зникли з видачі (у вересні бот ховав ресторани - сторінки віддавали
404, і Google викинув їх з індексу), після повернення на сайт Google сам переобходить тижнями: у загальному
sitemap дата зміни в усіх сторінок «сьогодні», і він їй не вірить. Тут - окремий sitemap лише з такими
сторінками і справжньою датою повернення.

Звідки список: SeoPageWeeks (seo_weekly.py) - сторінка мала >= MIN_IMPRESSIONS показів на тиждень раніше,
а в останньому тижні її немає. Кожну перевіряє запитом: у sitemap лише ті, що зараз віддають 200 без noindex.
Сторінка знову з'явилась у видачі (покази в останньому тижні) або перестала віддавати 200 - прибирається.
Таблиця SeoRecrawl, сайт читає її на /sitemap-recrawl.xml.

Запуск:  python3 recrawl_pages.py --dry-run   # показати, нічого не записувати
         python3 recrawl_pages.py             # оновити (cron щодня)
"""
import argparse
import re
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime

import requests

from sync_restaurants import get_db

MIN_IMPRESSIONS = 30
NOINDEX = re.compile(r"<meta[^>]+name=[\"']robots[\"'][^>]+noindex", re.I)

TABLE_SQL = """CREATE TABLE IF NOT EXISTS SeoRecrawl (
    Page VARCHAR(700) NOT NULL PRIMARY KEY,
    Impressions INT NOT NULL,
    ReturnedAt DATETIME NOT NULL,
    CheckedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


def live(url):
    """Сторінка відкривається (200 без переадресації) і не закрита noindex."""
    try:
        r = requests.get(url, timeout=30, allow_redirects=False, headers={"User-Agent": "TipMyMenuBot/1.0"})
    except requests.RequestException as e:
        return False, type(e).__name__
    if r.status_code != 200:
        return False, str(r.status_code)
    if NOINDEX.search(r.text):
        return False, "noindex"
    return True, "200"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor()
    cur.execute(TABLE_SQL)
    cur.execute("SELECT MAX(WeekEnd) FROM SeoPageWeeks")
    last = cur.fetchone()[0]
    if not last:
        print("SeoPageWeeks порожня - спершу seo_weekly.py")
        return
    cur.execute("""SELECT Page, MAX(Impressions) FROM SeoPageWeeks
                   WHERE WeekEnd < %s AND Impressions >= %s
                     AND Page NOT IN (SELECT Page FROM SeoPageWeeks WHERE WeekEnd = %s AND Impressions > 0)
                   GROUP BY Page""", (last, MIN_IMPRESSIONS, last))
    lost = dict(cur.fetchall())
    cur.execute("SELECT Page, ReturnedAt FROM SeoRecrawl")
    known = dict(cur.fetchall())

    with ThreadPoolExecutor(8) as pool:
        checks = dict(zip(lost, pool.map(live, lost)))

    now = datetime.now()
    keep = {page: lost[page] for page, (ok, _) in checks.items() if ok}
    statuses = {}
    for ok, status in checks.values():
        statuses[status] = statuses.get(status, 0) + 1
    print(f"тиждень до {last}: зникли з видачі {len(lost)} сторінок (>= {MIN_IMPRESSIONS} показів на тиждень раніше)")
    print(f"зараз відповідають: {statuses}")
    print(f"у sitemap: {len(keep)} (нових {len(set(keep) - set(known))}, прибрано {len(set(known) - set(keep))})")
    for page in sorted(keep, key=lambda p: -keep[p])[:20]:
        print(f"  {keep[page]:>5}  {page}")

    if args.dry_run:
        print("нічого не записано (--dry-run)")
    else:
        cur.execute("DELETE FROM SeoRecrawl")
        cur.executemany("INSERT INTO SeoRecrawl (Page, Impressions, ReturnedAt, CheckedAt) VALUES (%s, %s, %s, %s)",
                        [(page, imp, known.get(page, now), now) for page, imp in keep.items()])
        db.commit()
        print("записано: https://tipmymenu.com/sitemap-recrawl.xml")
    db.close()


if __name__ == "__main__":
    main()

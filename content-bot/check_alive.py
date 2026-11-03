"""Чи заклад ще працює - для розсилки власникам (адмінка «Розсилка», колонка «Перевірка»). Кодом, без AI.

Що перевіряється (лише ресторани з контактами для розсилки, або всі з --all):
- QR-меню (ChoiceQR, Expirenza): з посилання на меню в картці ресторану або сайт, якщо це адреса QR-меню.
  «Сервіс призупинено» -> qr_suspended: такий ресторан не потрапляє в розсилку; 404 -> QR-меню видалено.
- Сайт: 404/410, домен не існує (DNS), домен на продаж / припаркований / хостинг заблоковано.

Висновок:
- closed (ЗАКРИТО, червоним в адмінці) - і сайт, і QR-меню є, і обидва мертві;
- doubt (сумнівно) - єдине відоме джерело мертве, або одне з двох;
- qr_suspended - QR-меню «Сервіс призупинено»;
- ok - сайт чи QR-меню відкриваються.
Без сайту й QR-меню перевіряти нічим - рядок не пишеться. Позначки адміна (кнопка в картці, Manual = 1) не чіпаються.
Бот нічого не ховає з сайту - лише позначає для розсилки.

Запуск:  python3 check_alive.py --dry-run     # показати, нічого не записувати
         python3 check_alive.py               # записати (cron раз на тиждень)
         python3 check_alive.py --all         # усі активні ресторани, не лише з контактами
"""
import argparse
import re
import socket
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import urlparse

import requests

from sync_restaurants import get_db

HEADERS = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36",
           "Accept-Language": "uk,ru;q=0.8,en;q=0.5"}
QR_DOMAINS = re.compile(r"choiceqr\.com|expz\.menu|expirenza", re.I)
SUSPENDED = re.compile(r"сервіс\s+призупинено|сервис\s+приостановлен|service\s+(?:is\s+)?suspended|"
                       r"обслуговування\s+призупинено|обслуживание\s+приостановлено", re.I)
PARKED = re.compile(r"domain\s+(?:is\s+)?for\s+sale|buy\s+this\s+domain|this\s+domain\s+(?:name\s+)?(?:may\s+be|is)\s+for\s+sale|"
                    r"домен\s+прода[её]тся|домен\s+продається|domain\s+has\s+expired|срок\s+(?:регистрации|делегирования)\s+домена\s+(?:истек|закончил)|"
                    r"термін\s+(?:реєстрації|делегування)\s+домену|account\s+(?:has\s+been\s+)?suspended|hosting\s+account\s+(?:is\s+)?suspended|"
                    r"parked\s+(?:free|domain)|sedoparking|is\s+parked", re.I)

# сторінки-каталоги й соцмережі відкриваються завжди - про те, чи заклад працює, вони нічого не кажуть
NOT_EVIDENCE = re.compile(r"instagram\.com|facebook\.com|fb\.com|t\.me/|tiktok\.com|firstline\.in\.ua|tripadvisor\.|"
                          r"restaurantguru\.|google\.[a-z.]+/maps|goo\.gl|maps\.app|2gis\.|tomato\.ua|linktr\.ee|taplink\.", re.I)

TABLE_SQL = """CREATE TABLE IF NOT EXISTS RestaurantAliveCheck (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    Status VARCHAR(16) NOT NULL,
    Reason TEXT NULL,
    Manual TINYINT(1) NOT NULL DEFAULT 0,
    CheckedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


def normalize(url):
    url = (url or "").strip()
    if not url:
        return None
    return url if re.match(r"https?://", url, re.I) else "https://" + url


def fetch(url):
    """(alive, причина): alive = True / False / None (не вдалося зрозуміти - не рахуємо)."""
    try:
        r = requests.get(url, timeout=25, headers=HEADERS, allow_redirects=True)
    except requests.exceptions.ConnectionError as e:
        host = urlparse(url).hostname or ""
        try:
            socket.gethostbyname(host)
        except OSError:
            return False, "домен не існує"
        return None, f"не відповідає ({type(e).__name__})"
    except requests.RequestException as e:
        return None, f"не відповідає ({type(e).__name__})"
    text = r.text[:200000] if "html" in (r.headers.get("content-type") or "html") else ""
    if r.status_code in (404, 410):
        return False, f"{r.status_code}"
    if SUSPENDED.search(text):
        return "suspended", "сервіс призупинено"
    if PARKED.search(text):
        return False, "домен на продаж / хостинг заблоковано"
    if r.status_code >= 500:
        return None, f"помилка сервера {r.status_code}"
    return True, f"{r.status_code}"


def check(r):
    website, menu = normalize(r["Website"]), normalize(r["MenuUrl"])
    qr = menu if menu and QR_DOMAINS.search(menu) else (website if website and QR_DOMAINS.search(website) else None)
    site = website if website and not QR_DOMAINS.search(website) and not NOT_EVIDENCE.search(website) else None
    results = {}
    if qr:
        results["QR-меню"] = fetch(qr)
    if site:
        results["сайт"] = fetch(site)
    if not results:
        return None
    if results.get("QR-меню", (None,))[0] == "suspended":
        return "qr_suspended", f"QR-меню: сервіс призупинено ({qr})"
    alive = [k for k, (a, _) in results.items() if a is True]
    dead = [k for k, (a, _) in results.items() if a is False]
    reason = "; ".join(f"{k}: {why}" for k, (a, why) in results.items())
    if alive:
        return "ok", reason
    if dead and len(dead) == len(results) == 2:
        return "closed", reason
    if dead:
        return "doubt", reason
    return "doubt", reason  # нічого не відповіло, але й мертвим не є (тайм-аути, 5xx)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--all", action="store_true", help="усі активні ресторани, не лише з контактами")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(TABLE_SQL)
    only_contacts = "" if args.all else """ AND (r.Email LIKE '%@%' OR EXISTS
        (SELECT 1 FROM RestaurantContacts c WHERE c.RestaurantId = r.Id AND c.Hidden = 0))"""
    cur.execute(f"""SELECT CONCAT(r.Id, '') AS Id, r.Name,
                           (SELECT ri.Website FROM RestaurantInfo ri WHERE ri.RestaurantId = r.Id LIMIT 1) AS Website,
                           (SELECT ml.Url FROM RestaurantMenuLinks ml WHERE ml.RestaurantId = r.Id LIMIT 1) AS MenuUrl
                    FROM Restaurants r
                    WHERE r.Status = 0
                      AND NOT EXISTS (SELECT 1 FROM RestaurantAliveCheck a WHERE a.RestaurantId = r.Id AND a.Manual = 1){only_contacts}""")
    restaurants = cur.fetchall()
    with ThreadPoolExecutor(8) as pool:
        results = list(pool.map(check, restaurants))

    stats = Counter()
    rows = []
    for r, res in zip(restaurants, results):
        if res is None:
            stats["немає сайту й QR-меню"] += 1
            continue
        status, reason = res
        stats[status] += 1
        rows.append((r["Id"], status, reason[:1000]))
        if status != "ok":
            print(f"{status:<13} {r['Name'][:40]:<40} {reason[:140]}")
    print(f"\nперевірено: {len(restaurants)}; {dict(stats)}")

    if args.dry_run:
        print("нічого не записано (--dry-run)")
    else:
        write = db.cursor()
        write.executemany("""INSERT INTO RestaurantAliveCheck (RestaurantId, Status, Reason, Manual, CheckedAt)
                             VALUES (%s, %s, %s, 0, NOW())
                             ON DUPLICATE KEY UPDATE Status = IF(Manual = 1, Status, VALUES(Status)),
                                                     Reason = IF(Manual = 1, Reason, VALUES(Reason)),
                                                     CheckedAt = IF(Manual = 1, CheckedAt, NOW())""", rows)
        db.commit()
        print(f"записано: {len(rows)} (видно в адмінці «Розсилка», колонка «Перевірка»)")
    db.close()


if __name__ == "__main__":
    main()

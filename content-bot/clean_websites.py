"""Чистить поле «Сайт» ресторанів від того, що не є робочим сайтом закладу і заважає боту брати меню
з Expirenza/ChoiceQR (бот читає сайт, а соцмережа чи мертвий домен дають порожнє меню або «Pending»):

- посилання на Expirenza/ChoiceQR у полі «Сайт» - переноситься в «Посилання на меню» (якщо там порожньо),
  з «Сайту» прибирається: бот одразу братиме меню з нього;
- Facebook, Instagram, Telegram, TikTok, linktr.ee/taplink, карти й каталоги (Google Maps, TripAdvisor,
  Restaurant Guru, Glovo, business.site...) - прибираються;
- не адреса («-», «немає», без домену) - прибирається;
- домен не існує: DNS сервера не знаходить і публічний DNS Cloudflare підтверджує NXDOMAIN - прибирається
  (DNS сервера сам помилявся на робочих доменах, тому лише з підтвердженням);
- домен є, але сайт не відповідає чи віддає помилку - НЕ прибирається, лише список для ручної перевірки
  (--http): 403/5xx від захисту від ботів чи тимчасові збої - не підстава видаляти;
- сайт переадресовує на Expirenza/ChoiceQR (--http) - сайт лишається, адреса меню йде в «Посилання на меню».

Кожне прибрання пишеться в WebsiteCleanupLog (старе значення й причина): бот бачить, що сайт прибрано,
і не ховає ресторан, а чекає посилання на меню. Повернути: --restore <адреса ресторану> або --restore all.

Запуск:  python3 clean_websites.py --dry-run           # що прибере, нічого не пише
         python3 clean_websites.py --dry-run --http    # плюс список сайтів, що не відповідають (довше)
         python3 clean_websites.py                     # записати
         python3 clean_websites.py --restore titka-klara-811
"""
import argparse
import re
import socket
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from urllib.parse import urlparse

import requests

from menu_platforms import EXPZ_ROOT, HEADERS
from sync_restaurants import (MENU_LINKS_TABLE_SQL, NOT_SITE_HOSTS, SOCIAL_HOSTS, WEBSITE_CLEANUP_TABLE_SQL,
                              get_db, host_in)

WORKERS = 16
SEPARATORS = re.compile(r"[\s,;|]+")
HOST_RE = re.compile(r"^(?:[a-z0-9а-яіїєґ](?:[a-z0-9а-яіїєґ-]{0,61}[a-z0-9а-яіїєґ])?\.)+[a-zа-яіїєґ]{2,}$", re.I)
REASONS = {"menu": "посилання на меню (Expirenza/ChoiceQR)", "social": "соцмережа", "not_site": "карта/каталог",
           "malformed": "не адреса", "nxdomain": "домен не існує"}


def normalize(url):
    url = url.strip().strip("\"'")
    if url and not re.match(r"^https?://", url, re.I):
        url = "http://" + url.lstrip("/")
    return url


def is_menu_link(url):
    return bool(EXPZ_ROOT.match(url)) or (urlparse(url).hostname or "").lower().endswith(".choiceqr.com")


def dns_missing(host):
    """True лише коли домену немає: DNS сервера не знаходить і Cloudflare DoH відповідає NXDOMAIN (Status 3)."""
    try:
        socket.getaddrinfo(host, 443)
        return False
    except socket.gaierror:
        pass
    try:
        r = requests.get("https://cloudflare-dns.com/dns-query", params={"name": host, "type": "A"},
                         headers={"Accept": "application/dns-json"}, timeout=15)
        return r.json().get("Status") == 3
    except (requests.RequestException, ValueError):
        return False  # не змогли перевірити - не прибираємо


def http_problem(url):
    """(опис проблеми сайту або None - лише для звіту, адреса меню, куди сайт переадресовує, або None)."""
    try:
        r = requests.get(url, headers=HEADERS, timeout=20, allow_redirects=True)
    except requests.RequestException as e:
        return type(e).__name__, None
    if r.status_code >= 400:
        return f"HTTP {r.status_code}", None
    final = r.url
    if is_menu_link(final):
        return None, final
    if host_in(final, SOCIAL_HOSTS + NOT_SITE_HOSTS):
        return f"переадресовує на {final}", None
    return None, None


def classify(url):
    """Причина прибрати (ключ REASONS) без мережі або None - потрібна перевірка DNS."""
    if is_menu_link(url):
        return "menu"
    if host_in(url, SOCIAL_HOSTS):
        return "social"
    if host_in(url, NOT_SITE_HOSTS):
        return "not_site"
    host = (urlparse(url).hostname or "").lower()
    if not HOST_RE.match(host):
        return "malformed"
    return None


def menu_links_for(menu_links, restaurant_id):
    return any(rid == restaurant_id for rid, _ in menu_links)


def restore(db, target):
    cur = db.cursor(dictionary=True)
    cur.execute(WEBSITE_CLEANUP_TABLE_SQL)
    where, params = "", ()
    if target != "all":
        where, params = " WHERE l.RestaurantId = (SELECT ItemId FROM RestaurantSeo WHERE Url = %s)", (target,)
    # найраніший запис ресторану - значення до першого прибрання
    cur.execute("""SELECT l.RestaurantId, l.OldWebsite FROM WebsiteCleanupLog l
                   JOIN (SELECT RestaurantId, MIN(Id) AS Id FROM WebsiteCleanupLog GROUP BY RestaurantId) f ON f.Id = l.Id"""
                + where, params)
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute("UPDATE RestaurantInfo SET Website = %s WHERE RestaurantId = %s", (row["OldWebsite"], row["RestaurantId"]))
        write.execute("DELETE FROM WebsiteCleanupLog WHERE RestaurantId = %s", (row["RestaurantId"],))
    db.commit()
    print(f"повернено сайтів: {len(rows)}. Перегенеруй кеш ресторанів.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--http", action="store_true", help="також перевірити, чи сайти відповідають (лише звіт) і чи не переадресовують на меню")
    parser.add_argument("--restore", help="адреса ресторану (RestaurantSeo.Url) або all - повернути прибрані сайти")
    args = parser.parse_args()

    db = get_db()
    if args.restore:
        restore(db, args.restore)
        db.close()
        return
    cur = db.cursor(dictionary=True)
    cur.execute(MENU_LINKS_TABLE_SQL)
    cur.execute(WEBSITE_CLEANUP_TABLE_SQL)
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, rs.Url AS RestaurantUrl, ri.Website, ml.Url AS MenuUrl
                   FROM Restaurants r JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   LEFT JOIN RestaurantMenuLinks ml ON ml.RestaurantId = r.Id
                   WHERE ri.Website IS NOT NULL AND TRIM(ri.Website) <> ''""")
    restaurants = cur.fetchall()

    # у полі буває кілька адрес («site.ua instagram.com/...»): кожна окремо, погані прибираються, решта лишається
    parsed = {}
    for r in restaurants:
        parsed[r["Id"]] = [(raw, normalize(raw)) for raw in SEPARATORS.split(r["Website"].strip()) if raw.strip("\"'")]
    hosts = {(urlparse(url).hostname or "").lower() for urls in parsed.values() for _, url in urls if not classify(url)}
    with ThreadPoolExecutor(WORKERS) as pool:
        missing = dict(zip(hosts, pool.map(dns_missing, hosts)))

    changes, menu_links, alive = [], [], []
    totals = Counter()
    for r in restaurants:
        keep, reasons = [], []
        for raw, url in parsed[r["Id"]]:
            reason = classify(url) or ("nxdomain" if missing.get((urlparse(url).hostname or "").lower()) else None)
            if reason:
                reasons.append(reason)
                totals[reason] += 1
                if reason == "menu" and not r["MenuUrl"] and not menu_links_for(menu_links, r["Id"]):
                    menu_links.append((r["Id"], url))
            else:
                keep.append(raw)
                alive.append((r, url))
        if reasons:
            changes.append((r, " ".join(keep), ", ".join(REASONS[x] for x in dict.fromkeys(reasons))))

    print(f"ресторанів із сайтом: {len(restaurants)}; змінити поле «Сайт»: {len(changes)}; "
          f"перенести в «Посилання на меню»: {len(menu_links)}")
    print("причини: " + ", ".join(f"{REASONS[k]} {v}" for k, v in totals.items()))
    for r, new, reason in changes:
        print(f"  {r['RestaurantUrl']}: {r['Website'].strip()[:90]} -> {new or 'порожньо'} ({reason})")

    if args.http:
        with ThreadPoolExecutor(WORKERS) as pool:
            checks = list(pool.map(lambda item: http_problem(item[1]), alive))
        # сайт сам переадресовує на Expirenza/ChoiceQR: сайт лишається, адреса меню - у «Посилання на меню»
        redirects = [(r, menu) for (r, _), (_, menu) in zip(alive, checks)
                     if menu and not r["MenuUrl"] and not menu_links_for(menu_links, r["Id"])]
        menu_links.extend((r["Id"], menu) for r, menu in redirects)
        print(f"\nсайт переадресовує на меню - додати в «Посилання на меню» (сайт лишається): {len(redirects)}")
        for r, menu in redirects:
            print(f"  {r['RestaurantUrl']}: {menu}")
        bad = [(r, url, p) for (r, url), (p, _) in zip(alive, checks) if p]
        print(f"\nне відповідають (не прибираються, перевір вручну): {len(bad)}")
        for r, url, problem in bad:
            print(f"  {r['RestaurantUrl']}: {url} - {problem}")

    if args.dry_run:
        db.close()
        return

    now = datetime.utcnow()
    write = db.cursor()
    for restaurant_id, url in menu_links:
        write.execute("INSERT IGNORE INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (%s, %s, %s)",
                      (restaurant_id, url, now))
    for r, new, reason in changes:
        write.execute("INSERT INTO WebsiteCleanupLog (RestaurantId, OldWebsite, Reason, CreatedAt) VALUES (%s, %s, %s, %s)",
                      (r["Id"], r["Website"][:1000], reason[:255], now))
        write.execute("UPDATE RestaurantInfo SET Website = %s WHERE RestaurantId = %s", (new, r["Id"]))
    db.commit()
    db.close()
    print("\nзаписано. Перегенеруй кеш ресторанів.")


if __name__ == "__main__":
    main()

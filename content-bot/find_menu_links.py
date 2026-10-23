"""Пошук посилань на меню в Expirenza (expz.menu) і ChoiceQR для ресторанів без «Посилання на меню».

Для кожного активного ресторану без посилання - два запити через Firecrawl Search API
(«<назва> site:expz.menu» і «<назва> site:choiceqr.com»). Знайдені посилання з назвами сторінок
пишуться в CSV для перевірки; --apply записує в адмінку (RestaurantMenuLinks) рядки зі статусом ok.

Статус ok - назва закладу на сторінці збігається з нашою, і це однозначно: або знайдено одну
сторінку, або вулиця з нашої адреси є в назві/описі сторінки (філії мережі), і посилання не
претендує на інший наш ресторан. Решта - «перевірити»: статус у CSV можна виправити вручну.

Запуск:  python3 find_menu_links.py --dry-run             # скільки ресторанів і запитів, без пошуку
         python3 find_menu_links.py                       # пошук -> menu_links.csv
         python3 find_menu_links.py --limit 20            # перші 20 ресторанів (пробний прогін)
         python3 find_menu_links.py --apply menu_links.csv  # записати рядки зі статусом ok
"""
import argparse
import csv
import difflib
import os
import re
import time
from collections import defaultdict

import requests

from sync_restaurants import MENU_LINKS_TABLE_SQL, get_db, normalize_name

SEARCH_URL = "https://api.firecrawl.dev/v2/search"
RESULTS_PER_QUERY = 10
PLATFORMS = {
    "expz": ("site:expz.menu", re.compile(r"^https?://(?:www\.)?expz\.menu/([0-9a-f-]{36})", re.I)),
    "choiceqr": ("site:choiceqr.com", re.compile(r"^https?://([a-z0-9-]+)\.choiceqr\.com", re.I)),
}
# при двох посиланнях з ok у одного ресторану - Expirenza (там є ваги й варіанти страв)
PLATFORM_ORDER = ["expz", "choiceqr"]
TITLE_SUFFIX = re.compile(r"\s*[-|–]\s*(?:expirenza|choiceqr|menu|меню)\s*$", re.I)
NAME_MATCH = 0.8
# слова адреси, що не відрізняють філії
ADDRESS_NOISE = {"вул", "вулиця", "просп", "проспект", "пр", "пл", "площа", "бул", "бульвар", "пров", "провулок",
                 "шосе", "наб", "набережна", "буд", "корпус", "трц", "тц", "україна", "київ", "львів", "одеса",
                 "харків", "дніпро", "ул", "улица"}
CSV_FIELDS = ["RestaurantId", "Restaurant", "Address", "Platform", "Url", "PageTitle", "PageDescription",
              "NameScore", "Status", "Reason"]


def search(query, key):
    for attempt in range(4):
        resp = requests.post(SEARCH_URL, timeout=60, headers={"Authorization": f"Bearer {key}"},
                             json={"query": query, "limit": RESULTS_PER_QUERY})
        # ліміт запитів на хвилину - чекаємо й повторюємо
        if resp.status_code == 429 and attempt < 3:
            time.sleep(30 * (attempt + 1))
            continue
        resp.raise_for_status()
        break
    data = resp.json().get("data") or {}
    return (data.get("web") or []) if isinstance(data, dict) else data


def menu_root(platform, url):
    """Корінь меню закладу: https://expz.menu/<uuid> або https://<заклад>.choiceqr.com/."""
    m = PLATFORMS[platform][1].match(url or "")
    if not m:
        return None
    return f"https://expz.menu/{m.group(1).lower()}" if platform == "expz" else f"https://{m.group(1).lower()}.choiceqr.com/"


def brand(title):
    """Назва закладу зі сторінки: «Хачапурі та Вино | Оболонь - Expirenza» -> «хачапурі та вино»."""
    return normalize_name(TITLE_SUFFIX.sub("", title or "").split("|")[0])


def name_score(ours, title):
    a, b = normalize_name(ours).replace(" і ", " та "), brand(title).replace(" і ", " та ")
    return difflib.SequenceMatcher(None, a, b).ratio() if a and b else 0


def street_words(address):
    return {w for w in normalize_name(address).split() if len(w) >= 4 and not w.isdigit() and w not in ADDRESS_NOISE}


def find(restaurants, key):
    rows = []
    for i, r in enumerate(restaurants, 1):
        for platform, (site, _) in PLATFORMS.items():
            try:
                results = search(f"{r['Name']} {site}", key)
            except requests.RequestException as e:
                print(f"  ! {r['Name']} {platform}: {e}")
                continue
            pages = {}
            for res in results:
                root = menu_root(platform, res.get("url"))
                if root and root not in pages:
                    pages[root] = res
            for root, res in pages.items():
                rows.append({"RestaurantId": r["Id"], "Restaurant": r["Name"], "Address": r["Address"] or "",
                             "Platform": platform, "Url": root, "PageTitle": res.get("title") or "",
                             "PageDescription": (res.get("description") or "")[:300],
                             "NameScore": round(name_score(r["Name"], res.get("title")), 2)})
        print(f"[{i}/{len(restaurants)}] {r['Name']}: знайдено {sum(1 for x in rows if x['RestaurantId'] == r['Id'])}")
    return rows


def classify(rows, restaurants):
    """Статус кожного рядка: ok або «перевірити» з причиною."""
    same_name = defaultdict(int)
    for r in restaurants:
        same_name[normalize_name(r["Name"])] += 1
    by_restaurant = defaultdict(list)
    for row in rows:
        by_restaurant[(row["RestaurantId"], row["Platform"])].append(row)

    for (restaurant_id, _), group in by_restaurant.items():
        named = [x for x in group if x["NameScore"] >= NAME_MATCH]
        for row in group:
            row["Status"], row["Reason"] = "перевірити", "назва не збігається"
        if not named:
            continue
        streets = street_words(group[0]["Address"])
        on_street = [x for x in named if streets & set(normalize_name(x["PageTitle"] + " " + x["PageDescription"]).split())]
        if len(on_street) == 1:
            for x in named:
                x["Reason"] = "інша філія"
            on_street[0]["Status"], on_street[0]["Reason"] = "ok", "назва й вулиця"
        elif len(named) == 1 and same_name[normalize_name(group[0]["Restaurant"])] == 1:
            named[0]["Status"], named[0]["Reason"] = "ok", "назва, одна сторінка"
        else:
            for x in named:
                x["Reason"] = "кілька філій - не видно вулиці" if len(named) > 1 else "у нас кілька закладів з цією назвою"

    # одне посилання на два наші ресторани - жодному без перевірки
    owners = defaultdict(set)
    for row in rows:
        if row["Status"] == "ok":
            owners[row["Url"]].add(row["RestaurantId"])
    for row in rows:
        if row["Status"] == "ok" and len(owners[row["Url"]]) > 1:
            row["Status"], row["Reason"] = "перевірити", "посилання знайдено для кількох наших ресторанів"
    return rows


def apply(path, dry_run):
    with open(path, encoding="utf-8-sig") as f:
        rows = [r for r in csv.DictReader(f) if r["Status"].strip().lower() == "ok"]
    best = {}
    for row in sorted(rows, key=lambda r: PLATFORM_ORDER.index(r["Platform"]) if r["Platform"] in PLATFORM_ORDER else 99):
        best.setdefault(row["RestaurantId"], row)
    db = get_db()
    cur = db.cursor()
    cur.execute(MENU_LINKS_TABLE_SQL)
    for row in best.values():
        print(f"  {row['Restaurant']} ({row['Address']}) -> {row['Url']}")
        if not dry_run:
            # посилання, яке адмін уже вказав, не перезаписується
            cur.execute("INSERT IGNORE INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (%s, %s, NOW())",
                        (row["RestaurantId"], row["Url"]))
    if not dry_run:
        db.commit()
    db.close()
    print(f"ресторанів з посиланням: {len(best)}{' (dry-run)' if dry_run else ''}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true", help="лише порахувати (з --apply - показати, що запишеться)")
    parser.add_argument("--limit", type=int, default=0, help="скільки ресторанів обробити (0 - усі)")
    parser.add_argument("--out", default="menu_links.csv")
    parser.add_argument("--apply", metavar="CSV", help="записати рядки зі статусом ok в адмінку")
    args = parser.parse_args()

    if args.apply:
        apply(args.apply, args.dry_run)
        return

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(MENU_LINKS_TABLE_SQL)
    cur.execute("""SELECT r.Id, r.Name, r.Address FROM Restaurants r
                   LEFT JOIN RestaurantMenuLinks ml ON ml.RestaurantId = r.Id
                   WHERE r.Status = 0 AND ml.Url IS NULL ORDER BY r.Name""")
    restaurants = cur.fetchall()
    db.close()
    if args.limit:
        restaurants = restaurants[:args.limit]
    queries = len(restaurants) * len(PLATFORMS)
    print(f"ресторанів без посилання: {len(restaurants)}, запитів: {queries} (~{queries * 2} кредитів Firecrawl)")
    if args.dry_run:
        return

    key = os.environ.get("FIRECRAWL_API_KEY")
    if not key:
        raise SystemExit("FIRECRAWL_API_KEY немає в content-bot/.env")
    started = time.time()
    rows = classify(find(restaurants, key), restaurants)
    with open(args.out, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    ok = {r["RestaurantId"] for r in rows if r["Status"] == "ok"}
    found = {r["RestaurantId"] for r in rows}
    print(f"\n{args.out}: рядків {len(rows)}; ресторанів зі знахідками {len(found)}, з них ok {len(ok)}; "
          f"{time.time() - started:.0f} с")


if __name__ == "__main__":
    main()

"""Пошук посилань на меню в Expirenza (expz.menu), ChoiceQR і RestoApp для ресторанів без «Посилання на меню».

Для кожного активного ресторану без посилання - по запиту на сервіс через Firecrawl Search API
(«<назва> site:expz.menu», «<назва> site:choiceqr.com», «<назва> site:restoapp.eu»). Знайдені сторінки, схожі за назвою,
записуються пропозиціями в MenuLinkProposals: адмін підтверджує або відхиляє їх у «Звіти» ->
«Посилання на меню». Відхилене повторно не пропонується; ресторан, по якому вже шукали,
повторно шукається через SEARCH_AGAIN_DAYS днів.

«Рекомендовано» - назва закладу на сторінці збігається з нашою, і вибір однозначний: або
знайдено одну сторінку, або вулиця з нашої адреси є в назві/описі сторінки (філії мережі), і
посилання не претендує на інший наш ресторан. Сторінки з іншого міста відкидаються.

Запуск:  python3 find_menu_links.py --dry-run                 # скільки ресторанів і запитів, без пошуку
         python3 find_menu_links.py --limit 20                # перші 20 ресторанів (пробний прогін)
         python3 find_menu_links.py                           # усі
         python3 find_menu_links.py --import menu_links.csv   # пропозиції з CSV попереднього прогону
         python3 find_menu_links.py --platform restoapp       # лише один сервіс, зокрема для вже перевірених
                                                              # ресторанів (новий сервіс раніше не шукали)
         python3 find_menu_links.py --catalog restoapp        # навпаки: зібрати сторінки сервісу пошуком
                                                              # («site:restoapp.eu <місто>») і звірити назви
                                                              # з нашими ресторанами - кілька десятків запитів
                                                              # замість запиту на кожен ресторан
"""
import argparse
import csv
import difflib
import os
import re
import time
from collections import defaultdict

import requests

from dish_city_pages import TRANSLIT
from sync_restaurants import (KEEP_INDEXED_TABLE_SQL, MENU_LINKS_TABLE_SQL, RESTAURANT_CLICKS_JOIN, get_db,
                              get_localizable_value, normalize_name)

SEARCH_URL = "https://api.firecrawl.dev/v2/search"
RESULTS_PER_QUERY = 10
SEARCH_AGAIN_DAYS = 90
PLATFORMS = {
    "expz": ("site:expz.menu", re.compile(r"^https?://(?:www\.)?expz\.menu/([0-9a-f-]{36})", re.I)),
    "choiceqr": ("site:choiceqr.com", re.compile(r"^https?://([a-z0-9-]+)\.choiceqr\.com", re.I)),
    # у піддоменах RestoApp бувають «_» (fortunate_son, resto__doner); сам restoapp.eu і www - не заклад
    "restoapp": ("site:restoapp.eu", re.compile(r"^https?://(?!www\.)([a-z0-9_-]+)\.restoapp\.eu", re.I)),
}
NAME_MATCH = 0.9     # «рекомендовано»: майже точний збіг («7 Пятниц» - «П'ятниця» 0.84 - ні)
NAME_PROPOSE = 0.5   # нижче - сторінка іншого закладу, в адмінку не потрапляє
NAME_CONTAINS = 0.7  # наша назва цілком у назві сторінки («Avalon» - «Avalon Palace»): лише пропозиція

# Частини заголовка сторінки, що не є назвою закладу («Bigoli - Доставка, З собою», «Меню - ...»)
SERVICE_WORDS = {"dostavka", "z", "soboiu", "broniuvannia", "meniu", "menu", "delivery", "takeaway", "expirenza",
                 "choiceqr", "order", "zamovlennia", "zamovyty", "onlain", "online", "resto", "app"}
# Тип закладу в назві («Шоу-ресторан AltBier», «Barbara Bar»)
PLACE_TYPES = {"restoran", "restorany", "restoraniv", "restaurant", "kafe", "cafe", "bar", "pab", "pub", "shou",
               "kaviarnia", "coffee", "piv", "pyvbar"}
# Слова адреси, що не відрізняють філії (укр. і рос.)
ADDRESS_NOISE = {"вул", "вулиця", "ул", "улица", "просп", "проспект", "пр", "пл", "площа", "площадь", "бул",
                 "бульв", "бульвар", "пров", "провулок", "пер", "переулок", "шосе", "шоссе", "наб", "набережна",
                 "набережная", "буд", "корпус", "трц", "тц", "м", "г", "україна", "украина"}

PROPOSALS_TABLE_SQL = """CREATE TABLE IF NOT EXISTS MenuLinkProposals (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    Platform VARCHAR(20) NOT NULL,
    Url VARCHAR(255) NOT NULL,
    PageTitle VARCHAR(500) NOT NULL,
    PageDescription TEXT NOT NULL,
    NameScore DECIMAL(4,2) NOT NULL,
    Recommended TINYINT(1) NOT NULL DEFAULT 0,
    Reason VARCHAR(255) NOT NULL,
    Status TINYINT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL,
    UNIQUE KEY UX_MenuLinkProposals_Restaurant_Url (RestaurantId, Url),
    KEY IX_MenuLinkProposals_Status (Status)
) DEFAULT CHARSET=utf8mb4"""
SEARCHES_TABLE_SQL = """CREATE TABLE IF NOT EXISTS MenuLinkSearches (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    SearchedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


def search(query, key, limit=RESULTS_PER_QUERY):
    for attempt in range(4):
        resp = requests.post(SEARCH_URL, timeout=60, headers={"Authorization": f"Bearer {key}"},
                             json={"query": query, "limit": limit})
        # ліміт запитів на хвилину - чекаємо й повторюємо
        if resp.status_code == 429 and attempt < 3:
            time.sleep(30 * (attempt + 1))
            continue
        resp.raise_for_status()
        break
    data = resp.json().get("data") or {}
    return (data.get("web") or []) if isinstance(data, dict) else data


def menu_root(platform, url):
    """Корінь меню закладу: https://expz.menu/<uuid>, https://<заклад>.choiceqr.com/ або https://<заклад>.restoapp.eu/."""
    m = PLATFORMS[platform][1].match(url or "")
    if not m:
        return None
    if platform == "expz":
        return f"https://expz.menu/{m.group(1).lower()}"
    return f"https://{m.group(1).lower()}.{'choiceqr.com' if platform == 'choiceqr' else 'restoapp.eu'}/"


def latin(text):
    """Назва латиницею для порівняння укр./рос./англ. написань: «Аріранг» = «Arirang», і/и/y однаково."""
    text = re.sub(r"['’`ʼ]", "", text or "")
    return "".join(TRANSLIT.get(ch, ch) for ch in normalize_name(text)).replace("y", "i")


def without_place_type(value):
    return " ".join(w for w in value.split() if w not in PLACE_TYPES)


def title_names(title, url):
    """Можливі назви закладу зі сторінки: частини заголовка без службових («Доставка, З собою»)
    і без типу закладу, плюс піддомен ChoiceQR / RestoApp («bigoli.choiceqr.com», «fortunate_son.restoapp.eu»)."""
    names = []
    for part in re.split(r"\s+[-–—]\s+|\|", title or ""):
        words = latin(part).split()
        if words and not all(w in SERVICE_WORDS for w in words):
            names.append(without_place_type(" ".join(words)) or " ".join(words))
    for platform in ("choiceqr", "restoapp"):
        m = PLATFORMS[platform][1].match(url or "")
        if m:
            names.append(re.sub(r"[-_]+", " ", m.group(1)).strip().replace("y", "i"))
    return names


def name_score(ours, title, url):
    o = without_place_type(latin(ours)) or latin(ours)
    best = 0
    for name in title_names(title, url):
        best = max(best, difflib.SequenceMatcher(None, o, name).ratio(),
                   difflib.SequenceMatcher(None, o.replace(" ", ""), name.replace(" ", "")).ratio())
        if len(o) >= 4 and set(o.split()) <= set(name.split()):
            best = max(best, NAME_CONTAINS)
    return round(best, 2)


def street_stems(address):
    return {latin(w)[:5] for w in normalize_name(address).split()
            if len(w) >= 4 and not w.isdigit() and w not in ADDRESS_NOISE}


def page_stems(row):
    return {w[:5] for w in latin(row["PageTitle"] + " " + row["PageDescription"]).split()}


def load_cities(db):
    """Id міста -> назви латиницею (укр. і рос.)."""
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    return {row["Id"]: {latin(get_localizable_value(row["Name"], l)) for l in ("uk", "ru")} - {""}
            for row in cur.fetchall()}


def other_city(row, city, cities):
    """Сторінка називає інше місто і не називає наше («Argentina Grill | м. Харків» для київського закладу)."""
    text = " " + latin(row["PageTitle"] + " " + row["PageDescription"]) + " "
    ours = cities.get(city, set())
    if any(f" {n} " in text for n in ours):
        return False
    return any(f" {n} " in text for cid, names in cities.items() if cid != city for n in names)


# Кирилиця-двійник у латинському слові («Churrasсo» з кириличною «с») - пошук нічого не знаходить
LOOKALIKES = str.maketrans("асеорхуікАСЕОРХУІКМТНВ", "aceopxyikACEOPXYIKMTHB")


def query_name(name):
    return " ".join(w.translate(LOOKALIKES) if re.search(r"[a-z]", w, re.I) else w for w in (name or "").split())


def find(restaurants, key, platforms=None):
    rows = []
    cache = {}  # філії мережі з однаковою назвою - один запит на всіх
    for i, r in enumerate(restaurants, 1):
        found = 0
        for platform, (site, _) in PLATFORMS.items():
            if platforms and platform not in platforms:
                continue
            query = f"{query_name(r['Name'])} {site}"
            if query.lower() not in cache:
                try:
                    cache[query.lower()] = search(query, key)
                except requests.RequestException as e:
                    print(f"  ! {r['Name']} {platform}: {e}")
                    continue
            results = cache[query.lower()]
            pages = {}
            for res in results:
                root = menu_root(platform, res.get("url"))
                if root and root not in pages:
                    pages[root] = res
            for root, res in pages.items():
                rows.append({"RestaurantId": r["Id"], "Platform": platform, "Url": root,
                             "PageTitle": (res.get("title") or "")[:500],
                             "PageDescription": (res.get("description") or "")[:300]})
                found += 1
        print(f"[{i}/{len(restaurants)}] {r['Name']}: знайдено {found}")
    return rows


# зворотний пошук (--catalog): загальні слова, якими заклади підписують сторінки
CATALOG_WORDS = ["", "ресторан", "кафе", "бар", "паб", "піца", "суші", "кав'ярня", "доставка", "меню"]


def catalog_pages(platform, cities, key):
    """Усі сторінки сервісу, які знаходить пошук: site:<сервіс> + місто / загальне слово. {корінь: результат}."""
    site = PLATFORMS[platform][0]
    city_names = sorted({n for names in cities.values() for n in names if n})
    queries = [f"{site} {w}".strip() for w in CATALOG_WORDS] + [f"{site} {c}" for c in city_names]
    pages = {}
    for i, query in enumerate(queries, 1):
        try:
            results = search(query, key, limit=100)
        except requests.RequestException as e:
            print(f"  ! {query}: {e}")
            continue
        new = 0
        for res in results:
            root = menu_root(platform, res.get("url"))
            if root and root not in pages:
                pages[root] = res
                new += 1
        print(f"[{i}/{len(queries)}] {query}: +{new} (усього {len(pages)})")
    return pages


def catalog_rows(platform, pages, restaurants):
    """Пари (наш ресторан, сторінка сервісу), де назви схожі - далі score/classify, як у звичайному пошуку."""
    rows = []
    for root, res in pages.items():
        title, desc = (res.get("title") or "")[:500], (res.get("description") or "")[:300]
        for r in restaurants:
            if name_score(r["Name"], title, root) >= NAME_PROPOSE:
                rows.append({"RestaurantId": r["Id"], "Platform": platform, "Url": root,
                             "PageTitle": title, "PageDescription": desc})
    return rows


def score(rows, restaurants, cities):
    """Схожість назви; сторінки інших закладів і з іншого міста відкидаються."""
    by_id = {r["Id"]: r for r in restaurants}
    kept = []
    for row in rows:
        r = by_id.get(row["RestaurantId"])
        if not r:
            continue
        row["NameScore"] = name_score(r["Name"], row["PageTitle"], row["Url"])
        if row["NameScore"] >= NAME_PROPOSE and not other_city(row, r["City"], cities):
            kept.append(row)
    return kept


def classify(rows, restaurants):
    """Recommended і причина для кожної пропозиції."""
    by_id = {r["Id"]: r for r in restaurants}
    same_name = defaultdict(int)
    for r in restaurants:
        same_name[latin(r["Name"])] += 1
    groups = defaultdict(list)
    for row in rows:
        row["Recommended"], row["Reason"] = 0, "назва схожа частково"
        groups[(row["RestaurantId"], row["Platform"])].append(row)

    for (restaurant_id, _), group in groups.items():
        r = by_id[restaurant_id]
        named = [x for x in group if x["NameScore"] >= NAME_MATCH]
        streets = street_stems(r["Address"])
        on_street = [x for x in group if streets & page_stems(x)]
        if len(on_street) == 1:
            for x in named:
                x["Reason"] = "інша філія"
            on_street[0]["Recommended"], on_street[0]["Reason"] = 1, "назва й вулиця"
        elif len(named) == 1 and same_name[latin(r["Name"])] == 1:
            named[0]["Recommended"], named[0]["Reason"] = 1, "назва, одна сторінка"
        else:
            for x in named:
                x["Reason"] = "кілька філій - не видно вулиці" if len(named) > 1 else "у нас кілька закладів з цією назвою"

    # одне посилання на два наші ресторани - без рекомендації
    owners = defaultdict(set)
    for row in rows:
        if row["Recommended"]:
            owners[row["Url"]].add(row["RestaurantId"])
    for row in rows:
        if row["Recommended"] and len(owners[row["Url"]]) > 1:
            row["Recommended"], row["Reason"] = 0, "посилання знайдено для кількох наших ресторанів"
    return rows


def save(db, rows, searched_ids):
    cur = db.cursor()
    for row in rows:
        # уже відхилене чи підтверджене не повертається: ключ (ресторан, посилання) унікальний
        cur.execute("""INSERT IGNORE INTO MenuLinkProposals (RestaurantId, Platform, Url, PageTitle, PageDescription,
                                                            NameScore, Recommended, Reason, Status, CreatedAt)
                       VALUES (%s, %s, %s, %s, %s, %s, %s, %s, 0, NOW())""",
                    (row["RestaurantId"], row["Platform"], row["Url"], row["PageTitle"], row["PageDescription"],
                     row["NameScore"], row["Recommended"], row["Reason"]))
    for restaurant_id in searched_ids:
        cur.execute("INSERT INTO MenuLinkSearches (RestaurantId, SearchedAt) VALUES (%s, NOW()) "
                    "ON DUPLICATE KEY UPDATE SearchedAt = NOW()", (restaurant_id,))
    db.commit()


RESTAURANTS_SQL = """SELECT CONCAT(r.Id, '') AS Id, r.Name, r.Address, r.City FROM Restaurants r
                     LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id""" + RESTAURANT_CLICKS_JOIN + """
                     LEFT JOIN RestaurantMenuLinks ml ON ml.RestaurantId = r.Id
                     LEFT JOIN MenuLinkSearches s ON s.RestaurantId = r.Id
                     WHERE r.Status = 0 AND ml.Url IS NULL"""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true", help="лише порахувати ресторани й запити")
    parser.add_argument("--limit", type=int, default=0, help="скільки ресторанів обробити (0 - усі)")
    parser.add_argument("--import", dest="import_csv", metavar="CSV", help="пропозиції з CSV попереднього прогону")
    parser.add_argument("--catalog", choices=sorted(PLATFORMS), help="зворотний пошук: сторінки сервісу -> наші ресторани")
    parser.add_argument("--platform", choices=sorted(PLATFORMS), action="append",
                        help="шукати лише в цьому сервісі (можна кілька); ресторани, по яких уже шукали, теж")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    for sql in (MENU_LINKS_TABLE_SQL, PROPOSALS_TABLE_SQL, SEARCHES_TABLE_SQL, KEEP_INDEXED_TABLE_SQL):
        cur.execute(sql)
    cities = load_cities(db)

    if args.import_csv:
        with open(args.import_csv, encoding="utf-8-sig") as f:
            raw = [{k: row[k] for k in ("RestaurantId", "Platform", "Url", "PageTitle", "PageDescription")}
                   for row in csv.DictReader(f)]
        cur.execute(RESTAURANTS_SQL)
        restaurants = cur.fetchall()
        rows = classify(score(raw, restaurants, cities), restaurants)
        searched = {r["RestaurantId"] for r in raw} & {r["Id"] for r in restaurants}
        save(db, rows, searched)
        print(f"пропозицій: {len(rows)}, рекомендовано: {sum(r['Recommended'] for r in rows)}, ресторанів: {len(searched)}")
        db.close()
        return

    if args.catalog:
        cur.execute(RESTAURANTS_SQL)
        restaurants = cur.fetchall()
        queries = len(CATALOG_WORDS) + len({n for names in cities.values() for n in names if n})
        print(f"ресторанів без посилання: {len(restaurants)}, запитів: {queries} (~{queries * 2} кредитів Firecrawl)")
        if args.dry_run:
            db.close()
            return
        key = os.environ.get("FIRECRAWL_API_KEY")
        if not key:
            raise SystemExit("FIRECRAWL_API_KEY немає в content-bot/.env")
        pages = catalog_pages(args.catalog, cities, key)
        rows = classify(score(catalog_rows(args.catalog, pages, restaurants), restaurants, cities), restaurants)
        db.close()
        db = get_db()
        save(db, rows, [])
        db.close()
        print(f"\nсторінок {args.catalog}: {len(pages)}; пропозицій: {len(rows)}, рекомендовано: "
              f"{sum(r['Recommended'] for r in rows)}. Перевірка: адмінка -> Звіти -> Посилання на меню")
        return

    # окремий сервіс (--platform) раніше не шукали - дата попереднього пошуку його не стосується
    if args.platform:
        cur.execute(RESTAURANTS_SQL + " ORDER BY IFNULL(k.Clicks, 0) DESC, r.Name")
    else:
        cur.execute(RESTAURANTS_SQL + " AND (s.SearchedAt IS NULL OR s.SearchedAt < NOW() - INTERVAL %s DAY)"
                    " ORDER BY IFNULL(k.Clicks, 0) DESC, r.Name",
                    (SEARCH_AGAIN_DAYS,))
    restaurants = cur.fetchall()
    if args.limit:
        restaurants = restaurants[:args.limit]
    queries = len({query_name(r["Name"]).lower() for r in restaurants}) * len(args.platform or PLATFORMS)
    print(f"ресторанів для пошуку: {len(restaurants)}, запитів: {queries} (~{queries * 2} кредитів Firecrawl)")
    if args.dry_run:
        db.close()
        return

    key = os.environ.get("FIRECRAWL_API_KEY")
    if not key:
        raise SystemExit("FIRECRAWL_API_KEY немає в content-bot/.env")
    started = time.time()
    rows = classify(score(find(restaurants, key, args.platform), restaurants, cities), restaurants)
    # з'єднання могло закритися за час пошуку
    db.close()
    db = get_db()
    save(db, rows, [r["Id"] for r in restaurants])
    db.close()
    print(f"\nпропозицій: {len(rows)}, рекомендовано: {sum(r['Recommended'] for r in rows)}, "
          f"ресторанів з пропозиціями: {len({r['RestaurantId'] for r in rows})} з {len(restaurants)}; "
          f"{time.time() - started:.0f} с. Перевірка: адмінка -> Звіти -> Посилання на меню")


if __name__ == "__main__":
    main()

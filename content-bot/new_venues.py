"""Нові заклади: ті, чиє меню шукають у Google, а в нас їх немає - кодом, без AI.

1. Запити конкурентів (Serpstat, getDomainsUniqKeywords: конкурент у топ-10, ми - ні, частота >= MIN_VOLUME):
   «криївка меню», «сушія одеса», «ресторан прага меню» тощо.
2. З запиту - назва закладу (без «меню», «ціни», «ресторан», назви міста…) і місто (зі слова в запиті
   або з адреси сторінки конкурента: /kyiv_catalog/, /kiev/, /lviv/…).
3. Заклади, що вже є в нас (назва або адреса сторінки, транслітом), відкидаються.
4. Пошук в OpenStreetMap (Nominatim) по наших містах: лише amenity restaurant/cafe/bar/pub/fast_food…, назва має
   збігатися з запитом. Звідти - адреса, координати, сайт, телефон, години роботи.
5. Таблиця NewVenueCandidates (Status: new). Адмін у «Ресторанах» -> «Нові заклади» натискає «Додати»
   (approved), «Не додавати» (rejected) чи «Дубль» (duplicate).
6. --create: схвалені стають закладами (Restaurants, RestaurantInfo, RestaurantSeo), Status = added.
   Меню наповнить sync_restaurants.py (сайт / QR-меню), тип, кухні, район - venue_types.py; поки страв мало,
   thin_pages.py тримає сторінку в noindex.

Запуск:  python3 new_venues.py --dry-run          # знайти кандидатів і показати, нічого не писати
         python3 new_venues.py                    # записати нових кандидатів (раз на тиждень)
         python3 new_venues.py --create           # створити схвалені заклади (щоночі, перед sync_restaurants)
"""
import argparse
import difflib
import json
import re
import time
import uuid
from collections import defaultdict
from datetime import datetime

import requests

from sync_restaurants import NOMINATIM_USER_AGENT, get_db, get_localizable_value, normalize_name  # load_dotenv
import serpstat_competitors as ss
from dish_city_pages import slugify

MIN_VOLUME = 50
FOOD_TYPES = {"restaurant", "cafe", "bar", "pub", "fast_food", "food_court", "ice_cream", "biergarten"}
# місто: слова в запиті й шматки адреси сторінки конкурента -> slug міста в нас
CITY_HINTS = {
    "kyiv": r"київ|киев|kyiv|kiev",
    "lviv": r"львів|львов|lviv|lvov",
    "odesa": r"одес|odes",
    "kharkiv": r"харків|харьков|kharkiv|kharkov",
    "dnipro": r"дніпр|днепр|dnipro|dnepr",
}
# сторінка конкретного закладу в конкурента (а не стаття, добірка чи категорія)
VENUE_PAGE = re.compile(r"/restaurants?/[^/]+|/place[-/]|_catalog/(?!type:|kuhnja:|osobennost:)[^/]+|/food/[^/]+|/restoran/[^/]+|/kafe-[^/]+|/bar-[^/]+", re.I)
# порядок пошуку, коли місто з запиту не видно: спершу найбільше
CITY_ORDER = ["kyiv", "lviv", "odesa", "kharkiv", "dnipro"]

# слова, що не є назвою закладу
NOISE = re.compile(
    r"\b(меню|menu|ціни|цiни|цени|цены|ціна|цена|ресторан[а-яіїє]*|restaurant|кафе|cafe|café|бар|bar|паб|pub|кав.ярня|кофейня|"
    r"піцерія|пиццерия|відгуки|отзывы|адреса|адрес|доставка|сайт|офіційний|официальный|телефон|графік|години|роботи|работы|"
    r"фото|на|в|у|и|і|та|з|с|по|центр|номер|бронювання|бронирование|забронировать|"
    + "|".join(CITY_HINTS.values()) + r"|[а-яіїє]*(київ|киев|львів|львов|одес|харків|харьков|дніпр|днепр)[а-яіїє]*)\b", re.I)

CANDIDATES_SQL = """CREATE TABLE IF NOT EXISTS NewVenueCandidates (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    OsmKey VARCHAR(64) NOT NULL,
    Keyword VARCHAR(255) NOT NULL,
    Volume INT NOT NULL DEFAULT 0,
    Name VARCHAR(255) NOT NULL,
    CityId VARCHAR(255) NOT NULL,
    Address VARCHAR(500) NULL,
    Lat VARCHAR(32) NULL,
    Lon VARCHAR(32) NULL,
    Website VARCHAR(500) NULL,
    Phone VARCHAR(100) NULL,
    OpenHours VARCHAR(500) NULL,
    Cuisine VARCHAR(255) NULL,
    SourceUrl VARCHAR(500) NULL,
    Status VARCHAR(16) NOT NULL DEFAULT 'new',
    RestaurantId VARCHAR(255) NULL,
    Note VARCHAR(255) NULL,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    UNIQUE KEY UX_OsmKey (OsmKey)
) DEFAULT CHARSET=utf8mb4"""


def latin(value):
    """Назва для порівняння: транслітом, лише літери й цифри («Криївка» = «kryivka» = «Kryjivka» приблизно)."""
    s = slugify(normalize_name(value).replace("ё", "е"))
    s = s.replace("j", "i").replace("y", "i").replace("kh", "h").replace("ts", "c")
    return re.sub(r"[^a-z0-9]", "", s)


def similar(a, b):
    a, b = latin(a), latin(b)
    if not a or not b:
        return False
    return a in b or b in a or difflib.SequenceMatcher(None, a, b).ratio() >= 0.8


def venue_name(keyword):
    name = re.sub(r"\s+", " ", NOISE.sub(" ", keyword.lower())).strip(" -«»\"'")
    return name if len(name) >= 3 and not name.isdigit() else None


def city_of(text):
    for city, rx in CITY_HINTS.items():
        if re.search(rx, text or "", re.I):
            return city
    return None


def gap_keywords(limit_pages):
    """[(keyword, volume, url)] - запити конкурентів про заклади, де нас немає."""
    out = {}
    for comp in ss.COMPETITORS:
        for page in range(1, limit_pages + 1):
            try:
                part = ss.rows(ss.call("SerpstatDomainProcedure.getDomainsUniqKeywords", {
                    "se": ss.SE, "domains": [comp], "minusDomain": ss.OUR, "page": page, "size": 1000,
                    "filters": {"position_to": 10, "queries_from": MIN_VOLUME}}))
            except Exception as e:
                print(f"  Serpstat {comp}: {e}")
                break
            for k in part:
                kw = (k.get("keyword") or "").strip()
                cat = ss.category(kw)
                # лише запити про конкретний заклад: «… меню», назва закладу, «ресторан X»
                if not ss.FOOD.search(kw) and not cat.startswith("інше"):
                    continue
                if cat not in ("меню/ціни конкретного закладу", "інше (назва закладу/бренд)",
                               "тип закладу в місті/районі (кафе, бари, кав'ярні)"):
                    continue
                # без «меню» в запиті - лише якщо конкурент показує на ньому сторінку закладу
                # («облака», «донер кебаб», «колиба» - загальні слова, а не заклад)
                if cat != "меню/ціни конкретного закладу" and not VENUE_PAGE.search(k.get("url") or ""):
                    continue
                volume = int(ss.num(k, "region_queries_count"))
                if kw not in out or volume > out[kw][1]:
                    out[kw] = (kw, volume, k.get("url") or "")
            if len(part) < 1000:
                break
    return list(out.values())


def osm_search(name, city_name):
    resp = requests.get("https://nominatim.openstreetmap.org/search", params={
        "q": f"{name}, {city_name}", "countrycodes": "ua", "format": "jsonv2", "addressdetails": 1,
        "extratags": 1, "namedetails": 1, "limit": 5, "accept-language": "uk"},
        headers={"User-Agent": NOMINATIM_USER_AGENT}, timeout=20)
    time.sleep(1.1)  # правила Nominatim: не частіше 1 запиту на секунду
    return resp.json() if resp.status_code == 200 else []


def address_of(r):
    a = r.get("address") or {}
    street = a.get("road") or a.get("pedestrian") or a.get("square") or ""
    house = a.get("house_number") or ""
    return (f"{street}, {house}" if street and house else street) or (r.get("display_name") or "").split(",")[0]


def find(db, args):
    cur = db.cursor(dictionary=True)
    cur.execute(CANDIDATES_SQL)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {}
    for c in cur.fetchall():
        slug = (get_localizable_value(c["Name"], "en") or "").lower()
        if slug in CITY_HINTS:
            cities[slug] = (c["Id"], get_localizable_value(c["Name"], "uk") or slug)
    # наші заклади (усі статуси - закриті теж не додаємо вдруге)
    cur.execute("""SELECT r.Name, r.City, rs.Url FROM Restaurants r LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id""")
    ours = defaultdict(list)
    for r in cur.fetchall():
        ours[r["City"]].append((r["Name"], r["Url"] or ""))
    cur.execute("SELECT OsmKey, Name, CityId FROM NewVenueCandidates")
    known = {r["OsmKey"] for r in cur.fetchall()}

    def is_ours(name, city_id):
        return any(similar(name, n) or (u and similar(name, u.replace("-", " "))) for n, u in ours[city_id])

    print("Serpstat: запити конкурентів…", flush=True)
    keywords = gap_keywords(args.pages)
    print(f"Запитів про заклади, де нас немає (частота >= {MIN_VOLUME}): {len(keywords)}")
    # одна назва + місто -> один пошук (сумарна частота запитів)
    groups = {}
    for kw, volume, url in keywords:
        name = venue_name(kw)
        if not name:
            continue
        city = city_of(kw) or city_of(url)
        key = (latin(name), city)
        g = groups.setdefault(key, {"name": name, "city": city, "volume": 0, "keyword": kw, "url": url})
        g["volume"] += volume
        if volume > 0 and len(kw) < len(g["keyword"]):
            g["keyword"] = kw
    todo = sorted(groups.values(), key=lambda g: -g["volume"])
    if args.limit:
        todo = todo[:args.limit]
    print(f"Назв закладів: {len(todo)}; шукаю в OpenStreetMap (~1-5 с на назву, кандидати пишуться одразу)…", flush=True)

    write = db.cursor()
    found, skipped_ours, not_found = [], 0, 0
    merged = {}  # (назва транслітом, місто) -> [рядок, сумарна частота]
    now = datetime.utcnow()
    for i, g in enumerate(todo, 1):
        if i % 10 == 0:
            print(f"  … {i}/{len(todo)}: знайдено {len(merged)}, уже є в нас {skipped_ours}, не знайдено {not_found}", flush=True)
        city_slugs = [g["city"]] if g["city"] in cities else [c for c in CITY_ORDER if c in cities]
        if any(is_ours(g["name"], cities[c][0]) for c in city_slugs):
            skipped_ours += 1
            continue
        hit = None
        for c in city_slugs:
            try:
                results = osm_search(g["name"], cities[c][1])
            except requests.RequestException as e:
                print(f"  {g['name']}: Nominatim - {e}")
                continue
            for r in results:
                if r.get("category") != "amenity" or r.get("type") not in FOOD_TYPES:
                    continue
                names = [r.get("name") or ""] + list((r.get("namedetails") or {}).values())
                if not any(similar(g["name"], n) for n in names if n):
                    continue
                city_of_result = (r.get("address") or {}).get("city") or ""
                if city_of_result and not similar(city_of_result, cities[c][1]) and c != city_of(city_of_result):
                    continue
                hit = (c, r)
                break
            if hit:
                break
        if not hit:
            not_found += 1
            continue
        c, r = hit
        city_id = cities[c][0]
        name = (r.get("namedetails") or {}).get("name:uk") or r.get("name") or g["name"]
        if is_ours(name, city_id):
            skipped_ours += 1
            continue
        merge_key = (latin(name), city_id)
        if merge_key in merged:
            merged[merge_key][1] += g["volume"]
            if not args.dry_run:
                write.execute("UPDATE NewVenueCandidates SET Volume = Volume + %s WHERE OsmKey = %s AND Status = 'new'",
                              (g["volume"], merged[merge_key][0][0]))
                db.commit()
            continue
        osm_key = f"{r.get('osm_type')}/{r.get('osm_id')}"
        tags = r.get("extratags") or {}
        row = (osm_key, g["keyword"][:255], g["volume"], name[:255], city_id, address_of(r)[:500],
               str(r.get("lat") or "")[:32], str(r.get("lon") or "")[:32],
               (tags.get("website") or tags.get("contact:website") or "")[:500] or None,
               (tags.get("phone") or tags.get("contact:phone") or "")[:100] or None,
               (tags.get("opening_hours") or "")[:500] or None, (tags.get("cuisine") or "")[:255] or None,
               g["url"][:500] or None)
        merged[merge_key] = [row, g["volume"], c]
        found.append(merge_key)
        print(f"  {g['volume']:>6}  {row[3]} ({c}) - {row[5]}; сайт {row[8] or '-'}; запит «{row[1]}»"
              + ("" if osm_key not in known else " [уже в кандидатах]"), flush=True)
        if not args.dry_run and osm_key not in known:
            write.execute("""INSERT INTO NewVenueCandidates (OsmKey, Keyword, Volume, Name, CityId, Address, Lat, Lon, Website,
                                                             Phone, OpenHours, Cuisine, SourceUrl, Status, CreatedAt, UpdatedAt)
                             VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, 'new', %s, %s)""",
                          row + (now, now))
            db.commit()
            known.add(osm_key)
    if not args.dry_run:
        db.commit()
    print(f"\nЗнайдено в OSM: {len(found)}; уже є в нас: {skipped_ours}; не знайдено в OSM: {not_found}"
          + ("" if not args.dry_run else " (dry-run: нічого не записано)"))


def localizable(value):
    return json.dumps({"Values": [{"Locale": "uk", "Value": value}]}, ensure_ascii=False) if value else json.dumps({"Values": []})


def create(db, args):
    """Схвалені кандидати -> заклади (Restaurants, RestaurantInfo, RestaurantSeo)."""
    cur = db.cursor(dictionary=True)
    cur.execute(CANDIDATES_SQL)
    cur.execute("SELECT * FROM NewVenueCandidates WHERE Status = 'approved'")
    rows = cur.fetchall()
    cur.execute("SELECT Id, Name FROM Cities")
    city_slug = {c["Id"]: (get_localizable_value(c["Name"], "en") or "").lower() for c in cur.fetchall()}
    now = datetime.now()
    empty = json.dumps({"Values": []})
    for row in rows:
        rid = str(uuid.uuid4())
        base = slugify(normalize_name(row["Name"])).strip("-") or "zaklad"
        slug = base
        for candidate in (base, f"{base}-{city_slug.get(row['CityId'], '')}".strip("-"), f"{base}-{rid[:4]}"):
            cur.execute("SELECT 1 FROM RestaurantSeo WHERE Url = %s", (candidate,))
            if not cur.fetchone():
                slug = candidate
                break
        cur.execute("""INSERT INTO Restaurants (Id, Name, Email, Phone, Address, City, Status, UpdateDate, CreateDate,
                                                Moderated, IsModeratable)
                       VALUES (%s, %s, '', %s, %s, %s, 0, %s, %s, 1, 0)""",
                    (rid, row["Name"], row["Phone"] or "", row["Address"] or "", row["CityId"], now, now))
        cur.execute("""INSERT INTO RestaurantInfo (Id, RestaurantId, Photos, Website, OpenHours, UserAllowedAddMenu,
                                                   UserAllowedOpenMenu, UserAllowedAddDishes, IsFeatured, ShowOnIndex,
                                                   Currency, ShortInfo, FullInfo, Cuisine, DisplayAddress, GpsLatitude, GpsLongitude)
                       VALUES (%s, %s, '[]', %s, %s, 1, 1, 1, 0, 0, '₴', %s, %s, %s, %s, %s, %s)""",
                    (str(uuid.uuid4()), rid, row["Website"] or "", row["OpenHours"] or "", empty, empty,
                     localizable(row["Cuisine"]), localizable(row["Address"]), row["Lat"], row["Lon"]))
        cur.execute("""INSERT INTO RestaurantSeo (Id, ItemId, Url, Header, Title, Description, Keywords, SearchTerms, SeoText, Tags)
                       VALUES (%s, %s, %s, %s, %s, %s, '', '', '', '')""",
                    (str(uuid.uuid4()), rid, slug, empty, empty, empty))
        cur.execute("UPDATE NewVenueCandidates SET Status = 'added', RestaurantId = %s, UpdatedAt = UTC_TIMESTAMP() WHERE Id = %s",
                    (rid, row["Id"]))
        print(f"  {row['Name']} -> /{city_slug.get(row['CityId'])}/restaurant/{slug}")
    if args.dry_run:
        db.rollback()
        print(f"перевірка: створилось би {len(rows)}; нічого не записано")
    else:
        db.commit()
        print(f"створено закладів: {len(rows)}. Меню й тип/кухні/район бот підтягне наступної ночі.")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--create", action="store_true", help="створити схвалені в адмінці заклади")
    ap.add_argument("--pages", type=int, default=2, help="сторінок по 1000 запитів на конкурента (кредити Serpstat)")
    ap.add_argument("--limit", type=int, default=150, help="не більше N найчастіших назв (0 - усі; ~1-5 с на назву)")
    args = ap.parse_args()
    db = get_db()
    if args.create:
        create(db, args)
    else:
        if not ss.TOKEN:
            raise SystemExit("Немає SERPSTAT_TOKEN / SERPSTAT_API_KEY у .env")
        find(db, args)
    db.close()


if __name__ == "__main__":
    main()

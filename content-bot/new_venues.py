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

7. --menu-links: відхилені в «Звіти» -> «Посилання на меню» сторінки QR-меню (Expirenza, ChoiceQR, RestoApp) - це меню
   закладів, яких у нас немає (інша назва, адреса, філія). Живі (меню відкривається, є страви) стають кандидатами
   (OsmKey «menu:<посилання>»): назва - з заголовка сторінки, місто - того закладу, для якого шукали, адреса й
   координати - з OSM, якщо знайдеться. Адмін підтверджує їх у «Нових закладах»; --create записує посилання на меню
   в картку (RestaurantMenuLinks), і sync_restaurants.py бере страви, ціни й фото з QR-меню.

Запуск:  python3 new_venues.py --dry-run          # знайти кандидатів і показати, нічого не писати
         python3 new_venues.py --menu-links --dry-run  # кандидати з відхилених посилань на QR-меню
         python3 new_venues.py                    # записати нових кандидатів (раз на тиждень)
         python3 new_venues.py --create           # створити схвалені заклади (щоночі, перед sync_restaurants)
         python3 new_venues.py --duplicates --dry-run  # показати можливі дублі наявних закладів
         python3 new_venues.py --duplicates       # позначити їх (Status suspect -> вкладка «Можливі дублі»)

Дублі: схожа назва (транслітом) у тому ж місті І (до DUP_METERS метрів за координатами АБО та сама вулиця й номер).
--create спершу перевіряє дублі, тож схвалений дубль не створюється.
"""
import math
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


DUP_METERS = 150
DUP_COLUMNS_SQL = [
    "ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS DuplicateOf VARCHAR(255) NULL",
    "ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS PrevStatus VARCHAR(16) NULL",
    "ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS NotDuplicate TINYINT(1) NOT NULL DEFAULT 0",
]
STREET_NOISE = re.compile(r"\b(вул|вулиця|ул|улица|просп|проспект|пр|бульв|бульвар|бул|пров|провулок|пер|переулок|пл|площа|площадь|"
                          r"шосе|шоссе|наб|набережна|набережная|узвіз|спуск|str|street|ave|avenue)\b\.?", re.I)


def meters(lat1, lon1, lat2, lon2):
    try:
        lat1, lon1, lat2, lon2 = (float(x) for x in (lat1, lon1, lat2, lon2))
    except (TypeError, ValueError):
        return None
    if not (lat1 and lon1 and lat2 and lon2):
        return None
    k = math.pi / 180
    x = (lon2 - lon1) * k * math.cos((lat1 + lat2) / 2 * k)
    y = (lat2 - lat1) * k
    return round(math.hypot(x, y) * 6371000)


def address_key(address):
    """«вул. Чкалова, 2Д» / «ул. Чкалова 2-д» -> ("chkal", "2d"): початок назви вулиці + номер будинку."""
    text = STREET_NOISE.sub(" ", (address or "").lower())
    number = re.search(r"\b(\d+)\s*[-/]?\s*([а-яіїєa-z])?\b", text)
    words = [w for w in re.findall(r"[a-zа-яіїєґ']{4,}", text)]
    if not number or not words:
        return None
    return latin(max(words, key=len))[:5], number.group(1) + latin(number.group(2) or "")


def duplicates(db, dry_run):
    """Кандидати (нові, схвалені, уже додані), що збігаються з наявним закладом -> Status suspect."""
    cur = db.cursor(dictionary=True)
    cur.execute(CANDIDATES_SQL)
    for sql in DUP_COLUMNS_SQL:
        cur.execute(sql)
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, CONCAT(r.City, '') AS City, r.Status, r.Address,
                          ri.GpsLatitude AS Lat, ri.GpsLongitude AS Lon, rs.Url AS Slug
                   FROM Restaurants r LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id""")
    by_city = defaultdict(list)
    for r in cur.fetchall():
        by_city[r["City"]].append(r)
    cur.execute("""SELECT Id, Name, CityId, Address, Lat, Lon, Status, RestaurantId FROM NewVenueCandidates
                   WHERE Status IN ('new', 'approved', 'added') AND NotDuplicate = 0 ORDER BY Id""")
    candidates = cur.fetchall()
    # заклад, створений з кандидата: дублем вважаємо пізніший (більший Id кандидата), а не обидва
    created_by = {c["RestaurantId"]: c["Id"] for c in candidates if c["RestaurantId"]}
    cur.execute("SELECT RestaurantId, Id FROM NewVenueCandidates WHERE RestaurantId IS NOT NULL")
    created_by.update({row["RestaurantId"]: row["Id"] for row in cur.fetchall()})
    write = db.cursor()
    found = 0
    for c in candidates:
        own_key = address_key(c["Address"])
        best = None
        for r in by_city.get(c["CityId"], []):
            if r["Id"] == c["RestaurantId"] or created_by.get(r["Id"], 0) > c["Id"]:
                continue
            if not similar(c["Name"], r["Name"]):
                continue
            dist = meters(c["Lat"], c["Lon"], r["Lat"], r["Lon"])
            same_address = own_key is not None and own_key == address_key(r["Address"])
            if (dist is not None and dist <= DUP_METERS) or same_address:
                rank = dist if dist is not None else DUP_METERS
                if best is None or rank < best[0]:
                    best = (rank, r, dist, same_address)
        if not best:
            continue
        _, r, dist, same_address = best
        found += 1
        status = {0: "активний", 1: "прихований", 3: "закритий"}.get(r["Status"], f"статус {r['Status']}")
        why = ", ".join(x for x in [f"{dist} м" if dist is not None else "", "та сама адреса" if same_address else ""] if x)
        note = f"Схожий: {r['Name']} ({status}), {r['Address'] or ''}; {why}"[:255]
        print(f"  {c['Name']} [{c['Status']}] ~ {r['Name']} /{r['Slug']} ({status}; {why})")
        if not dry_run:
            write.execute("""UPDATE NewVenueCandidates SET PrevStatus = Status, Status = 'suspect', DuplicateOf = %s, Note = %s,
                             UpdatedAt = UTC_TIMESTAMP() WHERE Id = %s""", (r["Id"], note, c["Id"]))
    if not dry_run:
        db.commit()
    print(f"Можливих дублів: {found}" + (" (перевірка, нічого не записано)" if dry_run else " -> «Нові заклади» → «Можливі дублі»"))


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


MENU_TITLE_NOISE = re.compile(r"^\s*(меню|menu|онлайн[- ]меню|электронное меню|електронне меню)\s*[:\-–—]?\s*", re.I)
MENU_SERVICE = re.compile(r"choiceqr|expirenza|expz|restoapp|доставка|delivery|з собою|навынос|takeaway|онлайн|online|меню|menu", re.I)


def menu_page_name(title, url):
    """Назва закладу з заголовка сторінки QR-меню («Меню Кафе Ранок | ChoiceQR») або з піддомену.
    Частини в дужках («(ТРЦ Атмосфера)» - місце, не назва) і службові («MENU», «/») не беруться."""
    for part in re.split(r"\s*[|/]\s*|\s+[-–—]\s+", title or ""):
        part = re.sub(r"\([^)]*\)?", " ", part)  # «Сільпо (ТРЦ Атмосфера)» -> «Сільпо»
        part = MENU_TITLE_NOISE.sub("", part)
        part = re.sub(r"\b(menu|меню)\b", " ", part, flags=re.I)
        part = re.sub(r"\s+", " ", part).strip(" «»\"'.,:;-–—")
        if len(re.sub(r"[\W\d_]", "", part)) >= 2 and not MENU_SERVICE.fullmatch(part.lower()):
            return part[:255]
    m = re.match(r"https?://([a-z0-9_-]+)\.(?:choiceqr\.com|restoapp\.eu)", url or "", re.I)
    return re.sub(r"[-_]+", " ", m.group(1)).title() if m else None


def qr_place(url):
    """Адреса, координати, телефон, години (JSON perDay) і сайт зі сторінки QR-меню (sync_platform_contacts)."""
    from sync_platform_contacts import platform_contacts
    try:
        c = platform_contacts(url) or {}
    except Exception as e:  # сервіс не відповів - без контактів
        print(f"  ! контакти {url}: {e}")
        return {}
    point = c.get("point") or (None, None)
    return {"address": c.get("address"), "lat": str(point[0])[:32] if point[0] else None,
            "lon": str(point[1])[:32] if point[1] else None, "phone": (c.get("phone") or "")[:100] or None,
            "hours": json.dumps({"perDay": True, "data": c["hours"]}, ensure_ascii=False) if c.get("hours") else None,
            "website": (c.get("website") or "")[:500] or None}


def from_menu_links(db, args):
    """Відхилені посилання на QR-меню -> кандидати в нові заклади (див. п. 7 у описі)."""
    from menu_platforms import platform_menu
    cur = db.cursor(dictionary=True)
    cur.execute(CANDIDATES_SQL)
    cur.execute("""SELECT p.Url, MAX(p.PageTitle) AS PageTitle, MAX(p.PageDescription) AS PageDescription,
                          SUBSTRING_INDEX(GROUP_CONCAT(CONCAT(r.City, '') ORDER BY p.Id), ',', 1) AS CityId
                   FROM MenuLinkProposals p JOIN Restaurants r ON r.Id = p.RestaurantId
                   WHERE p.Status = 2
                     AND p.Url NOT IN (SELECT Url FROM MenuLinkProposals WHERE Status = 1)
                     AND p.Url NOT IN (SELECT Url FROM RestaurantMenuLinks)
                   GROUP BY p.Url""")
    rejected = cur.fetchall()
    cur.execute("SELECT OsmKey FROM NewVenueCandidates WHERE OsmKey LIKE 'menu:%'")
    known = {r["OsmKey"] for r in cur.fetchall()}
    cur.execute("SELECT Id, Name FROM Cities")
    city_name = {c["Id"]: get_localizable_value(c["Name"], "uk") for c in cur.fetchall()}
    # наші заклади: схожа назва - підказка адміну (інша філія мережі чи той самий заклад)
    cur.execute("SELECT r.Name, CONCAT(r.City, '') AS City FROM Restaurants r WHERE r.Status IN (0, 3)")
    ours = defaultdict(list)
    for r in cur.fetchall():
        ours[r["City"]].append(r["Name"])
    print(f"відхилених посилань на QR-меню: {len(rejected)}")
    now, added, dead = datetime.now(), 0, 0
    write = db.cursor()
    for p in rejected:
        key = ("menu:" + p["Url"])[:64] if len("menu:" + p["Url"]) <= 64 else "menu:" + uuid.uuid5(uuid.NAMESPACE_URL, p["Url"]).hex
        if key in known:
            continue
        name = menu_page_name(p["PageTitle"], p["Url"])
        if not name:
            continue
        try:
            dishes = len(platform_menu(p["Url"]) or [])
        except Exception as e:  # сервіс не відповів - наступного разу
            print(f"  ! {p['Url']}: {e}")
            continue
        if dishes == 0:
            dead += 1
            continue
        address = lat = lon = website = phone = hours = cuisine = None
        # адреса, координати, телефон і години - спершу з самого QR-меню (його веде заклад), решту доповнює OSM
        qr = qr_place(p["Url"])
        if qr.get("address"):
            address, lat, lon, phone, hours, website = (qr["address"], qr["lat"], qr["lon"], qr["phone"], qr["hours"], qr["website"])
        hits = [] if address and lat else [r for r in osm_search(name, city_name.get(p["CityId"]) or "")
                if (r.get("type") in FOOD_TYPES or r.get("category") == "amenity")
                and similar(name, (r.get("namedetails") or {}).get("name") or r.get("name") or "")]
        if hits:
            r = hits[0]
            tags = r.get("extratags") or {}
            address = address or address_of(r)[:500]
            lat, lon = lat or str(r.get("lat") or "")[:32], lon or str(r.get("lon") or "")[:32]
            website = website or (tags.get("website") or tags.get("contact:website") or "")[:500] or None
            phone = phone or (tags.get("phone") or tags.get("contact:phone") or "")[:100] or None
            hours = hours or (tags.get("opening_hours") or "")[:500] or None
            cuisine = (tags.get("cuisine") or "")[:255] or None
        alike = next((n for n in ours[p["CityId"]] if similar(name, n)), None)
        note = f"QR-меню: {dishes} страв" + (f" · схоже на наш «{alike}» (інша філія?)" if alike else "")
        print(f"  {name} ({city_name.get(p['CityId'])}) - {note}; {address or 'адреси немає ні в QR-меню, ні в OSM'}; {p['Url']}")
        added += 1
        if not args.dry_run:
            write.execute("""INSERT IGNORE INTO NewVenueCandidates (OsmKey, Keyword, Volume, Name, CityId, Address, Lat, Lon, Website,
                                                                    Phone, OpenHours, Cuisine, SourceUrl, Status, CreatedAt, UpdatedAt)
                             VALUES (%s, %s, 0, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, 'new', %s, %s)""",
                          (key, note[:255], name, p["CityId"], address, lat, lon, website, phone, hours,
                           cuisine, p["Url"][:500], now, now))
            db.commit()
    # кандидати, додані раніше без адреси - адреса з QR-меню
    cur.execute("""SELECT Id, Name, SourceUrl FROM NewVenueCandidates
                   WHERE OsmKey LIKE 'menu:%%' AND Status IN ('new', 'approved', 'suspect') AND (Address IS NULL OR Address = '')""")
    filled = 0
    for c in cur.fetchall():
        qr = qr_place(c["SourceUrl"])
        if not qr.get("address"):
            continue
        filled += 1
        print(f"  адреса з QR-меню: {c['Name']} - {qr['address']}")
        if not args.dry_run:
            write.execute("""UPDATE NewVenueCandidates SET Address = %s, Lat = IFNULL(%s, Lat), Lon = IFNULL(%s, Lon),
                                    Phone = IFNULL(Phone, %s), OpenHours = IFNULL(OpenHours, %s), Website = IFNULL(Website, %s),
                                    UpdatedAt = UTC_TIMESTAMP() WHERE Id = %s""",
                          (qr["address"], qr["lat"], qr["lon"], qr["phone"], qr["hours"], qr["website"], c["Id"]))
            db.commit()
    print(f"\nкандидатів: {added}; меню не відкривається або без страв: {dead}; адресу з QR-меню отримали раніше додані: {filled}"
          + (" (dry-run: нічого не записано)" if args.dry_run else ". Перевірка: «Ресторани» -> «Нові заклади»"))


def localizable(value):
    return json.dumps({"Values": [{"Locale": "uk", "Value": value}]}, ensure_ascii=False) if value else json.dumps({"Values": []})


def create(db, args):
    """Схвалені кандидати -> заклади (Restaurants, RestaurantInfo, RestaurantSeo). Спершу - перевірка дублів."""
    duplicates(db, args.dry_run)
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
        if (row["OsmKey"] or "").startswith("menu:") and row["SourceUrl"]:
            # кандидат із QR-меню: посилання на меню - у картку, страви й ціни бере sync_restaurants.py
            cur.execute("""INSERT INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (%s, %s, NOW())
                           ON DUPLICATE KEY UPDATE Url = VALUES(Url), UpdatedAt = NOW()""", (rid, row["SourceUrl"]))
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
    ap.add_argument("--duplicates", action="store_true", help="позначити кандидатів, схожих на наявні заклади")
    ap.add_argument("--menu-links", action="store_true", help="кандидати з відхилених посилань на QR-меню («Звіти»)")
    ap.add_argument("--pages", type=int, default=2, help="сторінок по 1000 запитів на конкурента (кредити Serpstat)")
    ap.add_argument("--limit", type=int, default=150, help="не більше N найчастіших назв (0 - усі; ~1-5 с на назву)")
    args = ap.parse_args()
    db = get_db()
    if args.menu_links:
        from_menu_links(db, args)
        duplicates(db, args.dry_run)  # схожі на наявні заклади - у вкладку «Можливі дублі»
    elif args.duplicates:
        duplicates(db, args.dry_run)
    elif args.create:
        create(db, args)
    else:
        if not ss.TOKEN:
            raise SystemExit("Немає SERPSTAT_TOKEN / SERPSTAT_API_KEY у .env")
        find(db, args)
    db.close()


if __name__ == "__main__":
    main()

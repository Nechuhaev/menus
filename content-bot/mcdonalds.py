"""McDonald's Україна: ресторани в наших містах - сторінки закладів з меню. Кодом, без AI.

1. Ресторани - з OpenStreetMap (Overpass, відкриті дані): бренд McDonald's (brand:wikidata=Q38076) навколо центру
   наших міст (Київ, Львів, Одеса, Харків, Дніпро); якщо в OSM указано місто (addr:city) - лише наше. Адреса, координати,
   години (opening_hours), телефон. Без вулиці в OSM заклад пропускається (видно в звіті).
2. Наявні на сайті McDonald's (до 150 м) не створюються вдруге - лише прив'язуються до точки OSM.
3. Новий заклад: Restaurants, RestaurantInfo, RestaurantSeo (сторінка mcdonalds-<вулиця-номер>),
   посилання на меню «mcdonalds://ua» у RestaurantMenuLinks.
4. Меню - menu_platforms.read_mcdonalds зі збережених адміном сторінок сайту McDonald's (content-bot/mcdonalds/*.html;
   сайт закриває автоматичні запити): sync_restaurants.py пропонує страви в «Звіти», адмін підтверджує один раз
   «разом із філіями» (філії - за однаковим сайтом). Мережу (/merezha/...) складе venue_types.py.
5. McdStores (точка OSM -> наш заклад): повторний запуск додає лише нові, а зниклі з OSM показує (не закриває сам).

Запуск:  python3 mcdonalds.py --dry-run   # показати, нічого не записувати
         python3 mcdonalds.py             # створити нові заклади
"""
import argparse
import json
import math
import re
import time
import uuid
from datetime import datetime

import requests

from dish_city_pages import slugify
from sync_restaurants import NOMINATIM_USER_AGENT, get_db, get_localizable_value, normalize_name, osm_to_week

# публічні сервери Overpass: головний часто перевантажений (502/504/429) - повтор і дзеркала
OVERPASS = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
            "https://overpass.private.coffee/api/interpreter"]


def overpass(query):
    last = None
    for attempt in range(3):
        for url in OVERPASS:
            try:
                r = requests.post(url, data={"data": query}, headers={"User-Agent": NOMINATIM_USER_AGENT}, timeout=120)
                if r.status_code == 200:
                    return r.json()
                last = f"{url}: {r.status_code}"
            except (requests.RequestException, ValueError) as e:
                last = f"{url}: {type(e).__name__}"
        time.sleep(15 * (attempt + 1))
    raise SystemExit(f"Overpass не відповідає ({last}) - спробуйте пізніше")
NAME = "McDonald's"
WEBSITE = "https://www.mcdonalds.com/ua/uk-ua.html"
MENU_LINK = "mcdonalds://ua"
CUISINE = "Американська кухня, фастфуд"
# наші міста: slug -> (назви для addr:city, центр, радіус пошуку в метрах - межі міста без передмість)
CITIES = {
    "kyiv": ({"київ", "киев", "kyiv", "kiev"}, (50.45, 30.52), 17000),
    "lviv": ({"львів", "львов", "lviv"}, (49.84, 24.03), 9000),
    "odesa": ({"одеса", "одесса", "odesa", "odessa"}, (46.46, 30.72), 12000),
    "kharkiv": ({"харків", "харьков", "kharkiv"}, (49.99, 36.25), 12000),
    "dnipro": ({"дніпро", "днепр", "dnipro"}, (48.46, 35.03), 13000),
}
SAME_PLACE_METERS = 150

TABLE_SQL = """CREATE TABLE IF NOT EXISTS McdStores (
    StoreId VARCHAR(64) NOT NULL PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    Address VARCHAR(500) NULL,
    SeenAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


STREET_WORDS = re.compile(r"\b(вулиця|вул|проспект|просп|пр-т|бульвар|бульв|площа|пл|шосе|провулок|пров|набережна|узвіз|майдан|траса)\b\.?", re.I)


def page_slug(address):
    """«вулиця Софіївська, 1/2» -> «mcdonalds-sofiivska-1-2»."""
    return "mcdonalds-" + slugify(normalize_name(STREET_WORDS.sub(" ", address or ""))).strip("-")[:50].strip("-")


def meters(lat1, lon1, lat2, lon2):
    p = math.pi / 180
    a = (math.sin((lat2 - lat1) * p / 2) ** 2
         + math.cos(lat1 * p) * math.cos(lat2 * p) * math.sin((lon2 - lon1) * p / 2) ** 2)
    return 12742000 * math.asin(math.sqrt(a))


def stores():
    """{точка OSM: ресторан} у наших містах; друге значення - скільки пропущено без адреси."""
    found, no_street = {}, 0
    for slug, (names, (lat, lon), radius) in CITIES.items():
        query = f"""[out:json][timeout:90];
            nwr["brand:wikidata"="Q38076"](around:{radius},{lat},{lon});
            out center tags;"""
        for el in overpass(query).get("elements") or []:
            tags = el.get("tags") or {}
            city = (tags.get("addr:city") or "").strip().lower()
            if city and city not in names:
                continue  # передмістя (Бровари, Вишневе) - не наше місто
            street = (tags.get("addr:street:uk") or tags.get("addr:street") or "").strip()
            house = (tags.get("addr:housenumber") or "").strip()
            if not street:
                no_street += 1
                continue
            point = el if "lat" in el else (el.get("center") or {})
            key = f"{el.get('type')}/{el.get('id')}"
            if key in found:
                continue
            found[key] = {"id": key, "city": slug, "address": f"{street}, {house}" if house else street,
                          "phone": (tags.get("phone") or tags.get("contact:phone") or "").strip(),
                          "lat": point.get("lat"), "lon": point.get("lon"),
                          "hours": (tags.get("opening_hours") or "").strip()}
        time.sleep(2)  # Overpass: не частіше
    return found, no_street


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    found, no_street = stores()
    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(TABLE_SQL)
    cur.execute("SELECT Id, Name FROM Cities")
    city_id = {(get_localizable_value(c["Name"], "en") or "").lower(): c["Id"] for c in cur.fetchall()}
    cur.execute("SELECT StoreId, RestaurantId FROM McdStores")
    known = {r["StoreId"]: r["RestaurantId"] for r in cur.fetchall()}
    # McDonald's, що вже є на сайті (додані раніше вручну чи ботом)
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, CONCAT(r.City, '') AS City, ri.GpsLatitude AS Lat, ri.GpsLongitude AS Lon
                   FROM Restaurants r LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   WHERE r.Status IN (0, 1, 3) AND (LOWER(r.Name) LIKE '%%mcdonald%%' OR LOWER(r.Name) LIKE '%%макдональд%%')""")
    existing = cur.fetchall()
    cur.execute("SELECT Url FROM RestaurantSeo")
    slugs = {r["Url"] for r in cur.fetchall()}

    def existing_near(store):
        for e in existing:
            try:
                if e["City"] == city_id.get(store["city"]) and e["Lat"] and store["lat"] is not None \
                        and meters(float(e["Lat"]), float(e["Lon"]), store["lat"], store["lon"]) <= SAME_PLACE_METERS:
                    return e["Id"]
            except (TypeError, ValueError):
                continue
        return None

    now = datetime.now()
    empty = json.dumps({"Values": []})
    write = db.cursor()
    created = linked = 0
    by_city = {}
    for store in sorted(found.values(), key=lambda s: (s["city"], s["address"])):
        by_city[store["city"]] = by_city.get(store["city"], 0) + 1
        if store["id"] in known:
            continue
        near = existing_near(store)
        if near:
            print(f"  уже є на сайті: {store['city']} {store['address']}")
            linked += 1
            if not args.dry_run:
                write.execute("INSERT IGNORE INTO McdStores (StoreId, RestaurantId, Address, SeenAt) VALUES (%s, %s, %s, NOW())",
                              (store["id"], near, store["address"][:500]))
            continue
        rid = str(uuid.uuid4())
        base = page_slug(store["address"])
        if base == "mcdonalds-":
            base += store["id"]
        slug = next(s for s in (base, f"{base}-{store['city']}", f"{base}-{rid[:4]}") if s not in slugs)
        slugs.add(slug)
        week = osm_to_week(store["hours"]) if store["hours"] else None
        hours = json.dumps({"perDay": True, "data": week}, ensure_ascii=False) if week else ""
        address = json.dumps({"Values": [{"Locale": "uk", "Value": store["address"]}]}, ensure_ascii=False)
        cuisine = json.dumps({"Values": [{"Locale": "uk", "Value": CUISINE}]}, ensure_ascii=False)
        print(f"  + {store['city']:<8} {store['address'][:50]:<50} /{slug}")
        created += 1
        if args.dry_run:
            continue
        write.execute("""INSERT INTO Restaurants (Id, Name, Email, Phone, Address, City, Status, UpdateDate, CreateDate,
                                                  Moderated, IsModeratable)
                         VALUES (%s, %s, '', %s, %s, %s, 0, %s, %s, 1, 0)""",
                      (rid, NAME, store["phone"][:100], store["address"][:500], city_id[store["city"]], now, now))
        write.execute("""INSERT INTO RestaurantInfo (Id, RestaurantId, Photos, Website, OpenHours, UserAllowedAddMenu,
                                                     UserAllowedOpenMenu, UserAllowedAddDishes, IsFeatured, ShowOnIndex,
                                                     Currency, ShortInfo, FullInfo, Cuisine, DisplayAddress, GpsLatitude, GpsLongitude)
                         VALUES (%s, %s, '[]', %s, %s, 1, 1, 1, 0, 0, '₴', %s, %s, %s, %s, %s, %s)""",
                      (str(uuid.uuid4()), rid, WEBSITE, hours, empty, empty, cuisine, address,
                       str(store["lat"] or ""), str(store["lon"] or "")))
        write.execute("""INSERT INTO RestaurantSeo (Id, ItemId, Url, Header, Title, Description, Keywords, SearchTerms, SeoText, Tags)
                         VALUES (%s, %s, %s, %s, %s, %s, '', '', '', '')""", (str(uuid.uuid4()), rid, slug, empty, empty, empty))
        write.execute("""INSERT INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (%s, %s, NOW())
                         ON DUPLICATE KEY UPDATE Url = VALUES(Url), UpdatedAt = NOW()""", (rid, MENU_LINK))
        write.execute("INSERT INTO McdStores (StoreId, RestaurantId, Address, SeenAt) VALUES (%s, %s, %s, NOW())",
                      (store["id"], rid, store["address"][:500]))
        db.commit()
    gone = [s for s in known if s not in found]
    if not args.dry_run:
        write.executemany("UPDATE McdStores SET SeenAt = NOW() WHERE StoreId = %s", [(s,) for s in found if s in known])
        db.commit()
    print(f"\nресторанів McDonald's у наших містах (OSM): {len(found)} {by_city}; нових: {created}; уже були на сайті: {linked}; "
          f"без вулиці в OSM (пропущено): {no_street}")
    if gone:
        print(f"зникли з OSM (можливо, закрились - перевірте в адмінці): {len(gone)}")
        cur.execute("SELECT m.StoreId, m.Address FROM McdStores m WHERE m.StoreId IN (" + ",".join(["%s"] * len(gone)) + ")", gone)
        for r in cur.fetchall():
            print(f"  - {r['Address']}")
    print("нічого не записано (--dry-run)" if args.dry_run else
          "далі: збережіть сторінки меню McDonald's у content-bot/mcdonalds/, sync_restaurants.py запропонує меню -> «Звіти», підтвердіть «разом із філіями»; venue_types.py складе мережу")
    db.close()


if __name__ == "__main__":
    main()

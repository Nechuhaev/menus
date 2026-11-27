"""McDonald's Україна: усі ресторани в наших містах - сторінки закладів з меню. Кодом, без AI.

1. Ресторани - з публічного API пошуку ресторанів McDonald's (адреса, координати, телефон, години по днях);
   лише відкриті (openstatus OPEN) і лише в наших містах (addressLine3 = Київ, Львів, Одеса, Харків, Дніпро).
2. Наявні на сайті McDonald's (схожа назва до 150 м) не створюються вдруге - лише прив'язуються до ресторану API.
3. Новий заклад: Restaurants, RestaurantInfo (години, телефон, координати), RestaurantSeo
   (адреса сторінки mcdonalds-<вулиця>), посилання на меню «mcdonalds://ua» у RestaurantMenuLinks.
4. Меню - menu_platforms.read_mcdonalds (одне на всі філії, без цін - на сайті McDonald's їх немає):
   sync_restaurants.py пропонує страви в «Звіти», адмін підтверджує один раз «разом із філіями».
   Мережу (сторінка /merezha/...) складе venue_types.py - однакова назва в місті.
5. Таблиця McdStores (ресторан API -> наш заклад): повторний запуск додає лише нові, а ресторани, яких більше немає
   в API, показує (не закриває сам - рішення за адміном).

Запуск:  python3 mcdonalds.py --dry-run   # показати, нічого не записувати
         python3 mcdonalds.py             # створити нові заклади
"""
import argparse
import json
import math
import re
import uuid
from datetime import datetime

import requests

from dish_city_pages import slugify
from sync_restaurants import get_db, get_localizable_value, normalize_name, osm_to_week

API = "https://www.mcdonalds.com/googleappsv2/geolocation"
HEADERS = {"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)", "Accept-Language": "uk"}
NAME = "McDonald's"
WEBSITE = "https://www.mcdonalds.com/ua/uk-ua.html"
MENU_LINK = "mcdonalds://ua"
CUISINE = "Американська кухня, фастфуд"
# наші міста: slug -> (назва в API, точки пошуку - API віддає ~50 найближчих, тож Київ - кількома точками)
CITIES = {
    "kyiv": ("Київ", [(50.45, 30.52), (50.50, 30.60), (50.40, 30.40), (50.40, 30.65), (50.52, 30.45)]),
    "lviv": ("Львів", [(49.84, 24.03)]),
    "odesa": ("Одеса", [(46.48, 30.72)]),
    "kharkiv": ("Харків", [(49.99, 36.23)]),
    "dnipro": ("Дніпро", [(48.46, 35.05)]),
}
SAME_PLACE_METERS = 150
DAYS = [("hoursMonday", "Mo"), ("hoursTuesday", "Tu"), ("hoursWednesday", "We"), ("hoursThursday", "Th"),
        ("hoursFriday", "Fr"), ("hoursSaturday", "Sa"), ("hoursSunday", "Su")]

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
    """{store_id: ресторан API} у наших містах."""
    found = {}
    for slug, (city, points) in CITIES.items():
        for lat, lon in points:
            r = requests.get(API, params={"latitude": lat, "longitude": lon, "radius": 50, "maxResults": 200,
                                          "country": "ua", "language": "uk-ua"}, headers=HEADERS, timeout=30)
            r.raise_for_status()
            for f in r.json().get("features") or []:
                p = f.get("properties") or {}
                if (p.get("addressLine3") or "").strip() != city or p.get("openstatus") != "OPEN":
                    continue
                store_id = p.get("identifierValue") or (p.get("id") or "").split(":")[0]
                lon_, lat_ = (f.get("geometry") or {}).get("coordinates") or [None, None]
                found[store_id] = {"id": store_id, "city": slug, "address": (p.get("name") or p.get("addressLine1") or "").strip(),
                                   "phone": (p.get("telephone") or "").strip(), "lat": lat_, "lon": lon_,
                                   "hours": p.get("restauranthours") or {}}
    return found


def osm_hours(hours):
    """{"hoursMonday": "06:00 - 22:00", ...} -> «Mo 06:00-22:00; Tu ...» (формат osm_to_week)."""
    parts = []
    for key, day in DAYS:
        value = (hours.get(key) or "").replace(" ", "")
        if not value:
            return None
        if value.lower() in ("closed", "зачинено"):
            parts.append(f"{day} off")
        else:
            parts.append(f"{day} {value}")
    return "; ".join(parts)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    found = stores()
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
        week = osm_to_week(osm_hours(store["hours"]) or "")
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
    print(f"\nресторанів McDonald's у наших містах: {len(found)} {by_city}; нових: {created}; уже були на сайті: {linked}")
    if gone:
        print(f"зникли з API (можливо, закрились - перевірте в адмінці): {len(gone)}")
        cur.execute("SELECT m.StoreId, m.Address FROM McdStores m WHERE m.StoreId IN (" + ",".join(["%s"] * len(gone)) + ")", gone)
        for r in cur.fetchall():
            print(f"  - {r['Address']}")
    print("нічого не записано (--dry-run)" if args.dry_run else
          "далі: sync_restaurants.py запропонує меню -> «Звіти», підтвердіть «разом із філіями»; venue_types.py складе мережу")
    db.close()


if __name__ == "__main__":
    main()

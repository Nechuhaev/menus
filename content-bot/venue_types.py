"""Тип закладу і район для каталогу міста (/{мова}/{місто}/restaurants/{тип}) - кодом, без AI.

Тип (VENUE_TYPES): спершу за назвою закладу («бар», «паб», «coffee», «піца», «суші»…), потім за полем «Кухня»
і складом меню (частка піци, суші, кави, напоїв); інакше - «ресторан».
Район: за GPS закладу через OpenStreetMap Nominatim (reverse), назви uk/ru/en, один раз на заклад
(повторно - лише якщо змінились координати). Ручні зміни з адмінки (TypeManual / DistrictManual) бот не чіпає.

Таблиця RestaurantVenue: RestaurantId, VenueType, TypeReason, TypeManual, DistrictUk/Ru/En, DistrictSlug,
DistrictManual, Lat/Lon (координати, за якими визначено район), UpdatedAt.

Запуск:  python3 venue_types.py --dry-run          # показати типи (нічого не пише)
         python3 venue_types.py                    # записати типи + райони нових закладів (cron щоночі)
         python3 venue_types.py --no-districts     # лише типи
         python3 venue_types.py --restaurant <slug> --dry-run   # пояснити тип одного закладу
"""
import argparse
import re
import time
from collections import Counter, defaultdict

import requests

from dish_city_candidates import DRINK_CATEGORY
from dish_city_pages import slugify
from sync_restaurants import NOMINATIM_USER_AGENT, get_db, get_localizable_value

RESTAURANT_STATUS_ACTIVE = 0
DISH_STATUS_ACTIVE = 0

# slug -> назва uk (той самий slug у всіх мовах адреси)
VENUE_TYPES = {
    "restorany": "Ресторани", "kafe": "Кафе", "bary": "Бари", "paby": "Паби",
    "kavyarni": "Кав'ярні", "pizzerii": "Піцерії", "sushi": "Суші-бари",
}

# назва закладу -> тип; перше збігання виграє (порядок важливий: «суші бар» - суші, «піца бар» - піцерія)
NAME_RULES = [
    ("sushi", r"суш[іи]|sushi|ролл|роли\b|\brolls?\b|якіторі|izakaya|ізакая"),
    ("pizzerii", r"піц|пицц|pizz"),
    ("paby", r"(?<!\w)паб(?!\w)|\bpub\b|пивн|пивовар|броварн|brewery|brew\b|beer|пив[оа]\b|крафт|craft"),
    ("kavyarni", r"кав.?ярн|кофейн|coffee|кофе(?!\w)|кава(?!\w)|espresso|еспресо|caffe|кофі"),
    ("bary", r"(?<!\w)бар(?!\w)|\bbar\b|lounge|лаунж|коктейл|cocktail|wine|винн|винотек|enoteca|speakeasy"),
    ("kafe", r"(?<!\w)кафе(?!\w)|\bcaf[eé]\b|\bkafe\b|\bcake\b|patisser|десерт|dessert|бістро|бистро|bistro|їдальн|столов|пекарн|bakery|кондитер|"
             r"бургерн|burger|шаурм|шаверм|чебуречн|пельменн|варенич|блинн|млинц|донер|kebab|кебаб|хот.?дог|"
             r"фаст.?фуд|fast.?food|street.?food|снек"),
]
# кухня (поле «Кухня») -> тип, якщо назва нічого не сказала
CUISINE_RULES = [
    ("sushi", r"суш|sushi"),
    ("pizzerii", r"піц|пицц|pizz"),
    ("kavyarni", r"кав.?ярн|кофейн|coffee|кава|кофе"),
    ("bary", r"(?<!\w)бар(?!\w)|\bbar\b|коктейл"),
    ("paby", r"(?<!\w)паб|\bpub\b|пивн"),
]
PIZZA = re.compile(r"піц|пицц|pizz", re.I)
SUSHI = re.compile(r"суш[іи]|sushi|ролл|\bрол\b|маки|урамак|нігір|нигир|гункан|каліфорні|калифорни|філадельф|филадельф", re.I)
COFFEE = re.compile(r"капуч|латте|еспрес|эспрес|американо|раф\b|флет|кава|кофе|coffee|какао|матча", re.I)


def menu_stats(db):
    """RestaurantId -> Counter(all, drinks, food, pizza, sushi, coffee) для активних страв."""
    cur = db.cursor(dictionary=True)
    cur.execute(
        """SELECT d.RestaurantId, d.Name,
                  GROUP_CONCAT(CONCAT_WS(' ', mc.Name, pc.Name) SEPARATOR ' ') AS Categories
           FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
           LEFT JOIN DishCategories dc ON dc.DishId = d.Id
           LEFT JOIN MenuCategories mc ON mc.Id = dc.MenuCategoryId
           LEFT JOIN MenuCategoryRelations rel ON rel.ChildId = mc.Id
           LEFT JOIN MenuCategories pc ON pc.Id = rel.ParentId
           WHERE d.Status = %s AND r.Status = %s
           GROUP BY d.Id""", (DISH_STATUS_ACTIVE, RESTAURANT_STATUS_ACTIVE))
    stats = defaultdict(Counter)
    for row in cur.fetchall():
        categories = re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), row["Categories"] or "")
        name = " ".join(get_localizable_value(row["Name"], lang) for lang in ("uk", "ru", "en"))
        st = stats[row["RestaurantId"]]
        st["all"] += 1
        if DRINK_CATEGORY.search(categories):
            st["drinks"] += 1
            if COFFEE.search(name) or COFFEE.search(categories):
                st["coffee"] += 1
            continue
        st["food"] += 1
        if PIZZA.search(name):
            st["pizza"] += 1
        if SUSHI.search(name):
            st["sushi"] += 1
    cur.close()
    return stats


def venue_type(name, cuisine, st):
    """(тип, причина)."""
    for vtype, rx in NAME_RULES:
        m = re.search(rx, name or "", re.I)
        if m:
            return vtype, f"назва: «{m.group(0)}»"
    for vtype, rx in CUISINE_RULES:
        m = re.search(rx, cuisine or "", re.I)
        if m:
            return vtype, f"кухня: «{m.group(0)}»"
    food = st["food"]
    if food >= 10 and st["pizza"] >= 0.4 * food:
        return "pizzerii", f"меню: піца {st['pizza']} з {food} страв"
    if food >= 10 and st["sushi"] >= 0.4 * food:
        return "sushi", f"меню: суші {st['sushi']} з {food} страв"
    if st["all"] >= 8 and st["coffee"] >= 0.3 * st["all"] and food < 15:
        return "kavyarni", f"меню: кава {st['coffee']} з {st['all']}, страв {food}"
    if st["all"] >= 15 and st["drinks"] >= 0.7 * st["all"]:
        return "bary", f"меню: напої {st['drinks']} з {st['all']}"
    return "restorany", "за замовчуванням" if st["all"] else "меню немає, за замовчуванням"


def ensure_table(db):
    cur = db.cursor()
    cur.execute("""CREATE TABLE IF NOT EXISTS RestaurantVenue (
        RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
        VenueType VARCHAR(32) NOT NULL,
        TypeReason VARCHAR(255) NULL,
        TypeManual TINYINT(1) NOT NULL DEFAULT 0,
        DistrictUk VARCHAR(255) NULL,
        DistrictRu VARCHAR(255) NULL,
        DistrictEn VARCHAR(255) NULL,
        DistrictSlug VARCHAR(128) NULL,
        DistrictManual TINYINT(1) NOT NULL DEFAULT 0,
        Lat VARCHAR(32) NULL,
        Lon VARCHAR(32) NULL,
        UpdatedAt DATETIME NOT NULL
    ) DEFAULT CHARSET=utf8mb4""")
    db.commit()
    cur.close()


DISTRICT_KEYS = ("city_district", "borough", "suburb", "district", "quarter")


def reverse_district(lat, lon, lang):
    """Назва району мовою lang (None - не знайдено)."""
    resp = requests.get("https://nominatim.openstreetmap.org/reverse",
                        params={"lat": lat, "lon": lon, "format": "jsonv2", "zoom": 14, "addressdetails": 1,
                                "accept-language": lang},
                        headers={"User-Agent": NOMINATIM_USER_AGENT}, timeout=20)
    time.sleep(1.1)  # правила Nominatim: не частіше 1 запиту на секунду
    if resp.status_code != 200:
        return None
    address = (resp.json() or {}).get("address") or {}
    for key in DISTRICT_KEYS:
        if address.get(key):
            return address[key]
    return None


def short_district(name):
    """«Оболонський район» -> «Оболонський»; «Shevchenkivskyi District» -> «Shevchenkivskyi»."""
    if not name:
        return None
    return re.sub(r"\s*(район|district|raion|r-n|р-н)\s*$", "", name.strip(), flags=re.I).strip() or name.strip()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-districts", action="store_true")
    ap.add_argument("--restaurant", help="slug або Id одного закладу")
    ap.add_argument("--districts-limit", type=int, default=0, help="не більше N закладів із новим районом за запуск (0 - усі)")
    args = ap.parse_args()

    db = get_db()
    ensure_table(db)
    cur = db.cursor(dictionary=True)
    where = "r.Status = %s"
    params = [RESTAURANT_STATUS_ACTIVE]
    if args.restaurant:
        where += " AND (r.Id = %s OR rs.Url = %s)"
        params += [args.restaurant, args.restaurant]
    cur.execute(f"""SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, rs.Url AS Slug, ri.Cuisine, ri.GpsLatitude, ri.GpsLongitude,
                           v.VenueType, v.TypeManual, v.DistrictManual, v.DistrictSlug, v.Lat, v.Lon
                    FROM Restaurants r
                    LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                    LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                    LEFT JOIN RestaurantVenue v ON v.RestaurantId = r.Id
                    WHERE {where}""", params)
    restaurants = cur.fetchall()
    stats = menu_stats(db)

    write = db.cursor()
    by_type = Counter()
    changed = 0
    for r in restaurants:
        cuisine = " ".join(get_localizable_value(r["Cuisine"], lang) for lang in ("uk", "ru", "en"))
        vtype, reason = venue_type(r["Name"], cuisine, stats.get(r["Id"], Counter()))
        if r["TypeManual"]:
            vtype, reason = r["VenueType"], "вручну (адмін)"
        by_type[vtype] += 1
        if args.restaurant or (args.dry_run and r["VenueType"] != vtype):
            print(f"  {r['Name']}: {VENUE_TYPES[vtype]} ({reason})" + (f", було {r['VenueType']}" if r["VenueType"] and r["VenueType"] != vtype else ""))
        if r["VenueType"] != vtype:
            changed += 1
        if not args.dry_run and not r["TypeManual"]:
            write.execute("""INSERT INTO RestaurantVenue (RestaurantId, VenueType, TypeReason, UpdatedAt)
                             VALUES (%s, %s, %s, UTC_TIMESTAMP())
                             ON DUPLICATE KEY UPDATE VenueType = VALUES(VenueType), TypeReason = VALUES(TypeReason),
                                                     UpdatedAt = IF(VenueType <> VALUES(VenueType), UTC_TIMESTAMP(), UpdatedAt)""",
                          (r["Id"], vtype, reason[:255]))
    if not args.dry_run:
        db.commit()
    print("Типи: " + ", ".join(f"{VENUE_TYPES[t]} {n}" for t, n in by_type.most_common()) + f"; змінилось {changed}")

    if args.dry_run or args.no_districts:
        db.close()
        return

    # райони: нові заклади або змінені координати, крім ручних
    todo = [r for r in restaurants if r["GpsLatitude"] and r["GpsLongitude"] and not r["DistrictManual"]
            and (not r["DistrictSlug"] or r["Lat"] != r["GpsLatitude"] or r["Lon"] != r["GpsLongitude"])]
    if args.districts_limit:
        todo = todo[:args.districts_limit]
    found = 0
    for i, r in enumerate(todo, 1):
        names = {}
        try:
            for lang in ("uk", "ru", "en"):
                names[lang] = short_district(reverse_district(r["GpsLatitude"], r["GpsLongitude"], lang))
        except requests.RequestException as e:
            print(f"  {r['Name']}: Nominatim - {e}")
            continue
        slug = slugify((names.get("en") or names.get("uk") or "").lower()) if (names.get("en") or names.get("uk")) else None
        slug = re.sub(r"[^a-z0-9-]", "", slug or "")[:128] or None
        write.execute("""UPDATE RestaurantVenue SET DistrictUk = %s, DistrictRu = %s, DistrictEn = %s, DistrictSlug = %s,
                                Lat = %s, Lon = %s, UpdatedAt = UTC_TIMESTAMP()
                         WHERE RestaurantId = %s AND DistrictManual = 0""",
                      (names.get("uk"), names.get("ru"), names.get("en"), slug, r["GpsLatitude"], r["GpsLongitude"], r["Id"]))
        db.commit()
        found += 1 if slug else 0
        if i % 50 == 0:
            print(f"  райони: {i}/{len(todo)}")
    print(f"Райони: перевірено {len(todo)}, знайдено {found}")
    db.close()


if __name__ == "__main__":
    main()

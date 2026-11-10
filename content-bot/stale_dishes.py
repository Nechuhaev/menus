"""Старі страви, яких уже немає в QR-меню закладу (Expirenza, ChoiceQR, RestoApp). Кодом, без AI.

Коли нове меню з QR-сервісу додали поверх старого, в закладі лишаються обидва: та сама страва двічі зі старою
й новою ціною, розділи-дублі. Скрипт читає меню з «Посилання на меню» закладу, зіставляє наші страви з ним
і ховає старі копії (Status = 2, «заблоковано»): страву з тією самою назвою, що й інша страва закладу,
у якої ціна як у QR-меню. Страви, яких немає в QR-меню, лише показуються (не ховаються).
Страви з оцінками гостей не ховаються - лише показуються для ручної перевірки.
Якщо QR-меню прочиталося погано (менше 10 страв) - заклад пропускається.
Кожна схована страва пишеться в StaleDishHidden - відкат: --rollback <адреса закладу>.

Запуск:  python3 stale_dishes.py --restaurant chachapuri          # показати, нічого не змінювати
         python3 stale_dishes.py --restaurant chachapuri --apply  # сховати
         python3 stale_dishes.py --rollback chachapuri            # повернути сховане
"""
import argparse

from menu_platforms import platform_menu
from collections import defaultdict

from sync_restaurants import get_db, get_localizable_value, menu_key, normalize_name

LOG_SQL = """CREATE TABLE IF NOT EXISTS StaleDishHidden (
    DishId VARCHAR(255) NOT NULL PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    HiddenAt DATETIME NOT NULL,
    KEY (RestaurantId)
) DEFAULT CHARSET=utf8mb4"""
MIN_PLATFORM_DISHES = 10


def restaurant(db, slug):
    cur = db.cursor(dictionary=True)
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name,
                          (SELECT ml.Url FROM RestaurantMenuLinks ml WHERE ml.RestaurantId = r.Id LIMIT 1) AS MenuUrl
                   FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id WHERE rs.Url = %s OR r.Id = %s LIMIT 1""",
                (slug, slug))
    row = cur.fetchone()
    cur.close()
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--restaurant", action="append", default=[], metavar="URL", help="адреса сторінки закладу або Id")
    parser.add_argument("--apply", action="store_true", help="сховати (без нього лише показати)")
    parser.add_argument("--rollback", metavar="URL", help="повернути страви, сховані цим скриптом у закладі")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_SQL)

    if args.rollback:
        r = restaurant(db, args.rollback)
        cur.execute("SELECT DishId FROM StaleDishHidden WHERE RestaurantId = %s", (r["Id"],))
        ids = [x["DishId"] for x in cur.fetchall()]
        for dish_id in ids:
            cur.execute("UPDATE Dishes SET Status = 0 WHERE Id = %s AND Status = 2", (dish_id,))
        cur.execute("DELETE FROM StaleDishHidden WHERE RestaurantId = %s", (r["Id"],))
        cur.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (r["Id"],))
        db.commit()
        print(f"{r['Name']}: повернуто страв {len(ids)}")
        return

    for slug in args.restaurant:
        r = restaurant(db, slug)
        if not r or not r["MenuUrl"]:
            print(f"{slug}: немає закладу або «Посилання на меню»")
            continue
        site = platform_menu(r["MenuUrl"].strip())
        if not site or len(site) < MIN_PLATFORM_DISHES:
            print(f"{r['Name']}: QR-меню прочиталося погано ({len(site or [])} страв) - пропущено")
            continue
        cur.execute("""SELECT CONCAT(Id, '') AS Id, Name, Price, IFNULL(RatingsCount, 0) AS RatingsCount
                       FROM Dishes WHERE RestaurantId = %s AND Status = 0""", (r["Id"],))
        ours = cur.fetchall()
        # Точне правило: страва ховається, лише коли в закладі є двійник з тією самою назвою, у якого ціна
        # як у QR-меню (стара й нова копія однієї страви). Страви, яких просто немає в QR-меню, не ховаються -
        # схоже зіставлення плутає їх з порціями («патара»), тож вони лише показуються.
        noise = set(normalize_name(r["Name"]).split())
        site_prices = defaultdict(set)
        for d in site:
            site_prices[menu_key(d["name"], noise)].add(round(float(d["price"] or 0), 2))
        groups = defaultdict(list)
        for d in ours:
            key = menu_key(get_localizable_value(d["Name"], "uk") or get_localizable_value(d["Name"], "ru"), noise)
            if key:
                groups[key].append(d)
        stale, rated, missing = [], [], []
        for key, dishes in groups.items():
            prices = site_prices.get(key)
            if not prices:
                missing += dishes
                continue
            current = [d for d in dishes if round(float(d["Price"] or 0), 2) in prices]
            if not current:
                continue
            for d in dishes:
                if d not in current:
                    (rated if d["RatingsCount"] else stale).append(d)
        hide = stale
        print(f"\n{r['Name']} <- {r['MenuUrl']}\n  у QR-меню {len(site)}, у нас {len(ours)}; "
              f"старих копій сховати {len(hide)}, з оцінками (лишаються) {len(rated)}, немає в QR-меню (не чіпаємо) {len(missing)}")
        for d in hide:
            print(f"   - {get_localizable_value(d['Name'], 'uk') or get_localizable_value(d['Name'], 'ru')} · {float(d['Price'] or 0):g} ₴")
        for d in rated:
            print(f"   ? {get_localizable_value(d['Name'], 'uk') or get_localizable_value(d['Name'], 'ru')} · {float(d['Price'] or 0):g} ₴ "
                  f"· оцінок {d['RatingsCount']} (перевірити вручну)")
        for d in missing:
            print(f"   ~ немає в QR-меню: {get_localizable_value(d['Name'], 'uk') or get_localizable_value(d['Name'], 'ru')} · {float(d['Price'] or 0):g} ₴")
        if args.apply and hide:
            for d in hide:
                cur.execute("UPDATE Dishes SET Status = 2 WHERE Id = %s", (d["Id"],))
                cur.execute("INSERT IGNORE INTO StaleDishHidden (DishId, RestaurantId, HiddenAt) VALUES (%s, %s, NOW())",
                            (d["Id"], r["Id"]))
            cur.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (r["Id"],))
            db.commit()
            print(f"  сховано {len(hide)} (відкат: --rollback {slug})")
    if not args.apply and not args.rollback:
        print("\nнічого не змінено - для запису --apply")
    db.close()


if __name__ == "__main__":
    main()

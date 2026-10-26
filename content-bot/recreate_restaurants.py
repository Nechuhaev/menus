"""Заводить заново ресторани, видалені з бази, під їхніми старими адресами.

Від видаленого ресторану лишився SEO-запис (RestaurantSeo: адреса сторінки, заголовок) з його Id. Скрипт створює
ресторан з тим самим Id - стара адреса з Google одразу знову відкриває сторінку, без нових URL і переадресацій.
Дані - recreate_restaurants.csv (перевірені вручну: заклад працює, місто, адреса, сайт, посилання на меню
Expirenza/ChoiceQR). Меню наповнить бот: з посилання на меню - кодом, інакше з сайту. Поки страв мало,
thin_pages.py закриває сторінку від індексації (noindex), тож порожні сторінки в Google не потрапляють.

Запуск:  python3 recreate_restaurants.py --dry-run   # усе вставляє в транзакції й відкочує: перевірка без змін
         python3 recreate_restaurants.py             # записати
"""
import argparse
import csv
import json
import os
import uuid
from datetime import datetime

from sync_restaurants import MENU_LINKS_TABLE_SQL, get_db, get_localizable_value

DATA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "recreate_restaurants.csv")
DEFAULT_OPEN_HOURS = json.dumps({"perDay": False, "data": [{"from": "11:00", "to": "23:00", "active": True}] * 7})
EMPTY = json.dumps({"Values": []})


def localizable(value):
    return json.dumps({"Values": [{"Locale": "uk", "Value": value}]}, ensure_ascii=False) if value else EMPTY


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    rows = list(csv.DictReader(open(DATA, encoding="utf-8")))
    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(MENU_LINKS_TABLE_SQL)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {(get_localizable_value(r["Name"], "en") or "").lower(): r["Id"] for r in cur.fetchall()}
    now = datetime.now()
    done, skipped = 0, []
    for row in rows:
        cur.execute("""SELECT CONCAT(rs.ItemId, '') AS ItemId, r.Id AS Existing FROM RestaurantSeo rs
                       LEFT JOIN Restaurants r ON r.Id = rs.ItemId WHERE rs.Url = %s""", (row["slug"],))
        seo = cur.fetchone()
        city = cities.get(row["city"])
        if not seo or seo["Existing"] or not city:
            reason = "адреси немає в базі" if not seo else "ресторан уже є" if seo["Existing"] else f"місто {row['city']} не знайдено"
            skipped.append((row["slug"], reason))
            continue
        rid = seo["ItemId"]
        cur.execute("""INSERT INTO Restaurants (Id, Name, Email, Phone, Address, City, Status, UpdateDate, CreateDate,
                                                Moderated, IsModeratable)
                       VALUES (%s, %s, '', '', %s, %s, 0, %s, %s, 1, 0)""",
                    (rid, row["name"], row["address"], city, now, now))
        # сиріт RestaurantInfo від видаленого ресторану не лишаємо - одна картка на ресторан
        cur.execute("DELETE FROM RestaurantInfo WHERE RestaurantId = %s", (rid,))
        cur.execute("""INSERT INTO RestaurantInfo (Id, RestaurantId, Photos, Website, OpenHours, UserAllowedAddMenu,
                                                   UserAllowedOpenMenu, UserAllowedAddDishes, IsFeatured, ShowOnIndex,
                                                   Currency, ShortInfo, FullInfo, Cuisine, DisplayAddress)
                       VALUES (%s, %s, '[]', %s, %s, 1, 1, 1, 0, 0, '₴', %s, %s, %s, %s)""",
                    (str(uuid.uuid4()), rid, row["website"], DEFAULT_OPEN_HOURS, EMPTY, EMPTY, EMPTY,
                     localizable(row["address"])))
        if row["menu_url"]:
            cur.execute("""INSERT INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (%s, %s, %s)
                           ON DUPLICATE KEY UPDATE Url = VALUES(Url), UpdatedAt = VALUES(UpdatedAt)""",
                        (rid, row["menu_url"], now))
        done += 1
        print(f"  {row['slug']}: {row['name']} ({row['city']}){' + меню ' + row['menu_url'] if row['menu_url'] else ''}")

    for slug, reason in skipped:
        print(f"  пропущено {slug}: {reason}")
    if args.dry_run:
        db.rollback()
        print(f"\nперевірка: створилось би {done}, пропущено {len(skipped)}; нічого не записано (відкат)")
    else:
        db.commit()
        print(f"\nстворено: {done}, пропущено {len(skipped)}. Перегенеруй sitemap; меню наповнить бот наступної ночі.")
    db.close()


if __name__ == "__main__":
    main()

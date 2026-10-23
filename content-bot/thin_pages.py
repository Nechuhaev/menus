"""Підрахунок «тонких» сторінок для noindex (лише читає базу, нічого не пише).

Сторінка страви тонка - немає ні опису, ні фото (лише назва й ціна - майже дубль рядка меню ресторану).
Сторінка ресторану тонка - менше MIN_DISHES активних страв і немає опису закладу (ShortInfo/FullInfo).
Правило оновлюється само: як тільки бот додасть опис чи фото, сторінка знову індексується.

Запуск:  python3 thin_pages.py              # цифри по містах і приклади адрес
         python3 thin_pages.py --min 3      # інший поріг страв для ресторану
"""
import argparse
import json
from collections import Counter

from sync_restaurants import get_db, get_localizable_value

MIN_DISHES = 5
EXAMPLES = 10


def has_text(value):
    """Локалізоване поле ({"Values": [...]}) або звичайний рядок - чи є в ньому текст."""
    if not value or not value.strip():
        return False
    try:
        parsed = json.loads(value)
    except ValueError:
        return True
    if isinstance(parsed, dict):
        return any((v.get("Value") or "").strip() for v in parsed.get("Values") or [])
    return bool(parsed)


def has_photo(photo_link):
    if not photo_link or not photo_link.strip():
        return False
    try:
        photos = json.loads(photo_link)
    except ValueError:
        return True
    return any(p and str(p).strip() for p in photos) if isinstance(photos, list) else bool(photos)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=int, default=MIN_DISHES, help="мінімум активних страв для ресторану")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") or row["Id"] for row in cur.fetchall()}

    cur.execute("""SELECT d.Description, d.PhotoLink, r.City, ds.Url
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   LEFT JOIN DishSeo ds ON ds.ItemId = d.Id
                   WHERE d.Status = 0 AND r.Status = 0""")
    dishes, thin_dishes, dish_examples = Counter(), Counter(), []
    for row in cur.fetchall():
        dishes[row["City"]] += 1
        if not has_text(row["Description"]) and not has_photo(row["PhotoLink"]):
            thin_dishes[row["City"]] += 1
            if len(dish_examples) < EXAMPLES and row["Url"]:
                dish_examples.append(row["Url"])

    cur.execute("""SELECT r.Name, r.City, ri.ShortInfo, ri.FullInfo, rs.Url,
                          (SELECT COUNT(*) FROM Dishes d WHERE d.RestaurantId = r.Id AND d.Status = 0) AS DishCount
                   FROM Restaurants r
                   LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   WHERE r.Status = 0""")
    restaurants, thin_restaurants, restaurant_examples = Counter(), Counter(), []
    for row in cur.fetchall():
        restaurants[row["City"]] += 1
        if row["DishCount"] < args.min and not has_text(row["ShortInfo"]) and not has_text(row["FullInfo"]):
            thin_restaurants[row["City"]] += 1
            if len(restaurant_examples) < EXAMPLES and row["Url"]:
                restaurant_examples.append(f"{row['Url']}  ({row['Name']}, страв: {row['DishCount']})")
    db.close()

    def pct(part, whole):
        return f"{100 * part / whole:.0f}%" if whole else "-"

    print(f"{'Місто':<20} {'страви':>8} {'тонкі':>8} {'':>5}   {'ресторани':>9} {'тонкі':>6} {'':>5}")
    for city in sorted(set(dishes) | set(restaurants), key=lambda c: -dishes[c]):
        print(f"{str(cities.get(city, city))[:20]:<20} {dishes[city]:>8} {thin_dishes[city]:>8} {pct(thin_dishes[city], dishes[city]):>5}"
              f"   {restaurants[city]:>9} {thin_restaurants[city]:>6} {pct(thin_restaurants[city], restaurants[city]):>5}")
    total_d, total_td = sum(dishes.values()), sum(thin_dishes.values())
    total_r, total_tr = sum(restaurants.values()), sum(thin_restaurants.values())
    print(f"{'УСЬОГО':<20} {total_d:>8} {total_td:>8} {pct(total_td, total_d):>5}   {total_r:>9} {total_tr:>6} {pct(total_tr, total_r):>5}")
    print("\nПриклади тонких сторінок страв (адреса з DishSeo):")
    for url in dish_examples:
        print(f"  {url}")
    print(f"\nПриклади тонких сторінок ресторанів (< {args.min} страв і без опису):")
    for line in restaurant_examples:
        print(f"  {line}")


if __name__ == "__main__":
    main()

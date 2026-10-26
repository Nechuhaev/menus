"""Підрахунок «тонких» сторінок для noindex (лише читає базу, нічого не пише).

Сторінка страви тонка - немає ні опису, ні фото (лише назва й ціна - майже дубль рядка меню ресторану).
Сторінка ресторану тонка - менше MIN_DISHES активних страв і немає опису закладу (ShortInfo/FullInfo).
Правило оновлюється само: як тільки бот додасть опис чи фото, сторінка знову індексується.

Тонкі страви діляться на напої (категорія страви - бар, кава, вино тощо, як у dish_city_candidates)
та їжу: напої закриваються першими, їжу бот поступово наповнює описами й фото.

--gsc Pages.csv - експорт Search Console («Сторінки»: адреса, кліки, покази): скільки кліків і
показів дають тонкі сторінки. Сторінка страви - /{мова}/{місто}/restaurant/{ресторан}/{страва}.

Запуск:  python3 thin_pages.py                      # цифри по містах і приклади адрес
         python3 thin_pages.py --min 3              # інший поріг страв для ресторану
         python3 thin_pages.py --gsc Pages.csv      # + трафік тонких сторінок з Search Console
"""
import argparse
import csv
import json
import re
from collections import Counter, defaultdict
from urllib.parse import unquote, urlparse

from dish_city_candidates import DRINK_CATEGORY
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


def page_key(url):
    """(ресторан, страва) з адреси .../restaurant/{ресторан}[/{страва}]; None - не сторінка ресторану."""
    parts = [unquote(p).lower() for p in urlparse(url).path.split("/") if p]
    if "restaurant" not in parts:
        return None
    i = parts.index("restaurant")
    if i + 1 >= len(parts):
        return None
    return parts[i + 1], parts[i + 2] if i + 2 < len(parts) else None


def number(value):
    digits = re.sub(r"[^\d]", "", value or "")
    return int(digits) if digits else 0


def gsc_report(path, page_kind):
    """Кліки й покази з експорту Search Console за видами сторінок; адреси групуються без мови й міста."""
    clicks, impressions, pages = Counter(), Counter(), Counter()
    top = []
    with open(path, encoding="utf-8-sig") as f:
        rows = list(csv.reader(f))
    for row in rows[1:]:
        if len(row) < 3:
            continue
        key = page_key(row[0])
        if key is None:
            kind = "інші сторінки"
        else:
            kind = page_kind.get(key, "ресторан" if key[1] is None else "страва не знайдена в базі")
        c, i = number(row[1]), number(row[2])
        clicks[kind] += c
        impressions[kind] += i
        pages[kind] += 1
        if kind.startswith("тонк") and (c or i):
            top.append((c, i, row[0]))
    print(f"\nSearch Console ({len(rows) - 1} адрес у файлі):")
    print(f"{'вид сторінки':<28} {'адрес':>7} {'кліки':>8} {'покази':>10}")
    for kind in sorted(pages, key=lambda k: -clicks[k]):
        print(f"{kind:<28} {pages[kind]:>7} {clicks[kind]:>8} {impressions[kind]:>10}")
    print("\nТонкі сторінки з найбільшим трафіком (кліки, покази):")
    for c, i, url in sorted(top, reverse=True)[:15]:
        print(f"  {c:>5} {i:>7}  {url}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=int, default=MIN_DISHES, help="мінімум активних страв для ресторану")
    parser.add_argument("--gsc", metavar="CSV", help="експорт Search Console «Сторінки» (адреса, кліки, покази)")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") or row["Id"] for row in cur.fetchall()}

    cur.execute("""SELECT d.Description, d.PhotoLink, r.City, ds.Url, rs.Url AS RestaurantUrl,
                          GROUP_CONCAT(CONCAT_WS(' ', mc.Name, pc.Name) SEPARATOR ' ') AS Categories
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   LEFT JOIN DishSeo ds ON ds.ItemId = d.Id
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   LEFT JOIN DishCategories dc ON dc.DishId = d.Id
                   LEFT JOIN MenuCategories mc ON mc.Id = dc.MenuCategoryId
                   LEFT JOIN MenuCategoryRelations rel ON rel.ChildId = mc.Id
                   LEFT JOIN MenuCategories pc ON pc.Id = rel.ParentId
                   WHERE d.Status = 0 AND r.Status = 0
                   GROUP BY d.Id""")
    dishes, thin_dishes, thin_drinks, dish_examples = Counter(), Counter(), Counter(), []
    page_kind = {}  # (ресторан, страва) -> вид сторінки для звіту Search Console
    for row in cur.fetchall():
        dishes[row["City"]] += 1
        kind = "страва з описом/фото"
        if not has_text(row["Description"]) and not has_photo(row["PhotoLink"]):
            thin_dishes[row["City"]] += 1
            # назви категорій - JSON з мовами, для пошуку слів досить сирого тексту
            categories = re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), row["Categories"] or "")
            if DRINK_CATEGORY.search(categories):
                thin_drinks[row["City"]] += 1
                kind = "тонка: напій"
            else:
                kind = "тонка: їжа"
            if len(dish_examples) < EXAMPLES and row["Url"]:
                dish_examples.append(row["Url"])
        if row["Url"] and row["RestaurantUrl"]:
            page_kind[(row["RestaurantUrl"].lower(), row["Url"].lower())] = kind

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
            if row["Url"]:
                page_kind[(row["Url"].lower(), None)] = "тонкий ресторан"
            if len(restaurant_examples) < EXAMPLES and row["Url"]:
                restaurant_examples.append(f"{row['Url']}  ({row['Name']}, страв: {row['DishCount']})")
    db.close()

    def pct(part, whole):
        return f"{100 * part / whole:.0f}%" if whole else "-"

    print(f"{'Місто':<20} {'страви':>8} {'тонкі':>8} {'':>5} {'з них напої':>11}   {'ресторани':>9} {'тонкі':>6} {'':>5}")
    for city in sorted(set(dishes) | set(restaurants), key=lambda c: -dishes[c]):
        print(f"{str(cities.get(city, city))[:20]:<20} {dishes[city]:>8} {thin_dishes[city]:>8} {pct(thin_dishes[city], dishes[city]):>5}"
              f" {thin_drinks[city]:>11}   {restaurants[city]:>9} {thin_restaurants[city]:>6} {pct(thin_restaurants[city], restaurants[city]):>5}")
    total_d, total_td, total_dr = sum(dishes.values()), sum(thin_dishes.values()), sum(thin_drinks.values())
    total_r, total_tr = sum(restaurants.values()), sum(thin_restaurants.values())
    print(f"{'УСЬОГО':<20} {total_d:>8} {total_td:>8} {pct(total_td, total_d):>5} {total_dr:>11}   {total_r:>9} {total_tr:>6} {pct(total_tr, total_r):>5}")
    print(f"\nТонкі напої: {total_dr} ({pct(total_dr, total_d)} усіх сторінок страв); тонка їжа: {total_td - total_dr}")
    if args.gsc:
        gsc_report(args.gsc, page_kind)
    print("\nПриклади тонких сторінок страв (адреса з DishSeo):")
    for url in dish_examples:
        print(f"  {url}")
    print(f"\nПриклади тонких сторінок ресторанів (< {args.min} страв і без опису):")
    for line in restaurant_examples:
        print(f"  {line}")


if __name__ == "__main__":
    main()

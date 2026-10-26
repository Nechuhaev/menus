"""«Тонкі» сторінки: підрахунок і список для noindex (таблиця SeoNoindex, її читає сайт).

Сторінка страви тонка - немає ні опису, ні фото (лише назва й ціна - майже дубль рядка меню ресторану).
Сторінка ресторану тонка - менше MIN_DISHES активних страв і немає опису закладу (ShortInfo/FullInfo).
Тонкі страви діляться на напої (категорія страви - бар, кава, вино тощо, як у dish_city_candidates) та їжу.

Закривається від індексації (noindex, follow і без sitemap): тонкі ресторани й тонкі напої, крім
сторінок, що мають кліки в Search Console (таблиця SeoKeepIndexed, оновлюється з --gsc --save).
Тонка їжа лишається в індексі: за Search Console вона дає майже половину кліків сторінок страв.
Правило оновлюється само (cron щоночі з --save): як тільки в напою з'явиться опис чи фото, сторінка
повертається в індекс.

--gsc Pages.csv - експорт Search Console («Сторінки»: адреса, кліки, покази): кліки й покази за видами
сторінок і адреси з кліками, яких немає серед активних страв (з кодом відповіді сайту).
Сторінка страви - /{мова}/{місто}/restaurant/{ресторан}/{страва}.

Запуск:  python3 thin_pages.py                          # цифри по містах і приклади адрес
         python3 thin_pages.py --gsc Pages.csv          # + трафік з Search Console
         python3 thin_pages.py --gsc Pages.csv --save   # + оновити список сторінок з кліками і SeoNoindex
         python3 thin_pages.py --save                   # оновити SeoNoindex (cron щоночі)
"""
import argparse
import csv
import json
import re
import time
from collections import Counter
from urllib.parse import unquote, urlparse

import requests

from dish_city_candidates import DRINK_CATEGORY
from sync_restaurants import get_db, get_localizable_value

MIN_DISHES = 5
EXAMPLES = 10
MISSING_SHOWN = 20

NOINDEX_TABLE_SQL = """CREATE TABLE IF NOT EXISTS SeoNoindex (
    ItemId VARCHAR(255) NOT NULL PRIMARY KEY,
    Kind VARCHAR(20) NOT NULL,
    UpdatedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""
KEEP_TABLE_SQL = """CREATE TABLE IF NOT EXISTS SeoKeepIndexed (
    RestaurantUrl VARCHAR(255) NOT NULL,
    DishUrl VARCHAR(255) NOT NULL,
    Clicks INT NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    PRIMARY KEY (RestaurantUrl, DishUrl)
) DEFAULT CHARSET=utf8mb4"""


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


def read_gsc(path):
    """[(адреса, кліки, покази)] з експорту Search Console."""
    with open(path, encoding="utf-8-sig") as f:
        rows = list(csv.reader(f))
    return [(row[0], number(row[1]), number(row[2])) for row in rows[1:] if len(row) >= 3]


def status_of(url):
    """Код відповіді сайту й адреса редиректу (без переходу за редиректом)."""
    try:
        resp = requests.get(url, timeout=15, allow_redirects=False,
                            headers={"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)"})
        return resp.status_code, resp.headers.get("Location") or ""
    except requests.RequestException as e:
        return "помилка", str(e)[:60]


def gsc_report(gsc_rows, page_kind, all_dishes):
    """Кліки й покази за видами сторінок; адреси групуються без мови й міста."""
    clicks, impressions, pages = Counter(), Counter(), Counter()
    top, missing = [], []
    for url, c, i in gsc_rows:
        key = page_key(url)
        if key is None:
            kind = "інші сторінки"
        else:
            kind = page_kind.get(key, "ресторан" if key[1] is None else "страва не знайдена в базі")
        clicks[kind] += c
        impressions[kind] += i
        pages[kind] += 1
        if kind.startswith("тонк") and (c or i):
            top.append((c, i, url))
        if kind == "страва не знайдена в базі" and c:
            missing.append((c, i, url, all_dishes.get(key, "немає в базі")))
    print(f"\nSearch Console ({len(gsc_rows)} адрес у файлі):")
    print(f"{'вид сторінки':<28} {'адрес':>7} {'кліки':>8} {'покази':>10}")
    for kind in sorted(pages, key=lambda k: -clicks[k]):
        print(f"{kind:<28} {pages[kind]:>7} {clicks[kind]:>8} {impressions[kind]:>10}")
    print("\nТонкі сторінки з найбільшим трафіком (кліки, покази):")
    for c, i, url in sorted(top, reverse=True)[:15]:
        print(f"  {c:>5} {i:>7}  {url}")

    print(f"\nАдреси з кліками, яких немає серед активних страв ({len(missing)}; перші {MISSING_SHOWN} - з кодом відповіді):")
    print(f"  {'кліки':>5} {'код':>6}  {'в базі':<24} адреса -> редирект")
    for c, i, url, state in sorted(missing, reverse=True)[:MISSING_SHOWN]:
        code, location = status_of(url)
        print(f"  {c:>5} {code:>6}  {state:<24} {url}{' -> ' + location if location else ''}")
        time.sleep(0.5)


def save(db, noindex, gsc_rows):
    cur = db.cursor()
    cur.execute(NOINDEX_TABLE_SQL)
    cur.execute(KEEP_TABLE_SQL)
    if gsc_rows is not None:
        # сторінки, що вже дають кліки, лишаються в індексі, навіть тонкі
        keep = Counter()
        for url, c, _ in gsc_rows:
            key = page_key(url)
            if key and c:
                keep[(key[0], key[1] or "")] += c
        cur.execute("DELETE FROM SeoKeepIndexed")
        cur.executemany("INSERT INTO SeoKeepIndexed (RestaurantUrl, DishUrl, Clicks, UpdatedAt) VALUES (%s, %s, %s, NOW())",
                        [(r, d, c) for (r, d), c in keep.items()])
    cur.execute("DELETE FROM SeoNoindex")
    cur.executemany("INSERT INTO SeoNoindex (ItemId, Kind, UpdatedAt) VALUES (%s, %s, NOW())", noindex)
    db.commit()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=int, default=MIN_DISHES, help="мінімум активних страв для ресторану")
    parser.add_argument("--gsc", metavar="CSV", help="експорт Search Console «Сторінки» (адреса, кліки, покази)")
    parser.add_argument("--save", action="store_true", help="записати список noindex (і сторінки з кліками з --gsc)")
    args = parser.parse_args()

    gsc_rows = read_gsc(args.gsc) if args.gsc else None
    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(KEEP_TABLE_SQL)
    if gsc_rows is not None:
        keep = {(k[0], k[1] or "") for k in (page_key(url) for url, c, _ in gsc_rows if c) if k}
    else:
        cur.execute("SELECT RestaurantUrl, DishUrl FROM SeoKeepIndexed")
        keep = {(row["RestaurantUrl"], row["DishUrl"]) for row in cur.fetchall()}

    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") or row["Id"] for row in cur.fetchall()}

    cur.execute("""SELECT CONCAT(d.Id, '') AS Id, d.Description, d.PhotoLink, r.City, ds.Url, rs.Url AS RestaurantUrl,
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
    noindex, kept = [], 0
    for row in cur.fetchall():
        dishes[row["City"]] += 1
        kind = "страва з описом/фото"
        key = ((row["RestaurantUrl"] or "").lower(), (row["Url"] or "").lower())
        if not has_text(row["Description"]) and not has_photo(row["PhotoLink"]):
            thin_dishes[row["City"]] += 1
            # назви категорій - JSON з мовами, для пошуку слів досить сирого тексту
            categories = re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), row["Categories"] or "")
            if DRINK_CATEGORY.search(categories):
                thin_drinks[row["City"]] += 1
                kind = "тонка: напій"
                if key in keep:
                    kept += 1
                else:
                    noindex.append((row["Id"], "dish"))
            else:
                kind = "тонка: їжа"
            if len(dish_examples) < EXAMPLES and row["Url"]:
                dish_examples.append(row["Url"])
        if row["Url"] and row["RestaurantUrl"]:
            page_kind[key] = kind

    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, ri.ShortInfo, ri.FullInfo, rs.Url,
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
            noindex.append((row["Id"], "restaurant"))
            if row["Url"]:
                page_kind[(row["Url"].lower(), None)] = "тонкий ресторан"
            if len(restaurant_examples) < EXAMPLES and row["Url"]:
                restaurant_examples.append(f"{row['Url']}  ({row['Name']}, страв: {row['DishCount']})")

    all_dishes = {}
    if gsc_rows is not None:
        # усі страви (і приховані) - чому адреси з кліками немає серед активних
        cur.execute("""SELECT LOWER(rs.Url) AS RestaurantUrl, LOWER(ds.Url) AS Url, d.Status, r.Status AS RestaurantStatus
                       FROM DishSeo ds JOIN Dishes d ON d.Id = ds.ItemId JOIN Restaurants r ON r.Id = d.RestaurantId
                       JOIN RestaurantSeo rs ON rs.ItemId = r.Id""")
        for row in cur.fetchall():
            all_dishes[(row["RestaurantUrl"], row["Url"])] = (
                "ресторан неактивний" if row["RestaurantStatus"] != 0 else f"страва зі статусом {row['Status']}")

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
    print(f"noindex: сторінок страв {sum(1 for _, k in noindex if k == 'dish')} (тонкі напої; лишено в індексі через кліки: {kept}), "
          f"ресторанів {sum(1 for _, k in noindex if k == 'restaurant')}")
    print("\nПриклади тонких сторінок страв (адреса з DishSeo):")
    for url in dish_examples:
        print(f"  {url}")
    print(f"\nПриклади тонких сторінок ресторанів (< {args.min} страв і без опису):")
    for line in restaurant_examples:
        print(f"  {line}")

    if gsc_rows is not None:
        gsc_report(gsc_rows, page_kind, all_dishes)
    if args.save:
        save(db, noindex, gsc_rows)
        print(f"\nЗаписано в SeoNoindex: {len(noindex)}" + (" ; SeoKeepIndexed оновлено з Search Console" if gsc_rows is not None else ""))
    db.close()


if __name__ == "__main__":
    main()

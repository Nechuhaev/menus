"""Кандидати для сторінок «страва + місто» (лише читає базу, нічого не пише).

Ключ - 1-2 слова поспіль з української назви страви (без розмірів, дужок і службових слів).
Для кожного міста рахується, у скількох різних закладах є страва з таким ключем; мережа з
кількома філіями рахується як один заклад (за назвою ресторану), щоб одна мережа не набивала поріг.

Запуск:  python3 dish_city_candidates.py              # усі міста, поріг 5 закладів
         python3 dish_city_candidates.py --min 8 --top 40
"""
import argparse
import statistics
from collections import defaultdict

from sync_restaurants import get_db, get_localizable_value, match_key, normalize_name

DISH_STATUS_ACTIVE = 0
RESTAURANT_STATUS_ACTIVE = 0
# службові слова й загальні слова меню, з яких сторінка не вийде
SKIP_WORDS = {
    "з", "із", "зі", "та", "і", "й", "в", "у", "на", "по", "для", "до", "від", "під", "або",
    "порція", "порції", "шт", "гр", "мл", "см", "новинка", "хіт", "фірмовий", "фірмова", "фірмові",
    "домашній", "домашня", "класичний", "класична", "мікс", "сет", "асорті",
}


def keys_for(name):
    """Ключі назви: окремі слова і пари сусідніх слів без службових між ними."""
    words = match_key(name).split()
    keys = set()
    for i, word in enumerate(words):
        if word in SKIP_WORDS or word.isdigit() or len(word) < 3:
            continue
        keys.add(word)
        if i + 1 < len(words):
            nxt = words[i + 1]
            if nxt not in SKIP_WORDS and not nxt.isdigit() and len(nxt) >= 3:
                keys.add(word + " " + nxt)
    return keys


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=int, default=5, help="мінімум різних закладів у місті")
    parser.add_argument("--top", type=int, default=60, help="скільки ключів показати на місто")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") or row["Id"] for row in cur.fetchall()}

    cur.execute(
        """SELECT d.Name, d.Price, r.Id AS RestaurantId, r.Name AS RestaurantName, r.City
           FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
           WHERE d.Status = %s AND r.Status = %s""",
        (DISH_STATUS_ACTIVE, RESTAURANT_STATUS_ACTIVE))

    # (місто, ключ) -> заклади (бренди), ресторани, ціни
    brands = defaultdict(set)
    restaurants = defaultdict(set)
    prices = defaultdict(list)
    for row in cur.fetchall():
        name = get_localizable_value(row["Name"], "uk") or get_localizable_value(row["Name"], "ru")
        brand = normalize_name(row["RestaurantName"])
        for key in keys_for(name):
            k = (row["City"], key)
            brands[k].add(brand)
            restaurants[k].add(row["RestaurantId"])
            if row["Price"] and row["Price"] > 0:
                prices[k].append(float(row["Price"]))

    by_city = defaultdict(list)
    for (city, key), b in brands.items():
        if len(b) >= args.min:
            p = prices[(city, key)]
            by_city[city].append((len(b), len(restaurants[(city, key)]), key, statistics.median(p) if p else 0))

    total = 0
    for city, rows in sorted(by_city.items(), key=lambda x: -len(x[1])):
        rows.sort(reverse=True)
        total += len(rows)
        print(f"\n=== {cities.get(city, city)}: {len(rows)} ключів із >= {args.min} закладами")
        print(f"{'закл.':>5} {'рест.':>5} {'медіана':>8}  ключ")
        for n_brands, n_rest, key, median in rows[:args.top]:
            print(f"{n_brands:>5} {n_rest:>5} {median:>8.0f}  {key}")
    print(f"\nУсього кандидатів: {total}")


if __name__ == "__main__":
    main()

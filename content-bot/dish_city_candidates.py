"""Кандидати для сторінок «страва + місто» (лише читає базу, нічого не пише).

Ключ - перше слово або перші два слова української назви страви (без розмірів, дужок і службових слів).
Для кожного міста рахується, у скількох різних закладах є страва з таким ключем; мережа з
кількома філіями рахується як один заклад (за назвою ресторану), щоб одна мережа не набивала поріг.

Запуск:  python3 dish_city_candidates.py              # усі міста, поріг 5 закладів
         python3 dish_city_candidates.py --min 8 --top 40
"""
import argparse
import re
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


# Категорії: окремим словом сторінка надто загальна, у парі - нормальна («салат цезар», «крем суп»)
GENERIC_WORDS = {
    "салат", "суп", "чай", "кава", "соус", "сік", "вода", "пиво", "вино", "десерт", "напій", "філе",
    "крем", "коктейль", "лимонад", "фреш", "морс", "сет", "закуска", "гарнір", "хліб", "сироп",
}


# Напої відсікаються за назвою категорії страви або її батьківської категорії (будь-якою мовою)
DRINK_CATEGORY = re.compile(
    r"(?<!\w)(?:бар(?!бек)|напо(?!лео)|напи|алко|вин(?!егр)|пив|коктей|кав(?!ун)|коф|чай|сік|соки|соков|"
    r"лимонад|фреш|смузі|віск|виск|горілк|водк|коньяк|лікер|ликер|ром\b|джин|текіл|текил|шампан|ігрист|игрист|"
    r"drink|bar\b|wine|beer|coffee|tea\b|cocktail|whisk|vodka|juice|lemonade|spirits)", re.I)
LATIN = re.compile(r"[a-z]")
# іменники з «прикметниковим» закінченням
NOT_ADJECTIVES = {"пельмені"}
# Одне слово-прикметник без іменника - не страва («шоколадний», «шоколадне», «овочева», «курячі»);
# пари з ним («шоколадний фондан») лишаються. -ина - іменники (свинина, телятина), не відкидаються
ADJECTIVE = re.compile(r"(?<!и)(?:ний|ній|на|не|ні)$|(?:ий|ій|ька|ська|ське|ські|ева|ова|еве|ове|еві|ові|ячий|яча|яче|ячі)$")


def keys_for(name):
    """Ключі - початок назви, де стоїть сама страва («Цезар з куркою» -> «цезар», «цезар куркою»
    не береться): перше слово (крім загальних категорій) і перші два слова. Інгредієнти після «з»
    у відмінкових формах («лососем», «сиром») сюди не потрапляють."""
    words = [w for w in match_key(name).split() if not w.isdigit()]
    # латиниця в українській назві - бренд без перекладу (Jack Daniel's, Aperol), не страва
    if any(LATIN.search(w) for w in words[:2]):
        return set()
    if not words or words[0] in SKIP_WORDS or len(words[0]) < 3:
        return set()
    keys = set()
    if words[0] not in GENERIC_WORDS:
        keys.add(words[0])
    if len(words) > 1 and words[1] not in SKIP_WORDS and len(words[1]) >= 3:
        keys.add(words[0] + " " + words[1])
    return keys


class KeyStats:
    """Страви одного ключа в одному місті."""

    def __init__(self):
        self.brands = set()
        self.restaurants = set()
        self.prices = []
        self.dishes = []  # (Id страви, назва uk, ru, en)


def collect(db):
    """(місто, ключ) -> KeyStats для всіх активних страв активних ресторанів, крім напоїв."""
    cur = db.cursor(dictionary=True)
    cur.execute(
        """SELECT d.Id, d.Name, d.Price, d.IsPriceMultiple, r.Id AS RestaurantId, r.Name AS RestaurantName, r.City,
                  GROUP_CONCAT(CONCAT_WS(' ', mc.Name, pc.Name) SEPARATOR ' ') AS Categories
           FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
           LEFT JOIN DishCategories dc ON dc.DishId = d.Id
           LEFT JOIN MenuCategories mc ON mc.Id = dc.MenuCategoryId
           LEFT JOIN MenuCategoryRelations rel ON rel.ChildId = mc.Id
           LEFT JOIN MenuCategories pc ON pc.Id = rel.ParentId
           WHERE d.Status = %s AND r.Status = %s
           GROUP BY d.Id""",
        (DISH_STATUS_ACTIVE, RESTAURANT_STATUS_ACTIVE))

    stats = defaultdict(KeyStats)
    drinks = 0
    for row in cur.fetchall():
        # назви категорій - JSON з мовами, для пошуку слів досить сирого тексту
        categories = re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), row["Categories"] or "")
        if DRINK_CATEGORY.search(categories):
            drinks += 1
            continue
        names = {lang: get_localizable_value(row["Name"], lang) for lang in ("uk", "ru", "en")}
        brand = normalize_name(row["RestaurantName"])
        for key in keys_for(names["uk"] or names["ru"]):
            st = stats[(row["City"], key)]
            st.brands.add(brand)
            st.restaurants.add(row["RestaurantId"])
            st.dishes.append((row["Id"], names["uk"], names["ru"], names["en"]))
            # ціна «за 100 г» (IsPriceMultiple) не порівнюється з ціною порції
            if row["Price"] and row["Price"] > 0 and not row["IsPriceMultiple"]:
                st.prices.append(float(row["Price"]))
    return stats, drinks


def merge_spellings(stats):
    """Різне написання однієї страви: «тар тар» / «тар-тар» -> «тартар». Пара, яка без пробілу
    збігається з одним словом того ж міста, додається до нього; повертає {(місто, пара): слово}."""
    merged = {}
    for (city, key) in list(stats):
        if " " not in key:
            continue
        joined = key.replace(" ", "")
        if (city, joined) in stats:
            target, source = stats[(city, joined)], stats.pop((city, key))
            target.brands |= source.brands
            target.restaurants |= source.restaurants
            target.prices += source.prices
            target.dishes += source.dishes
            merged[(city, key)] = joined
    return merged


def candidates(stats, min_venues):
    """Ключі з порогом закладів. Одне слово, яке майже завжди йде з тим самим другим
    («теплий» -> «теплий салат», «картопляне» -> «картопляне пюре»), відкидається - лишається пара;
    так само одне слово-прикметник. Різне написання об'єднується (merge_spellings змінює stats)."""
    merge_spellings(stats)
    best_pair = defaultdict(int)
    for (city, key), st in stats.items():
        if " " in key:
            first = key.split(" ", 1)[0]
            best_pair[(city, first)] = max(best_pair[(city, first)], len(st.brands))

    result = {}
    for (city, key), st in stats.items():
        adjective = ADJECTIVE.search(key) and key not in NOT_ADJECTIVES
        if " " not in key and (best_pair[(city, key)] >= 0.5 * len(st.brands) or adjective):
            continue
        if len(st.brands) >= min_venues:
            result[(city, key)] = st
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min", type=int, default=5, help="мінімум різних закладів у місті")
    parser.add_argument("--top", type=int, default=60, help="скільки ключів показати на місто")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") or row["Id"] for row in cur.fetchall()}

    stats, drinks = collect(db)
    by_city = defaultdict(list)
    for (city, key), st in candidates(stats, args.min).items():
        by_city[city].append((len(st.brands), len(st.restaurants), key, statistics.median(st.prices) if st.prices else 0))

    total = 0
    for city, rows in sorted(by_city.items(), key=lambda x: -len(x[1])):
        rows.sort(reverse=True)
        total += len(rows)
        print(f"\n=== {cities.get(city, city)}: {len(rows)} ключів із >= {args.min} закладами")
        print(f"{'закл.':>5} {'рест.':>5} {'медіана':>8}  ключ")
        for n_brands, n_rest, key, median in rows[:args.top]:
            print(f"{n_brands:>5} {n_rest:>5} {median:>8.0f}  {key}")
    print(f"\nУсього кандидатів: {total}; страв-напоїв відкинуто: {drinks}")


if __name__ == "__main__":
    main()

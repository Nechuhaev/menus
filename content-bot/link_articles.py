"""Посилання в статтях блогу на ресторани й сторінки «страва + місто» - кодом, без AI.

У тексті кожної опублікованої статті (uk і ru окремо) скрипт шукає назви закладів, що є на сайті, і робить
першу згадку посиланням на сторінку ресторану; у статті про конкретне місто - ще й назви страв, для яких
увімкнена сторінка «страва + місто» (/uk/kyiv/dishes/borshch). Статті вже мають покази в Google, тож
посилання з них передають вагу сторінкам ресторанів і страв.

Правила:
- текст усередині <a> і заголовків <h1>-<h6> не чіпається; посилання, що вже є в статті, не дублюються;
- кожна сторінка - одне посилання (перша згадка), не більше MAX_LINKS посилань на статтю;
- назва закладу - точний збіг з урахуванням регістру й меж слова; загальні слова («Кафе», «Піца») пропускаються;
- одна назва в кількох містах (мережа) - береться місто статті; не вдалося визначити - назва пропускається;
- місто статті - те, що найчастіше згадується (назва міста або однозначні заклади);
- старий текст статті зберігається в ArticleLinksLog: --rollback повертає всі статті до стану до посилань.

Запуск:  python3 link_articles.py --dry-run   # показати, що буде додано, нічого не записувати
         python3 link_articles.py             # записати (повторний запуск додає лише нове - для cron)
         python3 link_articles.py --rollback  # прибрати все, що додав скрипт
"""
import argparse
import html
import json
import re
from collections import Counter, defaultdict
from datetime import datetime

from dish_city_pages import MIN_VENUES
from sync_restaurants import get_db, get_localizable_value

LOCALES = ("uk", "ru")
MAX_LINKS = 8
MIN_NAME_LEN = 3
# назви закладів, які в тексті майже завжди означають звичайне слово
GENERIC_NAMES = {
    "кафе", "ресторан", "бар", "паб", "піца", "пицца", "суші", "суши", "кава", "кофе", "кухня", "меню", "хата",
    "дім", "дом", "гриль", "бістро", "бистро", "тераса", "терраса", "шаурма", "кальян", "пекарня", "кондитерська",
    "кондитерская", "їдальня", "столовая", "сніданки", "завтраки", "вино", "пиво", "бургер", "бургери", "бургеры",
    "cafe", "café", "bar", "pub", "pizza", "sushi", "coffee", "grill", "bistro", "wine", "beer", "food", "home",
    "kitchen", "lounge", "street", "burger", "bakery", "terrace", "breakfast", "brunch", "menu",
}
SKIP_TAGS = {"a", "h1", "h2", "h3", "h4", "h5", "h6", "script", "style"}
TAG = re.compile(r"<(/?)([a-zA-Z0-9]+)[^>]*>")
WORD = r"[\w'’]"

LOG_TABLE_SQL = """CREATE TABLE IF NOT EXISTS ArticleLinksLog (
    PageId VARCHAR(255) NOT NULL,
    LinkedAt DATETIME NOT NULL,
    OldContent MEDIUMTEXT NOT NULL,
    Links TEXT NOT NULL,
    KEY IX_ArticleLinksLog_Page (PageId, LinkedAt)
) DEFAULT CHARSET=utf8mb4"""


def localizable(json_str):
    try:
        return {v["Locale"]: v["Value"] or "" for v in json.loads(json_str).get("Values", [])}
    except (json.JSONDecodeError, TypeError, AttributeError, KeyError):
        return {}


def localizable_json(values):
    return json.dumps({"Values": [{"Locale": k, "Value": v} for k, v in values.items()]}, ensure_ascii=False)


def names_of(raw):
    """Назва закладу: звичайний рядок або JSON з мовами -> множина варіантів."""
    values = localizable(raw) if raw and raw.lstrip().startswith("{") else {}
    names = set(values.values()) if values else {raw or ""}
    return {n.strip() for n in names if n and len(n.strip()) >= MIN_NAME_LEN and n.strip().lower() not in GENERIC_NAMES}


def city_pattern(name):
    """Назва міста з відмінками без словника: Київ/Києві, Львів/Львові, Одеса/Одесі, Киев/Киеве."""
    base = re.sub(r"[аяеоуиыіь]$", "", name)
    alts = {base, re.sub(r"ї(?=[^ї]*$)", "є", base), re.sub(r"і(?=[^і]*$)", "о", base)}
    return re.compile(r"(?<!" + WORD + r")(?:" + "|".join(re.escape(a) for a in sorted(alts, key=len, reverse=True))
                      + r")[а-яіїєґё]{0,3}(?!" + WORD + ")")


def load(db):
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT CONCAT(Id, '') AS Id, Name FROM Cities")
    cities = {}
    for row in cur.fetchall():
        slug = (get_localizable_value(row["Name"], "en") or "").lower()
        if slug:
            cities[row["Id"]] = {"slug": slug, "patterns": [city_pattern(n) for n in
                                                             {get_localizable_value(row["Name"], loc) for loc in LOCALES} if n]}

    cur.execute("""SELECT r.Name, CONCAT(r.City, '') AS City, rs.Url
                   FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   WHERE r.Status = 0 AND rs.Url IS NOT NULL AND rs.Url <> ''""")
    restaurants = defaultdict(list)  # назва -> [(місто, url)]
    for row in cur.fetchall():
        if row["City"] in cities:
            for name in names_of(row["Name"]):
                restaurants[name].append((row["City"], row["Url"]))

    cur.execute(f"""SELECT CONCAT(CityId, '') AS CityId, Slug, Title FROM DishCityPages
                    WHERE Enabled = 1 AND Venues >= {MIN_VENUES}""")
    dishes = defaultdict(list)  # місто -> [(locale, назва, slug)]
    for row in cur.fetchall():
        titles = localizable(row["Title"])
        for loc in LOCALES:
            if len(titles.get(loc, "")) >= MIN_NAME_LEN:
                dishes[row["CityId"]].append((loc, titles[loc], row["Slug"]))
    return cities, restaurants, dishes


_WORD_PATTERNS = {}


def whole_word(name):
    if name not in _WORD_PATTERNS:
        _WORD_PATTERNS[name] = re.compile(r"(?<!" + WORD + ")" + re.escape(name) + "(?!" + WORD + ")")
    return _WORD_PATTERNS[name]


def text_of(content):
    return html.unescape(TAG.sub(" ", content))


def article_city(texts, cities, restaurants):
    """Місто, що найчастіше згадується: назва міста + заклади, які є лише в одному місті."""
    votes = Counter()
    for text in texts:
        for city_id, city in cities.items():
            votes[city_id] += sum(len(p.findall(text)) for p in city["patterns"])
        for name, places in restaurants.items():
            if len({c for c, _ in places}) == 1 and name in text and whole_word(name).search(text):
                votes[places[0][0]] += 1
    top = votes.most_common(2)
    if not top or top[0][1] == 0 or (len(top) > 1 and top[0][1] == top[1][1]):
        return None
    return top[0][0]


def segments(content):
    """HTML -> список [текст, можна_посилати]; теги й текст у <a>/<h*> - незмінні."""
    parts, depth, pos = [], 0, 0
    for m in TAG.finditer(content):
        if m.start() > pos:
            parts.append([content[pos:m.start()], depth == 0])
        parts.append([m.group(0), False])
        if m.group(2).lower() in SKIP_TAGS and not m.group(0).endswith("/>"):
            depth = max(0, depth + (-1 if m.group(1) else 1))
        pos = m.end()
    if pos < len(content):
        parts.append([content[pos:], depth == 0])
    return parts


def link_first(parts, name, href, ignore_case):
    pattern = re.compile(r"(?<!" + WORD + ")" + re.escape(html.escape(name, quote=False)) + "(?!" + WORD + ")",
                         re.I if ignore_case else 0)
    for i, (text, linkable) in enumerate(parts):
        if not linkable:
            continue
        m = pattern.search(text)
        if m:
            parts[i:i + 1] = [[text[:m.start()], True], [f'<a href="{href}">{m.group(0)}</a>', False], [text[m.end():], True]]
            return True
    return False


def add_links(content, loc, city_id, cities, restaurants, dishes):
    targets = []  # (назва, адреса, без урахування регістру)
    for name, places in restaurants.items():
        in_city = [p for p in places if p[0] == city_id] if len({c for c, _ in places}) > 1 else places
        if len({url for _, url in in_city}) == 1:  # кілька закладів з однією назвою в місті - неоднозначно
            city, url = in_city[0]
            targets.append((name, f"/{loc}/{cities[city]['slug']}/restaurant/{url}", False))
    if city_id:
        targets += [(title, f"/{loc}/{cities[city_id]['slug']}/dishes/{slug}", True)
                    for dish_loc, title, slug in dishes.get(city_id, []) if dish_loc == loc]
    # довші назви першими: «Пузата хата» не має стати посиланням на «Хата»
    targets.sort(key=lambda t: -len(t[0]))

    parts, added, used = segments(content), [], set()
    existing = set(re.findall(r'href="([^"]+)"', content))
    for name, href, ignore_case in targets:
        if len(added) >= MAX_LINKS:
            break
        if href in existing or href in used:
            continue
        if link_first(parts, name, href, ignore_case):
            used.add(href)
            added.append(f"{name} -> {href}")
    return "".join(p[0] for p in parts), added


def rollback(db):
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    # найраніший запис статті - її текст до будь-яких посилань скрипта
    cur.execute("""SELECT l.PageId, l.OldContent FROM ArticleLinksLog l
                   JOIN (SELECT PageId, MIN(LinkedAt) AS First FROM ArticleLinksLog GROUP BY PageId) f
                     ON f.PageId = l.PageId AND f.First = l.LinkedAt""")
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute("UPDATE Pages SET Content = %s WHERE Id = %s", (row["OldContent"], row["PageId"]))
    write.execute("DELETE FROM ArticleLinksLog")
    db.commit()
    print(f"повернуто статей: {len(rows)} (зміни інших скриптів після посилань теж скасовано)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--rollback", action="store_true")
    args = parser.parse_args()

    db = get_db()
    if args.rollback:
        rollback(db)
        db.close()
        return

    cities, restaurants, dishes = load(db)
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    cur.execute("SELECT Id, Url, Title, Content FROM Pages WHERE Category = 1 AND Status = 1")
    articles = cur.fetchall()

    changed, total = 0, 0
    write = db.cursor()
    for article in articles:
        contents = localizable(article["Content"])
        city_id = article_city([text_of(contents.get(loc, "")) for loc in LOCALES], cities, restaurants)
        new_contents, log = dict(contents), []
        for loc in LOCALES:
            if contents.get(loc):
                new_contents[loc], added = add_links(contents[loc], loc, city_id, cities, restaurants, dishes)
                log += [f"{loc}: {a}" for a in added]
        if not log:
            continue
        changed += 1
        total += len(log)
        city = cities[city_id]["slug"] if city_id else "місто не визначено"
        print(f"\n{article['Url']} ({city}): +{len(log)}")
        for line in log:
            print(f"  {line}")
        if not args.dry_run:
            write.execute("INSERT INTO ArticleLinksLog (PageId, LinkedAt, OldContent, Links) VALUES (%s, %s, %s, %s)",
                          (article["Id"], datetime.now(), article["Content"], "\n".join(log)))
            write.execute("UPDATE Pages SET Content = %s WHERE Id = %s", (localizable_json(new_contents), article["Id"]))
            db.commit()

    print(f"\nстатей: {len(articles)}; змінено: {changed}; посилань додано: {total}")
    if args.dry_run:
        print("нічого не записано (--dry-run)")
    db.close()


if __name__ == "__main__":
    main()

"""Нічний перерахунок сторінок «страва + місто» (таблиця DishCityPages).

Кандидати й фільтри - з dish_city_candidates.py. Нові ключі додаються вимкненими (Enabled = 0):
сторінку вмикає адмін у вкладці «Страва + місто». Для наявних оновлюються цифри й список страв,
Enabled не змінюється. Ключі, що впали нижче порогу, лишаються в таблиці з новими цифрами -
сайт сам не показує сторінку, де закладів менше MinVenues.

Назва сторінки uk - сам ключ, ru/en - найчастіший початок назв цих страв відповідною мовою
(«борщ» -> «Борщ» / «Борщ» / «Borscht»), тобто з уже перекладених назв страв. Якщо такої назви
немає (у полі en часто лежить українська), назва перекладається DeepL один раз і далі береться з таблиці.

Запуск:  python3 dish_city_pages.py --dry-run   # показати, що зміниться
         python3 dish_city_pages.py             # записати (cron щодня після sync_restaurants)
"""
import argparse
import json
import os
import re
import statistics
import uuid
from collections import Counter

import requests

from dish_city_candidates import candidates, collect
from sync_restaurants import get_db, match_key

MIN_VENUES = 5

CREATE_TABLE_SQL = """
CREATE TABLE IF NOT EXISTS DishCityPages (
    Id CHAR(36) NOT NULL PRIMARY KEY,
    CityId VARCHAR(255) NOT NULL,
    KeyUk VARCHAR(255) NOT NULL,
    Slug VARCHAR(255) NOT NULL,
    Title TEXT NOT NULL,
    Enabled TINYINT(1) NOT NULL DEFAULT 0,
    Venues INT NOT NULL,
    Restaurants INT NOT NULL,
    MinPrice DECIMAL(10,2) NOT NULL,
    MedianPrice DECIMAL(10,2) NOT NULL,
    MaxPrice DECIMAL(10,2) NOT NULL,
    DishIds MEDIUMTEXT NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    UNIQUE KEY UX_DishCityPages_City_Key (CityId, KeyUk),
    KEY IX_DishCityPages_City_Slug (CityId, Slug)
) DEFAULT CHARSET=utf8mb4"""

TRANSLIT = {
    "а": "a", "б": "b", "в": "v", "г": "h", "ґ": "g", "д": "d", "е": "e", "є": "ie", "ж": "zh", "з": "z",
    "и": "y", "і": "i", "ї": "i", "й": "i", "к": "k", "л": "l", "м": "m", "н": "n", "о": "o", "п": "p",
    "р": "r", "с": "s", "т": "t", "у": "u", "ф": "f", "х": "kh", "ц": "ts", "ч": "ch", "ш": "sh",
    "щ": "shch", "ь": "", "ю": "iu", "я": "ia", "ы": "y", "э": "e", "ё": "io", "ъ": "",
}


def slugify(key):
    return "-".join("".join(TRANSLIT.get(ch, ch) for ch in word) for word in key.split())


CYRILLIC = re.compile(r"[а-яіїєґё]", re.I)
UKRAINIAN_ONLY = re.compile(r"[іїєґ]", re.I)
LATIN = re.compile(r"[a-z]", re.I)


def fits(lang, text):
    """Назва справді цією мовою: en - латиниця, ru - кирилиця без українських літер."""
    if not text:
        return False
    if lang == "en":
        return bool(LATIN.search(text)) and not CYRILLIC.search(text)
    if lang == "ru":
        return bool(CYRILLIC.search(text)) and not UKRAINIAN_ONLY.search(text)
    return True


def capitalize(text):
    return text[:1].upper() + text[1:]


def title_for(key, names, lang):
    """Найчастіші перші слова назв цією мовою (стільки ж слів, скільки в ключі); None, якщо таких немає."""
    n = len(key.split())
    starts = Counter(" ".join(match_key(name).split()[:n]) for name in names if fits(lang, name))
    starts.pop("", None)
    return capitalize(starts.most_common(1)[0][0]) if starts else None


def deepl(texts, lang):
    """Переклад назв з української (лише ті, яких немає серед назв страв)."""
    if not texts:
        return []
    key = os.environ["DEEPL_API_KEY"]
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    result = []
    for i in range(0, len(texts), 50):
        r = requests.post(url + "/v2/translate", timeout=60, headers={"Authorization": "DeepL-Auth-Key " + key},
                          json={"text": texts[i:i + 50], "source_lang": "UK", "target_lang": {"ru": "RU", "en": "EN-US"}[lang],
                                "context": "Назва страви в меню ресторану"})
        r.raise_for_status()
        result += [capitalize(t["text"]) for t in r.json()["translations"]]
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(CREATE_TABLE_SQL)
    cur.execute("SELECT Id, CityId, KeyUk, Slug, Title, Enabled FROM DishCityPages")
    existing = {(row["CityId"], row["KeyUk"]): row for row in cur.fetchall()}
    used_slugs = {(row["CityId"], row["Slug"]) for row in existing.values()}

    stats, _ = collect(db)
    found = candidates(stats, MIN_VENUES)
    # ключі, що вже є в таблиці, оновлюються навіть нижче порогу (щоб сайт бачив нові цифри)
    keys = set(found) | {k for k in existing if k in stats}

    # назви: з назв страв, інакше вже збережена в таблиці, інакше DeepL (один раз)
    titles, to_translate = {}, {"ru": [], "en": []}
    for city, key in keys:
        st = stats[(city, key)]
        old = json.loads(existing[(city, key)]["Title"]) if (city, key) in existing else {}
        title = {"uk": title_for(key, [d[1] for d in st.dishes], "uk") or capitalize(key)}
        for lang, idx in (("ru", 2), ("en", 3)):
            title[lang] = title_for(key, [d[idx] for d in st.dishes], lang) or (old.get(lang) if fits(lang, old.get(lang)) else None)
            if not title[lang]:
                to_translate[lang].append((city, key))
        titles[(city, key)] = title
    for lang, pending in to_translate.items():
        unique = sorted({titles[k]["uk"] for k in pending})
        print(f"DeepL {lang}: {len(unique)} назв, {sum(len(t) for t in unique)} символів")
        translated = dict(zip(unique, deepl(unique, lang) if not args.dry_run else unique))
        for k in pending:
            titles[k][lang] = translated[titles[k]["uk"]]

    added = updated = 0
    for city, key in sorted(keys):
        st = stats[(city, key)]
        prices = sorted(st.prices) or [0]
        title = titles[(city, key)]
        row = existing.get((city, key))
        if row:
            slug = row["Slug"]
            updated += 1
        else:
            slug = base = slugify(key)
            i = 2
            while (city, slug) in used_slugs:
                slug, i = f"{base}-{i}", i + 1
            used_slugs.add((city, slug))
            added += 1
            if args.dry_run:
                print(f"+ {city[:8]} {len(st.brands):>4} закл.  {key} -> /{slug}  {title}")

        if args.dry_run:
            continue
        cur.execute(
            """INSERT INTO DishCityPages (Id, CityId, KeyUk, Slug, Title, Enabled, Venues, Restaurants,
                                          MinPrice, MedianPrice, MaxPrice, DishIds, UpdatedAt)
               VALUES (%s, %s, %s, %s, %s, 0, %s, %s, %s, %s, %s, %s, NOW())
               ON DUPLICATE KEY UPDATE Title = VALUES(Title), Venues = VALUES(Venues),
                   Restaurants = VALUES(Restaurants), MinPrice = VALUES(MinPrice),
                   MedianPrice = VALUES(MedianPrice), MaxPrice = VALUES(MaxPrice),
                   DishIds = VALUES(DishIds), UpdatedAt = NOW()""",
            (str(uuid.uuid4()), city, key, slug, json.dumps(title, ensure_ascii=False),
             len(st.brands), len(st.restaurants), prices[0], statistics.median(prices), prices[-1],
             json.dumps([d[0] for d in st.dishes])))

    # ключі, яких більше немає серед страв зовсім: 0 закладів - сторінка зникає
    gone = [k for k in existing if k not in stats]
    if not args.dry_run:
        for city, key in gone:
            cur.execute("UPDATE DishCityPages SET Venues = 0, Restaurants = 0, DishIds = '[]', UpdatedAt = NOW() "
                        "WHERE CityId = %s AND KeyUk = %s", (city, key))
        db.commit()
    print(f"нових: {added}, оновлено: {updated}, зникли: {len(gone)}{' (dry-run)' if args.dry_run else ''}")


if __name__ == "__main__":
    main()

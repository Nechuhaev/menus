"""Нічний перерахунок сторінок «страва + місто» (таблиця DishCityPages).

Кандидати й фільтри - з dish_city_candidates.py. Нові ключі додаються вимкненими (Enabled = 0):
сторінку вмикає адмін у вкладці «Страва + місто». Для наявних оновлюються цифри й список страв,
Enabled не змінюється. Ключі, що впали нижче порогу, лишаються в таблиці з новими цифрами -
сайт сам не показує сторінку, де закладів менше MinVenues.

Назва сторінки uk - сам ключ («борщ» -> «Борщ»), ru/en - переклад DeepL з української, один раз
(далі береться з таблиці).
Дублі написання («тар тар» / «тартар») об'єднуються, прикметники без іменника відкидаються.

Запуск:  python3 dish_city_pages.py --dry-run   # показати, що зміниться
         python3 dish_city_pages.py             # записати (cron щодня після sync_restaurants)
"""
import argparse
import json
import os
import re
import statistics
import uuid

import requests

from dish_city_candidates import ADJECTIVE, NOT_ADJECTIVES, candidates, collect, merge_spellings
from sync_restaurants import get_db

MIN_VENUES = 5
TITLE_VERSION = 2  # назви ru/en перекладені DeepL з uk

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
    merged = merge_spellings(stats)  # «тар тар» -> «тартар»
    found = candidates(stats, MIN_VENUES)

    # Рядки таблиці, яких немає серед кандидатів: дубль написання - галочка переходить на
    # об'єднаний ключ, рядок видаляється; прикметник або вимкнений - видаляється;
    # увімкнений нижче порогу - лишається з новими цифрами (сайт його не показує)
    to_delete, to_enable = [], set()
    for (city, key), row in existing.items():
        if (city, key) in found:
            continue
        if (city, key) in merged:
            if row["Enabled"]:
                to_enable.add((city, merged[(city, key)]))
            to_delete.append((city, key))
        elif not row["Enabled"] or (" " not in key and ADJECTIVE.search(key) and key not in NOT_ADJECTIVES):
            to_delete.append((city, key))
    keys = set(found) | {k for k in existing if k in stats and k not in to_delete}

    # назви: uk - з назв страв; ru/en - DeepL з української (порядок слів у мовах різний:
    # «Овочі гриль» - «Grilled vegetables»), перекладене зберігається й не перекладається вдруге
    titles, to_translate = {}, {"ru": [], "en": []}
    for city, key in keys:
        st = stats[(city, key)]
        old = json.loads(existing[(city, key)]["Title"]) if (city, key) in existing else {}
        # uk - сам ключ (після об'єднання «тар тар» -> «тартар» перші слова назв дали б «Тар»)
        title = {"uk": capitalize(key), "v": TITLE_VERSION}
        for lang in ("ru", "en"):
            keep = old.get("v") == TITLE_VERSION and old.get("uk") == title["uk"] and fits(lang, old.get(lang))
            title[lang] = old[lang] if keep else None
            if not keep:
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
    gone = [k for k in existing if k not in stats and k not in to_delete]
    if args.dry_run:
        for city, key in to_delete:
            target = merged.get((city, key))
            print(f"- {city[:8]} {key}" + (f" -> {target}" if target else ""))
    else:
        for city, key in to_delete:
            cur.execute("DELETE FROM DishCityPages WHERE CityId = %s AND KeyUk = %s", (city, key))
        for city, key in to_enable:
            cur.execute("UPDATE DishCityPages SET Enabled = 1 WHERE CityId = %s AND KeyUk = %s", (city, key))
        for city, key in gone:
            cur.execute("UPDATE DishCityPages SET Venues = 0, Restaurants = 0, DishIds = '[]', UpdatedAt = NOW() "
                        "WHERE CityId = %s AND KeyUk = %s", (city, key))
        db.commit()
    print(f"нових: {added}, оновлено: {updated}, видалено: {len(to_delete)}, зникли: {len(gone)}"
          f"{' (dry-run)' if args.dry_run else ''}")


if __name__ == "__main__":
    main()

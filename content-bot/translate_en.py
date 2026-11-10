"""Англійська для iframe-меню: переклад uk -> en через DeepL (не LLM).

Iframe закладу показує мови UA та EN. Англійські тексти перекладаються з української лише для
закладів з iframe: назви, короткі описи, склад і описи страв, назви розділів меню, кухня й адреса.
Перекладається тільки те, де англійської ще немає, а українська є і вона справді українська
(російський текст у полі uk спершу виправляє translate_missing.py).

Запуск:  python3 translate_en.py --iframe --dry-run              # скільки текстів і символів, без DeepL
         python3 translate_en.py --iframe                        # усі заклади, що налаштували iframe
         python3 translate_en.py --restaurant chachapuri         # один заклад: Id або адреса сторінки
Змінні в .env: DEEPL_API_KEY; ліміт за запуск - DEEPL_MAX_CHARS (за замовчуванням 100000).
"""
import argparse
import os

import requests

from translate_missing import (DEEPL_API_KEY, DEEPL_URL, get_db, get_locale_value, looks_russian,
                               set_locale_value)

MAX_CHARS = int(os.environ.get("DEEPL_MAX_CHARS", "100000"))
BATCH = 40
spent = [0]

# (таблиця, ключ, колонки, умова за рестораном)
TARGETS = [
    ("Dishes", "Id", ("Name", "ShortDescription", "Ingredients", "Description"), "RestaurantId = %s AND Status = 0"),
    ("MenuCategories", "Id", ("Name",), "RestaurantId = %s"),
    ("RestaurantInfo", "RestaurantId", ("Cuisine", "DisplayAddress"), "RestaurantId = %s"),
]


def needs_en(value):
    """Український текст для перекладу або None (англійська вже є / української немає / вона російська)."""
    if get_locale_value(value, "en").strip():
        return None
    uk = get_locale_value(value, "uk").strip()
    if not uk or looks_russian(uk):
        return None
    return uk


def deepl_en(texts):
    size = sum(len(t) for t in texts)
    if spent[0] + size > MAX_CHARS:
        raise SystemExit(f"ліміт DEEPL_MAX_CHARS={MAX_CHARS} вичерпано ({spent[0]} символів) - запустіть ще раз або збільште ліміт")
    r = requests.post(f"{DEEPL_URL}/v2/translate", headers={"Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}"},
                      json={"text": texts, "source_lang": "UK", "target_lang": "EN-GB",
                            "tag_handling": "html", "preserve_formatting": True}, timeout=60)
    r.raise_for_status()
    spent[0] += size
    return [t["text"] for t in r.json()["translations"]]


def restaurant_ids(db, args):
    cur = db.cursor()
    ids = []
    if args.iframe:
        cur.execute("SELECT CONCAT(RestaurantId, '') FROM IframeDesign")
        ids += [row[0] for row in cur.fetchall()]
    for value in args.restaurant or []:
        cur.execute("SELECT CONCAT(ItemId, '') FROM RestaurantSeo WHERE Url = %s LIMIT 1", (value,))
        row = cur.fetchone()
        ids.append(row[0] if row else value)
    cur.close()
    return list(dict.fromkeys(ids))


def collect(db, restaurant_id):
    """[(таблиця, ключ, id, колонка, json, текст uk)] - усе, що треба перекласти в цьому закладі."""
    jobs = []
    cur = db.cursor(dictionary=True)
    for table, key, columns, where in TARGETS:
        cur.execute(f"SELECT CONCAT({key}, '') AS K, {', '.join(columns)} FROM {table} WHERE {where}", (restaurant_id,))
        for row in cur.fetchall():
            for column in columns:
                text = needs_en(row[column])
                if text:
                    jobs.append((table, key, row["K"], column, row[column], text))
    cur.close()
    return jobs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--iframe", action="store_true", help="усі заклади, що налаштували iframe (таблиця IframeDesign)")
    parser.add_argument("--restaurant", action="append", metavar="ID", help="Id або адреса сторінки закладу (можна кілька)")
    parser.add_argument("--dry-run", action="store_true", help="лише порахувати, без DeepL і без запису")
    args = parser.parse_args()
    if not args.iframe and not args.restaurant:
        parser.error("вкажіть --iframe або --restaurant")

    db = get_db()
    total_texts = total_chars = 0
    for rid in restaurant_ids(db, args):
        jobs = collect(db, rid)
        chars = sum(len(j[5]) for j in jobs)
        total_texts += len(jobs)
        total_chars += chars
        print(f"{rid}: текстів {len(jobs)}, символів {chars}")
        if args.dry_run or not jobs:
            continue
        cur = db.cursor()
        for i in range(0, len(jobs), BATCH):
            part = jobs[i:i + BATCH]
            for (table, key, kid, column, value, _), en in zip(part, deepl_en([j[5] for j in part])):
                # той самий рядок може мати кілька колонок - читаємо свіже значення не потрібно: кожна колонка окремо
                cur.execute(f"UPDATE {table} SET {column} = %s WHERE {key} = %s", (set_locale_value(value, "en", en), kid))
            db.commit()
        cur.close()
    print(f"\nусього: текстів {total_texts}, символів {total_chars}; DeepL витрачено {spent[0]}"
          + (" (нічого не записано - --dry-run)" if args.dry_run else ""))
    db.close()


if __name__ == "__main__":
    main()

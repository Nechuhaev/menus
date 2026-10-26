"""Дозаповнює переклади описів страв через DeepL: опис є хоч однією мовою - інші (uk/ru/en) перекладаються.
Поля: Description (опис), ShortDescription (короткий опис, іде в JSON-LD), Ingredients (склад).

translate_missing.py перекладає лише ru -> uk; тут - будь-який напрямок, зокрема описи, які
clean_descriptions.py переніс з ru в uk (там тепер порожньо, сайт показує українську і російськомовним).
Джерело - українська, якщо є, інакше російська, інакше англійська. Першими - страви ресторанів, чиї
сторінки мають кліки в Search Console. Не більше DESC_DEEPL_MAX_CHARS символів за запуск (решта - наступного).

Запуск:  python3 fill_translations.py --dry-run          # скільки описів і символів DeepL треба, нічого не пише
         python3 fill_translations.py                    # перекласти в межах бюджету запуску
         python3 fill_translations.py --langs ru         # лише російська
"""
import argparse
import json
import os

import requests

from sync_restaurants import RESTAURANT_CLICKS_JOIN, KEEP_INDEXED_TABLE_SQL, get_db

DEEPL_TARGET = {"uk": "UK", "ru": "RU", "en": "EN-US"}
SOURCE_ORDER = ("uk", "ru", "en")
BATCH = 50
MAX_CHARS = int(os.environ.get("DESC_DEEPL_MAX_CHARS", "15000"))
FIELDS = ("Description", "ShortDescription", "Ingredients")


def values(description):
    try:
        parsed = json.loads(description or "")
    except ValueError:
        return {}
    if not isinstance(parsed, dict):
        return {}
    return {v.get("Locale"): (v.get("Value") or "").strip() for v in parsed.get("Values") or [] if (v.get("Value") or "").strip()}


def deepl(texts, source, target):
    key = os.environ["DEEPL_API_KEY"]
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    r = requests.post(url + "/v2/translate", timeout=60, headers={"Authorization": "DeepL-Auth-Key " + key},
                      json={"text": texts, "source_lang": DEEPL_TARGET[source][:2], "target_lang": DEEPL_TARGET[target],
                            "tag_handling": "html", "preserve_formatting": True, "context": "Опис страви в меню ресторану"})
    r.raise_for_status()
    return [t["text"] for t in r.json()["translations"]]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--langs", default="uk,ru,en", help="мови, які дозаповнювати")
    args = parser.parse_args()
    targets = [l for l in args.langs.split(",") if l in DEEPL_TARGET]

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(KEEP_INDEXED_TABLE_SQL)
    cur.execute("""SELECT CONCAT(d.Id, '') AS Id, d.Description, d.ShortDescription, d.Ingredients
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   JOIN RestaurantSeo rs ON rs.ItemId = r.Id""" + RESTAURANT_CLICKS_JOIN + """
                   WHERE d.Status = 0 AND r.Status = 0
                     AND (d.Description <> '' OR d.ShortDescription <> '' OR d.Ingredients <> '')
                   ORDER BY IFNULL(k.Clicks, 0) DESC, r.Id""")

    # (мова-джерело, мова-ціль) -> [((Id страви, поле), текст)]
    jobs, texts = {}, {}
    for row in cur.fetchall():
        for field in FIELDS:
            have = values(row[field])
            source = next((l for l in SOURCE_ORDER if l in have), None)
            if not source:
                continue
            texts[(row["Id"], field)] = row[field]
            for target in targets:
                if target not in have:
                    jobs.setdefault((source, target), []).append(((row["Id"], field), have[source]))

    total = sum(len(t) for items in jobs.values() for _, t in items)
    for (source, target), items in sorted(jobs.items()):
        print(f"{source} -> {target}: текстів {len(items)}, символів {sum(len(t) for _, t in items)}")
    print(f"усього символів DeepL: {total}; за запуск не більше {MAX_CHARS}")
    if args.dry_run:
        db.close()
        return

    spent, done = 0, 0
    write = db.cursor()
    for (source, target), items in sorted(jobs.items()):
        for i in range(0, len(items), BATCH):
            batch = items[i:i + BATCH]
            size = sum(len(t) for _, t in batch)
            if spent + size > MAX_CHARS:
                batch, size = [], 0
                for item in items[i:i + BATCH]:
                    if spent + size + len(item[1]) > MAX_CHARS:
                        break
                    batch.append(item)
                    size += len(item[1])
                if not batch:
                    break
            for (key, _), translated in zip(batch, deepl([t for _, t in batch], source, target)):
                parsed = json.loads(texts[key])
                # порожнє значення цієї мови (якщо є) замінюється, інакше додається
                parsed["Values"] = [v for v in parsed.get("Values") or [] if v.get("Locale") != target]
                parsed["Values"].append({"Locale": target, "Value": translated})
                texts[key] = json.dumps(parsed, ensure_ascii=False)
                dish_id, field = key
                write.execute(f"UPDATE Dishes SET {field} = %s WHERE Id = %s", (texts[key], dish_id))
                done += 1
            spent += size
            db.commit()
            if spent >= MAX_CHARS:
                break
    db.close()
    print(f"перекладено текстів: {done}, символів: {spent}")


if __name__ == "__main__":
    main()

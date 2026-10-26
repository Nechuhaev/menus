"""Дозаповнює переклади через DeepL між українською й російською: текст є однією з них - друга перекладається.
Спершу назви (страви й категорії меню: страви, додані зі «Звітів», мають назву лише мовою джерела, і на /ru/
показувалась українська), потім описи страв: Description, ShortDescription (іде в JSON-LD), Ingredients.
Англійська не перекладається і не є джерелом: латинська назва - бренд або власна назва («PASTORALE»).

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

# сайт працює з українською й російською: англійською не перекладаємо і з англійської теж
DEEPL_TARGET = {"uk": "UK", "ru": "RU"}
SOURCE_ORDER = ("uk", "ru")
BATCH = 50
MAX_CHARS = int(os.environ.get("DESC_DEEPL_MAX_CHARS", "15000"))
FIELDS = ("Description", "ShortDescription", "Ingredients")
NAME_SOURCES = ("uk", "ru")
CONTEXT = {"name": "Назва страви або розділу меню ресторану", "description": "Опис страви в меню ресторану"}


def values(description):
    try:
        parsed = json.loads(description or "")
    except ValueError:
        return {}
    if not isinstance(parsed, dict):
        return {}
    return {v.get("Locale"): (v.get("Value") or "").strip() for v in parsed.get("Values") or [] if (v.get("Value") or "").strip()}


def deepl(texts, source, target, kind):
    key = os.environ["DEEPL_API_KEY"]
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    r = requests.post(url + "/v2/translate", timeout=60, headers={"Authorization": "DeepL-Auth-Key " + key},
                      json={"text": texts, "source_lang": DEEPL_TARGET[source][:2], "target_lang": DEEPL_TARGET[target],
                            "tag_handling": "html", "preserve_formatting": True, "context": CONTEXT[kind]})
    r.raise_for_status()
    return [t["text"] for t in r.json()["translations"]]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--langs", default="uk,ru", help="мови, які дозаповнювати (uk, ru)")
    args = parser.parse_args()
    targets = [l for l in args.langs.split(",") if l in DEEPL_TARGET]

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(KEEP_INDEXED_TABLE_SQL)
    # (вид, мова-джерело, мова-ціль) -> [((таблиця, Id, поле), текст)]
    jobs, texts = {}, {}

    def add(kind, table, row_id, field, value, sources):
        have = values(value)
        source = next((l for l in sources if l in have), None)
        if not source:
            return
        texts[(table, row_id, field)] = value
        for target in targets:
            if target not in have:
                jobs.setdefault((kind, source, target), []).append(((table, row_id, field), have[source]))

    order = "ORDER BY IFNULL(k.Clicks, 0) DESC, r.Id"
    cur.execute("""SELECT CONCAT(d.Id, '') AS Id, d.Name, d.Description, d.ShortDescription, d.Ingredients
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   JOIN RestaurantSeo rs ON rs.ItemId = r.Id""" + RESTAURANT_CLICKS_JOIN + """
                   WHERE d.Status = 0 AND r.Status = 0 """ + order)
    for row in cur.fetchall():
        add("name", "Dishes", row["Id"], "Name", row["Name"], NAME_SOURCES)
        for field in FIELDS:
            add("description", "Dishes", row["Id"], field, row[field], SOURCE_ORDER)
    cur.execute("""SELECT CONCAT(c.Id, '') AS Id, c.Name
                   FROM MenuCategories c JOIN Restaurants r ON r.Id = c.RestaurantId
                   JOIN RestaurantSeo rs ON rs.ItemId = r.Id""" + RESTAURANT_CLICKS_JOIN + """
                   WHERE r.Status = 0 """ + order)
    for row in cur.fetchall():
        add("name", "MenuCategories", row["Id"], "Name", row["Name"], NAME_SOURCES)

    total = sum(len(t) for items in jobs.values() for _, t in items)
    # назви першими: коротші й видні в меню ресторану
    ordered = sorted(jobs.items(), key=lambda j: (j[0][0] != "name", j[0]))
    for (kind, source, target), items in ordered:
        print(f"{'назви' if kind == 'name' else 'описи'} {source} -> {target}: текстів {len(items)}, символів {sum(len(t) for _, t in items)}")
    print(f"усього символів DeepL: {total}; за запуск не більше {MAX_CHARS}")
    if args.dry_run:
        db.close()
        return

    spent, done = 0, 0
    write = db.cursor()
    for (kind, source, target), items in ordered:
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
            for (key, _), translated in zip(batch, deepl([t for _, t in batch], source, target, kind)):
                parsed = json.loads(texts[key])
                # порожнє значення цієї мови (якщо є) замінюється, інакше додається
                parsed["Values"] = [v for v in parsed.get("Values") or [] if v.get("Locale") != target]
                parsed["Values"].append({"Locale": target, "Value": translated})
                texts[key] = json.dumps(parsed, ensure_ascii=False)
                table, row_id, field = key
                write.execute(f"UPDATE {table} SET {field} = %s WHERE Id = %s", (texts[key], row_id))
                done += 1
            spent += size
            db.commit()
            if spent >= MAX_CHARS:
                break
    db.close()
    print(f"перекладено текстів: {done}, символів: {spent}")


if __name__ == "__main__":
    main()

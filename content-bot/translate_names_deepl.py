#!/usr/bin/env python3
"""
Переклад назв страв і категорій меню ru -> uk через DeepL.

Береться лише те, де є російська назва, але немає української:
  - назва латиницею або вже українською - копіюється як є (без DeepL);
  - решта - DeepL, однакові назви перекладаються один раз, не більше DEEPL_MAX_CHARS
    символів за запуск. Переклади інших записів не підставляються: у різних ресторанів
    одна й та сама російська назва може мати різну власну українську.
Російська назва не змінюється, URL сторінок (DishSeo.Url) теж. До DishSeo.SearchTerms
додається українська назва, щоб пошук знаходив страву українською.

Змінні в .env: DEEPL_API_KEY (ключ Free закінчується на :fx), DEEPL_MAX_CHARS (за замовчуванням
15000 - вкладається в 500 тис./міс. безкоштовного тарифу при щоденному запуску).

Запуск:  python3 translate_names_deepl.py --dry-run   # показати переклади, нічого не писати
         python3 translate_names_deepl.py             # перекласти й записати
"""
import html
import json
import os
import re
import sys

import mysql.connector
import requests
from dotenv import load_dotenv

load_dotenv()

DEEPL_API_KEY = os.environ["DEEPL_API_KEY"]
DEEPL_MAX_CHARS = int(os.environ.get("DEEPL_MAX_CHARS", "15000"))
DEEPL_URL = "https://api-free.deepl.com" if DEEPL_API_KEY.endswith(":fx") else "https://api.deepl.com"
DEEPL_BATCH = 50  # максимум текстів в одному запиті DeepL

DB_HOST = os.environ.get("DB_HOST", "127.0.0.1")
DB_PORT = int(os.environ.get("DB_PORT", "3306"))
DB_USER = os.environ.get("DB_USER", "root")
DB_PASSWORD = os.environ["DB_PASSWORD"]
DB_NAME = os.environ.get("DB_NAME", "menus")

DRY_RUN = "--dry-run" in sys.argv

CYRILLIC = re.compile(r"[А-Яа-яЁёЇїІіЄєҐґ]")
UKRAINIAN_ONLY = re.compile(r"[ЇїІіЄєҐґ]")
RUSSIAN_ONLY = re.compile(r"[ЫыЭэЪъЁё]")
UKRAINIAN_WORD = re.compile(r"(?i)(?:^|\s)(?:з|із|зі|та)(?:\s|$)|\w['ʼ’]\w")
# скорочення на кшталт «б/а», «с/с» DeepL не розуміє («б/а» -> «без дати») - передаємо як є
ABBREVIATION = re.compile(r"(?<!\w)\w{1,3}/\w{1,3}(?!\w)")
# слова, які DeepL перекладає не так, як прийнято в меню («сет» -> «набір»)
FIXED = {"сет": "сет", "сети": "сети", "сеты": "сети"}
# латинська «i» всередині кириличного слова («огiрками», «горiлка»)
LATIN_I = re.compile(r"(?<=[А-Яа-яЇїІіЄєҐґ'’ʼ])[iI]|[iI](?=[А-Яа-яЇїІіЄєҐґ])")


def fix_latin_i(text):
    return LATIN_I.sub(lambda m: "і" if m.group(0) == "i" else "І", text)


def fixed(text):
    """Назва з FIXED (з великої літери, якщо оригінал з великої) або None."""
    uk = FIXED.get(text.lower())
    if uk and text[:1].isupper():
        uk = uk[:1].upper() + uk[1:]
    return uk


def is_ukrainian(text):
    """Назви, збережені як «ru», часто вже українською («Рол з хрусткою куркою»)."""
    if RUSSIAN_ONLY.search(text):
        return False
    return bool(UKRAINIAN_ONLY.search(text) or UKRAINIAN_WORD.search(text))


def get_db():
    return mysql.connector.connect(host=DB_HOST, port=DB_PORT, user=DB_USER, password=DB_PASSWORD, database=DB_NAME)


def get_value(json_str, locale):
    try:
        for v in (json.loads(json_str) if json_str else {}).get("Values", []):
            if v.get("Locale") == locale:
                return (v.get("Value") or "").strip()
    except (json.JSONDecodeError, TypeError, AttributeError):
        pass
    return ""


def set_value(json_str, locale, value):
    try:
        parsed = json.loads(json_str) if json_str else {"Values": []}
    except (json.JSONDecodeError, TypeError):
        parsed = {"Values": []}
    values = [v for v in parsed.get("Values", []) if v.get("Locale") != locale]
    values.append({"Locale": locale, "Value": value})
    parsed["Values"] = values
    return json.dumps(parsed, ensure_ascii=False, separators=(",", ":"))


def deepl_usage():
    r = requests.get(f"{DEEPL_URL}/v2/usage", headers={"Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}"}, timeout=30)
    r.raise_for_status()
    d = r.json()
    return d.get("character_count", 0), d.get("character_limit", 0)


CONTEXT = {
    "MenuCategories": "Назва розділу меню ресторану, наприклад: супи, салати, сети, напої.",
    "Dishes": "Назва страви або напою в меню ресторану.",
}


def deepl_translate(texts, table):
    """Мову оригіналу визначає DeepL: назви, збережені як «ru», часто вже українські
    («супи», «сет») - примусове RU перетворювало їх на «супі», «сітка». Якщо DeepL
    визначив українську, лишаємо оригінал. context не тарифікується."""
    r = requests.post(
        f"{DEEPL_URL}/v2/translate",
        headers={"Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}"},
        json={"text": [_protect(t) for t in texts], "target_lang": "UK", "context": CONTEXT[table],
              "preserve_formatting": True, "tag_handling": "xml", "ignore_tags": ["k"]},
        timeout=60,
    )
    r.raise_for_status()
    return [src if t.get("detected_source_language") == "UK" else _unprotect(t["text"])
            for src, t in zip(texts, r.json()["translations"])]


def _protect(text):
    text = html.escape(text, quote=False)
    return ABBREVIATION.sub(lambda m: f"<k>{m.group(0)}</k>", text)


def _unprotect(text):
    return html.unescape(re.sub(r"</?k>", "", text))


def load_rows(db, table):
    cur = db.cursor()
    if table == "Dishes":
        # спершу страви активних ресторанів, головна сторінка - першою
        cur.execute(
            """SELECT d.Id, d.Name FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
               LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
               ORDER BY r.Status = 0 DESC, IFNULL(ri.ShowOnIndex, 0) DESC, r.Id"""
        )
    else:
        cur.execute(f"SELECT Id, Name FROM {table}")
    rows = cur.fetchall()
    cur.close()
    return rows


def plan(rows):
    """Повертає список (id, name_json, ru, uk або None - треба DeepL)."""
    todo = []
    for _id, name in rows:
        ru, uk = get_value(name, "ru"), get_value(name, "uk")
        if uk or not ru:
            continue
        if fixed(ru):
            todo.append((_id, name, ru, fixed(ru)))
        elif not CYRILLIC.search(ru) or is_ukrainian(ru):
            todo.append((_id, name, ru, fix_latin_i(ru)))  # латиниця/бренд або вже українською
        else:
            todo.append((_id, name, ru, None))
    return todo


def translate_pending(todo, budget, table):
    """Перекладає унікальні назви через DeepL у межах бюджету символів."""
    pending = []
    seen = set()
    spent = 0
    for _id, _name, ru, uk in todo:
        if uk is None and ru.lower() not in seen:
            if spent + len(ru) > budget:
                break
            seen.add(ru.lower())
            pending.append(ru)
            spent += len(ru)
    result = {}
    for i in range(0, len(pending), DEEPL_BATCH):
        chunk = pending[i:i + DEEPL_BATCH]
        for src, dst in zip(chunk, deepl_translate(chunk, table)):
            result[src.lower()] = fix_latin_i(dst.strip())
    return result, spent


def apply(db, table, todo, translated):
    cur = db.cursor()
    done = 0
    for _id, name, ru, uk in todo:
        uk = uk or translated.get(ru.lower())
        if not uk:
            continue  # не вмістилось у бюджет - наступного запуску
        if DRY_RUN:
            if done < 40:
                print(f"  {ru}  ->  {uk}")
        else:
            cur.execute(f"UPDATE {table} SET Name = %s WHERE Id = %s", (set_value(name, "uk", uk), _id))
            if table == "Dishes":
                cur.execute(
                    "UPDATE DishSeo SET SearchTerms = CONCAT(IFNULL(SearchTerms, ''), ' ', %s) "
                    "WHERE ItemId = %s AND IFNULL(SearchTerms, '') NOT LIKE CONCAT('%%', %s, '%%')",
                    (uk, _id, uk),
                )
        done += 1
    if not DRY_RUN:
        db.commit()
    cur.close()
    return done


def main():
    used, limit = deepl_usage()
    remaining = (limit - used) if limit else DEEPL_MAX_CHARS
    budget = min(DEEPL_MAX_CHARS, remaining, 500 if DRY_RUN else DEEPL_MAX_CHARS)  # dry-run: лише зразок
    print(f"DeepL: використано {used} з {limit}, бюджет цього запуску {budget} символів{' (DRY RUN)' if DRY_RUN else ''}")

    db = get_db()
    try:
        for table in ("MenuCategories", "Dishes"):
            rows = load_rows(db, table)
            todo = plan(rows)
            free = sum(1 for t in todo if t[3] is not None)
            need = sum(len(ru) for ru in {t[2].lower() for t in todo if t[3] is None})
            translated, spent = translate_pending(todo, budget, table) if budget > 0 else ({}, 0)
            budget -= spent
            done = apply(db, table, todo, translated)
            left = len(todo) - done
            print(f"{table}: без uk {len(todo)}, без DeepL {free}, DeepL {spent} з {need} симв., записано {done}, лишилось {left}")
    finally:
        db.close()


if __name__ == "__main__":
    main()

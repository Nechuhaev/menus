"""Англійська для iframe-меню: переклад uk -> en через DeepL (не LLM).

Iframe закладу показує мови UA та EN. Англійські тексти перекладаються з української лише для
закладів з iframe: назви, короткі описи, склад і описи страв, назви розділів меню, кухня й адреса.
Перекладається тільки те, де англійської ще немає, а українська є і вона справді українська
(російський текст у полі uk спершу виправляє translate_missing.py).

Назви страв грузинської кухні тощо (GLOSSARY) DeepL не перекладає - підставляємо готову англійську
(«Харчо» -> Kharcho, а не «Food»). Якщо української немає - перекладаємо з російської.

Запуск:  python3 translate_en.py --iframe --dry-run              # скільки текстів і символів, без DeepL
         python3 translate_en.py --iframe                        # усі заклади, що налаштували iframe
         python3 translate_en.py --restaurant chachapuri         # один заклад: Id або адреса сторінки
         python3 translate_en.py --iframe --redo-glossary        # перекласти наново тексти з термінами глосарію
Змінні в .env: DEEPL_API_KEY; ліміт за запуск - DEEPL_MAX_CHARS (за замовчуванням 100000).
"""
import argparse
import os
import re

import requests

from translate_missing import (DEEPL_API_KEY, DEEPL_URL, get_db, get_locale_value, looks_russian,
                               set_locale_value)

MAX_CHARS = int(os.environ.get("DEEPL_MAX_CHARS", "100000"))
BATCH = 40
spent = [0]

# слово (нижній регістр) -> англійська; «*» у кінці - основа, ловиться з будь-яким закінченням
GLOSSARY = {
    "харчо": "kharcho", "мочі": "mochi", "тоне": "tone", "каре": "rack", "лате": "latte", "латте": "latte",
    "додатки": "extras", "хінкалі": "khinkali", "хинкалі": "khinkali", "хинкали": "khinkali", "хачапурі": "khachapuri",
    "чкмерулі": "chkmeruli", "шкмерулі": "shkmeruli", "оджахурі": "ojakhuri", "аджахурі": "ojakhuri", "пхалі": "pkhali",
    "сациві": "satsivi", "чахохбілі": "chakhokhbili", "лобіо": "lobio", "сулугун*": "suluguni", "аджик*": "adjika",
    "ткемалі": "tkemali", "чурчхел*": "churchkhela", "мацоні": "matsoni", "тархун*": "tarragon", "боржомі": "Borjomi",
    "боржоми": "Borjomi", "патара": "patara", "мчаді": "mchadi", "ачма": "achma", "чашушулі": "chashushuli",
    "чанахі": "chanakhi", "бозбаші": "bozbashi", "махан": "makhan", "капучино": "cappuccino", "еспресо": "espresso",
    "американо": "americano", "тоне меню": "tone menu", "пінн*": "penovani",
}
# DeepL з tag_handling=html повертає лапки й & як сутності, а сайт екранує їх ще раз («&quot;MILK STRAND&quot;»)
ENTITIES = {"&quot;": '"', "&#39;": "'", "&#x27;": "'", "&amp;": "&"}
GLOSSARY_RE = re.compile(r"(?<!\w)(" + "|".join(
    re.escape(k[:-1]) + r"\w*" if k.endswith("*") else re.escape(k) + r"(?!\w)"
    for k in sorted(GLOSSARY, key=len, reverse=True)) + ")", re.IGNORECASE)
STEMS = sorted(GLOSSARY, key=len, reverse=True)
CYRILLIC_RE = re.compile(r"[а-яёіїєґ]", re.IGNORECASE)
KEEP_RE = re.compile(r'<span translate="no">(.*?)</span>')


def protect(text):
    """Текст, де терміни глосарію вже англійською і DeepL їх не чіпає (translate="no")."""
    out, pos = [], 0
    for m in GLOSSARY_RE.finditer(text):
        word = m.group(0)
        low = word.lower()
        en = next(GLOSSARY[k] for k in STEMS if (low.startswith(k[:-1]) if k.endswith("*") else low == k))
        if word.isupper() and len(word) > 1:
            en = en.upper()
        elif word[0].isupper():
            en = en[0].upper() + en[1:]
        out += [text[pos:m.start()], f'<span translate="no">{en}</span>']
        pos = m.end()
    out.append(text[pos:])
    return "".join(out)


def unprotect(text):
    text = KEEP_RE.sub(lambda m: m.group(1), text)
    for entity, char in ENTITIES.items():
        text = text.replace(entity, char)
    return text

# (таблиця, ключ, колонки, умова за рестораном)
TARGETS = [
    ("Dishes", "Id", ("Name", "ShortDescription", "Ingredients", "Description"), "RestaurantId = %s AND Status = 0"),
    ("MenuCategories", "Id", ("Name",), "RestaurantId = %s"),
    ("RestaurantInfo", "RestaurantId", ("Cuisine", "DisplayAddress"), "RestaurantId = %s"),
]


def needs_en(value, redo_glossary=False):
    """(мова джерела, текст) для перекладу або None. Джерело - українська, а якщо її немає чи вона насправді
    російська - російська. Англійська вже є - None, крім --redo-glossary для текстів з термінами глосарію
    і крім «англійської», що є копією української чи російської."""
    uk = get_locale_value(value, "uk").strip()
    source = ("UK", uk) if uk and not looks_russian(uk) else ("RU", get_locale_value(value, "ru").strip())
    if not source[1]:
        return None
    en = get_locale_value(value, "en").strip()
    # англійська є, але це копія української/російської, кирилиця («Грузинская кухня») або сутності &quot; - наново
    broken = (en in (uk, get_locale_value(value, "ru").strip()) or any(e in en for e in ENTITIES)
              or CYRILLIC_RE.search(en))
    if en and not broken and not (redo_glossary and GLOSSARY_RE.search(source[1])):
        return None
    return source


def deepl_en(texts, source_lang):
    size = sum(len(t) for t in texts)
    if spent[0] + size > MAX_CHARS:
        raise SystemExit(f"ліміт DEEPL_MAX_CHARS={MAX_CHARS} вичерпано ({spent[0]} символів) - запустіть ще раз або збільште ліміт")
    r = requests.post(f"{DEEPL_URL}/v2/translate", headers={"Authorization": f"DeepL-Auth-Key {DEEPL_API_KEY}"},
                      json={"text": [protect(t) for t in texts], "source_lang": source_lang, "target_lang": "EN-GB",
                            "tag_handling": "html", "preserve_formatting": True}, timeout=60)
    r.raise_for_status()
    spent[0] += size
    return [unprotect(t["text"]) for t in r.json()["translations"]]


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


def collect(db, restaurant_id, redo_glossary=False):
    """[(таблиця, ключ, id, колонка, json, (мова, текст))] - усе, що треба перекласти в цьому закладі."""
    jobs = []
    cur = db.cursor(dictionary=True)
    for table, key, columns, where in TARGETS:
        cur.execute(f"SELECT CONCAT({key}, '') AS K, {', '.join(columns)} FROM {table} WHERE {where}", (restaurant_id,))
        for row in cur.fetchall():
            for column in columns:
                text = needs_en(row[column], redo_glossary)
                if text:
                    jobs.append((table, key, row["K"], column, row[column], text))
    cur.close()
    return jobs


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--iframe", action="store_true", help="усі заклади, що налаштували iframe (таблиця IframeDesign)")
    parser.add_argument("--restaurant", action="append", metavar="ID", help="Id або адреса сторінки закладу (можна кілька)")
    parser.add_argument("--dry-run", action="store_true", help="лише порахувати, без DeepL і без запису")
    parser.add_argument("--redo-glossary", action="store_true", help="перекласти наново тексти з термінами глосарію")
    args = parser.parse_args()
    if not args.iframe and not args.restaurant:
        parser.error("вкажіть --iframe або --restaurant")

    db = get_db()
    total_texts = total_chars = 0
    for rid in restaurant_ids(db, args):
        jobs = collect(db, rid, args.redo_glossary)
        chars = sum(len(j[5][1]) for j in jobs)
        total_texts += len(jobs)
        total_chars += chars
        print(f"{rid}: текстів {len(jobs)}, символів {chars}")
        if args.dry_run or not jobs:
            continue
        cur = db.cursor()
        for lang in ("UK", "RU"):
            same = [j for j in jobs if j[5][0] == lang]
            for i in range(0, len(same), BATCH):
                part = same[i:i + BATCH]
                for (table, key, kid, column, value, _), en in zip(part, deepl_en([j[5][1] for j in part], lang)):
                    # кожна колонка пишеться окремо, тож інші колонки того самого рядка не перетираються
                    cur.execute(f"UPDATE {table} SET {column} = %s WHERE {key} = %s", (set_locale_value(value, "en", en), kid))
                db.commit()
        cur.close()
    print(f"\nусього: текстів {total_texts}, символів {total_chars}; DeepL витрачено {spent[0]}"
          + (" (нічого не записано - --dry-run)" if args.dry_run else ""))
    db.close()


if __name__ == "__main__":
    main()

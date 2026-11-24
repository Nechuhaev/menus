"""«Кухня» не тією мовою: російський текст в українському полі («Японская кухня») і навпаки - переклад
словником (поширені кухні й типи закладів), решта - DeepL; ліміт DeepL вичерпано - решта лишається як є.

Українська сторінка ресторану показувала кухню російською - у тексті, описі для Google і розмітці servesCuisine.
Назв кухонь небагато, тож кожна окрема назва перекладається один раз, а результат ставиться всім ресторанам.

Запуск:  python3 fix_cuisine_lang.py             # показати, що зміниться (переклад DeepL - так, запис у базу - ні)
         python3 fix_cuisine_lang.py --apply     # записати (попереднє значення - в CuisineLangLog)
         python3 fix_cuisine_lang.py --restore   # повернути все, що записав скрипт
"""
import argparse
import json
import os
import re
from collections import Counter
from datetime import datetime

import requests

from sync_restaurants import get_db

# російські слова в «Кухні» українською: літери, закінчення (-ская, -ная, -ия, -яя) і типи закладів без таких ознак
# («Пиццерия», «Кофейня», «Суши», «Бистро»)
RU_ONLY = re.compile(r"[ыэёъ]|ск(ая|ое|ие|ий)\b|\bкухни\b|(н|ов)ая\b|[^і]ия\b|яя\b|\bсуши\b|\bкофейн|\bбистро\b|ицц", re.I)
UK_ONLY = re.compile(r"[іїєґ]|ськ", re.I)
LOG_TABLE_SQL = """CREATE TABLE IF NOT EXISTS CuisineLangLog (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    OldCuisine TEXT NULL,
    CreatedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


# поширені кухні й типи закладів: перекладаються кодом, DeepL - лише для решти (і коли його ліміт вичерпано)
RU_UK = {
    "пиццерия": "Піцерія", "кофейня": "Кав'ярня", "кафе": "Кафе", "суши": "Суші", "суши-бар": "Суші-бар",
    "бистро": "Бістро", "закусочная": "Закусочна", "столовая": "Їдальня", "пельменная": "Пельменна",
    "чебуречная": "Чебуречна", "шашлычная": "Шашлична", "блинная": "Млинцева", "кондитерская": "Кондитерська",
    "пекарня": "Пекарня", "бар": "Бар", "паб": "Паб", "ресторан": "Ресторан", "гриль": "Гриль", "фастфуд": "Фастфуд",
    "европейская": "Європейська", "итальянская": "Італійська", "японская": "Японська", "грузинская": "Грузинська",
    "украинская": "Українська", "американская": "Американська", "паназиатская": "Паназійська", "азиатская": "Азійська",
    "китайская": "Китайська", "французская": "Французька", "мексиканская": "Мексиканська", "кавказская": "Кавказька",
    "восточная": "Східна", "средиземноморская": "Середземноморська", "домашняя": "Домашня", "авторская": "Авторська",
    "вегетарианская": "Вегетаріанська", "мясная": "М'ясна", "рыбная": "Рибна", "морепродукты": "Морепродукти",
    "тайская": "Тайська", "корейская": "Корейська", "индийская": "Індійська", "турецкая": "Турецька",
    "узбекская": "Узбецька", "армянская": "Вірменська", "греческая": "Грецька", "испанская": "Іспанська",
    "немецкая": "Німецька", "чешская": "Чеська", "вьетнамская": "В'єтнамська", "израильская": "Ізраїльська",
    "ближневосточная": "Близькосхідна", "международная": "Міжнародна", "интернациональная": "Інтернаціональна",
    "веганская": "Веганська", "здоровая еда": "Здорова їжа", "стейк-хаус": "Стейк-хаус", "бургерная": "Бургерна",
    "кухня": "Кухня", "пицерия": "Піцерія", "пицца": "Піца", "кальянная": "Кальянна", "кальян": "Кальян",
    "кофейня-пекарня": "Кав'ярня-пекарня", "салат-бар": "Салат-бар", "ресторан здорового питания": "Ресторан здорового харчування",
    "украинский": "Український", "хинкальная": "Хінкальня", "вареничная": "Варенична", "рюмочная": "Чаркова",
}
UK_RU = {v.lower(): k[:1].upper() + k[1:] for k, v in RU_UK.items() if k not in ("пицерия",)}  # «Піцерія» -> «Пиццерия»


def by_dictionary(text, source):
    """Переклад кодом: ціла назва або «Японская кухня» - слово за словом; None - якщо щось не знайдено."""
    table = RU_UK if source == "ru" else UK_RU
    key = text.strip().lower()
    if key in table:
        return table[key]
    if ";" in key:  # «Ресторан здорового питания; салат-бар»
        pieces = [by_dictionary(x, source) for x in text.split(";")]
        return "; ".join(pieces) if all(pieces) else None
    words = key.split()
    if len(words) > 1 and all(w in table for w in words):
        out = " ".join(table[w].lower() for w in words)
        return out[:1].upper() + out[1:]
    return None


def deepl(texts, source, target):
    """Спершу словник; решта - DeepL. Ліміт DeepL вичерпано (456) - решта лишається як є (повідомлення)."""
    result = {}
    for t in texts:
        found = by_dictionary(t, source)
        if found:
            result[t] = found
    texts = [t for t in texts if t not in result]
    if not texts:
        return result
    try:
        result.update(_deepl(texts, source, target))
    except requests.RequestException as e:
        code = getattr(getattr(e, "response", None), "status_code", None)
        print(f"! DeepL {code}: {'ліміт символів вичерпано' if code == 456 else e} - без перекладу лишаються: {', '.join(texts)}")
    return result


def _deepl(texts, source, target):
    key = os.environ["DEEPL_API_KEY"]
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    r = requests.post(url + "/v2/translate", timeout=60, headers={"Authorization": "DeepL-Auth-Key " + key},
                      json={"text": texts, "source_lang": source.upper(), "target_lang": target.upper(),
                            "context": "Кухня ресторану (італійська, грузинська, японська...)"})
    r.raise_for_status()
    out = [t["text"].strip() for t in r.json()["translations"]]
    return {src: dst[:1].upper() + dst[1:] for src, dst in zip(texts, out)}


def parts(value):
    return [p.strip() for p in (value or "").split(",") if p.strip()]


def wrong(part, lang):
    return bool(RU_ONLY.search(part)) if lang == "uk" else bool(UK_ONLY.search(part))


def restore(db):
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    cur.execute("SELECT RestaurantId, OldCuisine FROM CuisineLangLog")
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute("UPDATE RestaurantInfo SET Cuisine = %s WHERE RestaurantId = %s", (row["OldCuisine"], row["RestaurantId"]))
    write.execute("DELETE FROM CuisineLangLog")
    db.commit()
    print(f"повернено: {len(rows)}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="записати в базу")
    parser.add_argument("--restore", action="store_true", help="повернути значення, які були до --apply")
    args = parser.parse_args()

    db = get_db()
    if args.restore:
        restore(db)
        db.close()
        return

    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    cur.execute("""SELECT CONCAT(ri.RestaurantId, '') AS Id, ri.Cuisine FROM RestaurantInfo ri
                   JOIN Restaurants r ON r.Id = ri.RestaurantId WHERE r.Status = 0""")
    rows = []
    for row in cur.fetchall():
        try:
            data = json.loads(row["Cuisine"] or "{}")
        except json.JSONDecodeError:
            continue
        if isinstance(data, dict) and data.get("Values"):
            rows.append((row, data))

    # лише ті мовні значення, що потрібні: uk-поле з російськими назвами -> з російської, ru-поле з українськими -> з української
    need = {"uk": Counter(), "ru": Counter()}
    for _, data in rows:
        for v in data["Values"]:
            lang = v.get("Locale")
            if lang in need:
                need[lang].update(p for p in parts(v.get("Value")) if wrong(p, lang))
    translated = {"uk": deepl(sorted(need["uk"]), "ru", "uk"), "ru": deepl(sorted(need["ru"]), "uk", "ru")}
    for lang in ("uk", "ru"):
        for src, n in need[lang].most_common():
            print(f"  {lang}: {src} -> {translated[lang].get(src, '(без перекладу)')}  ({n})")

    changed = []
    for row, data in rows:
        touched = False
        for v in data["Values"]:
            lang = v.get("Locale")
            if lang not in translated:
                continue
            new_parts = [translated[lang].get(p, p) if wrong(p, lang) else p for p in parts(v.get("Value"))]
            new_value = ", ".join(dict.fromkeys(new_parts))  # без повторів після перекладу
            if new_value != (v.get("Value") or "").strip():
                v["Value"] = new_value
                touched = True
        if touched:
            changed.append((row, json.dumps(data, ensure_ascii=False)))

    print(f"\nресторанів з кухнею: {len(rows)}; змінити: {len(changed)}")
    if not args.apply:
        print("нічого не записано; запиши з --apply")
        db.close()
        return

    now = datetime.now()
    write = db.cursor()
    for row, value in changed:
        write.execute("""INSERT INTO CuisineLangLog (RestaurantId, OldCuisine, CreatedAt) VALUES (%s, %s, %s)
                         ON DUPLICATE KEY UPDATE CreatedAt = VALUES(CreatedAt)""", (row["Id"], row["Cuisine"], now))
        write.execute("UPDATE RestaurantInfo SET Cuisine = %s WHERE RestaurantId = %s", (value, row["Id"]))
    db.commit()
    db.close()
    print(f"записано: {len(changed)}. Сторінки оновляться з кешу Cloudflare протягом 30 хв.")


if __name__ == "__main__":
    main()

"""Старі SEO-заголовки ресторанів без міста - прибрати, щоб сторінка показала свій заголовок з даних меню.

Коли title у RestaurantSeo порожній або шаблонний, сторінка (Views/Home/Restaurant.cshtml, SeoText) будує його сама:
«Меню {Назва}, {місто}: ціни {рік} | TipMyMenu». Старі заголовки з бази («Чачапурі - грузинська кухня у місті»)
не містять ні міста, ні слова «меню» - а саме з ними шукають («чачапурі київ меню»). Скрипт очищає лише ті
заголовки, де немає назви міста (окремо uk і ru), а також title і description, де згадано ІНШЕ місто
(Егерзунд: київська сторінка з «морепродукти у Львові» - з 03.11 покази впали вчетверо). Кодом, без AI.
Кожна зміна - у журналі SeoTitleUpdate, відкат: --rollback.

Запуск:  python3 seo_titles.py                 # показати, що зміниться (нічого не пише)
         python3 seo_titles.py --apply         # записати
         python3 seo_titles.py --rollback      # повернути всі заголовки, змінені цим скриптом
"""
import argparse
import re
from collections import Counter
from datetime import date

from sync_restaurants import city_names, get_db
from translate_missing import get_locale_value, set_locale_value

LOG_SQL = """CREATE TABLE IF NOT EXISTS SeoTitleUpdate (
    ItemId VARCHAR(255) NOT NULL PRIMARY KEY,
    OldTitle TEXT NULL,
    NewTitle TEXT NOT NULL,
    OldDescription TEXT NULL,
    ChangedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


YEAR = date.today().year


def other_city(city):
    """Згадка іншого міста з початку слова в будь-якому відмінку: «Львові», «Харкова», «Києві», «Одесі»,
    «Львове», «Киеве». Прикметники («Одеська траса», «Дніпровський район») - не місто."""
    city = (city or "").strip().lower()
    if len(city) < 4:
        return None
    stems = {city, city[:-1] if city[-1] in "аеиіїоуяюь" else city}
    if city.endswith("ів"):
        stems.add(city[:-2] + "ов")   # Львів -> Львов-і, Харків -> Харков-а
    if city.endswith("їв"):
        stems.add(city[:-2] + "єв")   # Київ -> Києв-і
    alternatives = "|".join(re.escape(x) for x in sorted(stems, key=len, reverse=True))
    return re.compile(r"(?<![\w])(?:" + alternatives + r")(?:[аеиіїоуяюь]{0,2})(?![\w])", re.I)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="записати (без нього - лише показати)")
    parser.add_argument("--rollback", action="store_true", help="повернути заголовки до змін цим скриптом")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_SQL)
    write = db.cursor()
    if args.rollback:
        cur.execute("SELECT ItemId, OldTitle, OldDescription FROM SeoTitleUpdate")
        rows = cur.fetchall()
        for row in rows:
            write.execute("UPDATE RestaurantSeo SET Title = %s, Description = IFNULL(%s, Description) WHERE ItemId = %s",
                          (row["OldTitle"], row["OldDescription"], row["ItemId"]))
        write.execute("DELETE FROM SeoTitleUpdate")
        db.commit()
        print(f"повернуто заголовків: {len(rows)}")
        return

    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, r.Address, rs.Title, rs.Description
                   FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id WHERE r.Status = 0 ORDER BY r.Name""")
    restaurants = cur.fetchall()
    all_cities = {cid: (other_city(uk), other_city(ru)) for cid, (uk, ru) in
                  ((r["City"], city_names(db, r["City"])) for r in restaurants)}
    stats, shown = Counter(), 0
    for r in restaurants:
        city_uk, city_ru = city_names(db, r["City"])
        title, description = r["Title"], r["Description"]
        other = [p for cid, pats in all_cities.items() if str(cid) != str(r["City"]) for p in pats if p]
        changed = []
        for locale in ("uk", "ru"):
            text = get_locale_value(description, locale).strip()
            wrong = next((p.pattern for p in other if p.search(text)), None)
            if text and wrong:
                description = set_locale_value(description, locale, "")
                changed.append((f"{locale} опис", text, "(опис складе сторінка з меню: тут було інше місто)"))
        for locale, city in (("uk", city_uk), ("ru", city_ru)):
            old = get_locale_value(title, locale).strip()
            own = other_city(city)
            wrong = next((p for p in other if p.search(old)), None)
            if not own or not old or (own.search(old) and not wrong):
                continue
            # як складе сторінка (SeoTitleMenuYear у resx; якщо довше 60 символів - коротший варіант)
            new = f"Меню {r['Name']}, {city}: {'ціни' if locale == 'uk' else 'цены'} {YEAR} | TipMyMenu"
            title = set_locale_value(title, locale, "")
            changed.append((locale, old, new))
        if not changed:
            stats["з містом - без змін"] += 1
            continue
        stats["очистити (буде авто-заголовок)"] += 1
        if shown < 40:
            print(f"\n{r['Name']}")
            for locale, old, new in changed:
                print(f"   {locale}: {old or '—'}\n       -> {new}")
            shown += 1
        if args.apply:
            write.execute("""INSERT IGNORE INTO SeoTitleUpdate (ItemId, OldTitle, NewTitle, OldDescription, ChangedAt)
                             VALUES (%s, %s, %s, %s, NOW())""", (r["Id"], r["Title"], title, r["Description"]))
            write.execute("UPDATE RestaurantSeo SET Title = %s, Description = %s WHERE ItemId = %s",
                          (title, description, r["Id"]))
            write.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (r["Id"],))
    if args.apply:
        db.commit()
    print(f"\n{dict(stats)}" + ("" if shown < stats["очистити (буде авто-заголовок)"] else "") +
          ("\nзаписано (відкат: --rollback)" if args.apply else "\nнічого не записано - для запису --apply"))
    db.close()


if __name__ == "__main__":
    main()

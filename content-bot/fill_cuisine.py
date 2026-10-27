"""Поле «Кухня» для ресторанів, де воно порожнє, - з назв страв у меню, без AI.

Бот заповнює кухню (AI) лише з сайту ресторану; у закладів без сайту (лише меню Expirenza/ChoiceQR) і там, де
сайт її не назвав, поле порожнє. Без кухні коротший опис сторінки в Google, немає servesCuisine у розмітці,
гірше підбираються «схожі заклади». Скрипт рахує страви з характерними словами (хінкалі, суші, піца, плов...)
і ставить 1-2 кухні, що займають помітну частку меню. Назви кухонь не перекладаються: беруться ті, що вже
стоять у інших ресторанів (uk і ru), тож на сайті вони однакові.

Запуск:  python3 fill_cuisine.py              # лише показати, що буде записано
         python3 fill_cuisine.py --apply      # записати (попереднє значення - в CuisineFillLog)
         python3 fill_cuisine.py --restore    # повернути все, що записав скрипт
"""
import argparse
import json
import re
from collections import Counter
from datetime import datetime

from sync_restaurants import get_db, get_localizable_value, is_localizable_empty, localizable_json

MIN_DISHES = 8      # менше страв - замало, щоб судити про кухню
MIN_HITS = 3        # страв із ознаками кухні щонайменше
MIN_SHARE = 0.15    # і частка від усього меню
MAX_CUISINES = 2

# кухня: (як шукати її назву серед уже вживаних uk, ru) і основи слів у назвах страв (uk, ru, латиницею)
CUISINES = {
    "italian": (r"^італ", r"^итал",
                r"піц|пицц|pizz|спагет|spaghet|лазань|lasagn|різот|ризот|risott|карбонар|carbonar|тірамісу|тирамису|"
                r"tiramis|брускет|bruschet|равіол|равиол|ravio|фокач|focacc|капрезе|caprese|панна.?кот|panna.?cot|"
                r"пенне|penne|фетучин|феттучин|fettucc|тальятел|tagliat|ньокк|gnocch"),
    "japanese": (r"^япон", r"^япон",
                 r"суші|суши|sushi|сашим|sashim|нігір|нигир|nigir|гункан|gunkan|темпур|tempur|рамен|ramen|удон|udon|"
                 r"мiсо|місо|мисо|miso|едамаме|эдамаме|edamam|філадельфі|филадельфи|philadelph|каліфорні|калифорни|"
                 r"californ|\bмакі\b|\bмаки\b|\bmaki\b|урамак|uramak"),  # не «макіато»
    "georgian": (r"^груз", r"^груз",
                 r"хінкал|хинкал|khinkal|хачапур|khachapur|чахохб|сацив|пхалі|пхали|лобіо|лобио|аджапсандал|"
                 r"чкмерул|шкмерул|оджахур|харчо|чанах|чашушул|мцвад|мчад|ткемал|чурчхел"),
    "ukrainian": (r"^укр", r"^укр",
                  r"вареник|вареники|борщ|деруни|драник|деруны|голубц|голубці|банош|галушк|пампушк|кров.?янк|"
                  r"кровянк|сирник|сырник|узвар|холодец|холодець|капусняк|юшк"),
    "uzbek": (r"^узб", r"^узб",
              r"плов|лагман|манти|манты|самс|шурп|казан.?кебаб|норин|чучвар|ханум"),
    "mexican": (r"^мекс", r"^мекс",
                r"\bтако|taco|буріто|буррито|бурито|burrito|начос|nacho|кесаділ|кесадил|quesadil|гуакамоле|"
                r"guacamol|фахітас|фахитас|fajit|енчілад|энчилад|enchilad"),
    "american": (r"^америк", r"^америк",
                 r"бургер|burger|хот.?дог|hot.?dog|чізбургер|чизбургер|cheeseburg|нагетс|наггетс|nugget|"
                 r"чікен.?стріпс|стрипс|strips|коул.?слоу|coleslaw"),
    "asian": (r"^азі|^паназ|^пан.?азі", r"^ази|^паназ|^пан.?ази",
              r"\bвок\b|\bwok\b|пад.?тай|pad.?thai|том.?ям|tom.?yum|дім.?сам|дим.?сам|dim.?sum|баоз|\bбао\b|\bbao\b|"
              r"фо.?бо|\bpho\b|локшин|лапша|noodle|теріякі|терияки|teriyak|кімчі|кимчи|kimchi|спрінг.?рол|спринг.?ролл|"
              r"spring.?roll|гедза|гьоза|гёдза|gyoza"),
    "french": (r"^франц", r"^франц",
               r"круасан|круассан|croissant|кіш\b|киш\b|quiche|рататуй|ratatouil|фуа.?гра|foie|крем.?брюле|"
               r"creme.?brul|буйабес|bouillab|confit|еклер|эклер|eclair"),  # не «конфітюр», не «макарони»
}
# в українському полі буває російський текст («Японская кухня») - беремо лише справжні uk/ru форми
# одна кухня на назву: «Азіатська та тайська» не годиться
UK_FORM = re.compile(r"^[^ыэёъ,;/\s]*ськ[аі]?(\s+кухня)?$", re.I)
RU_FORM = re.compile(r"^[^іїєґ,;/\s]*ск(ая|ие)(\s+кухня)?$", re.I)
COMPILED = {k: (re.compile(uk, re.I), re.compile(ru, re.I), re.compile(words, re.I)) for k, (uk, ru, words) in CUISINES.items()}

LOG_TABLE_SQL = """CREATE TABLE IF NOT EXISTS CuisineFillLog (
    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
    OldCuisine TEXT NULL,
    NewCuisine TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL
) DEFAULT CHARSET=utf8mb4"""


def names(json_str):
    """Усі мовні варіанти назви страви одним рядком."""
    try:
        return " ".join(v.get("Value") or "" for v in json.loads(json_str or "{}").get("Values", []))
    except (json.JSONDecodeError, AttributeError):
        return json_str or ""


def labels_in_use(cur):
    """Назва кожної кухні так, як вона вже стоїть у ресторанів: найчастіший варіант uk і ru."""
    cur.execute("SELECT ri.Cuisine FROM RestaurantInfo ri JOIN Restaurants r ON r.Id = ri.RestaurantId WHERE r.Status = 0")
    used = {"uk": Counter(), "ru": Counter()}
    for row in cur.fetchall():
        for lang in used:
            for part in (get_localizable_value(row["Cuisine"], lang) or "").split(","):
                part = part.strip().rstrip(".")
                if part:
                    used[lang][part] += 1
    labels = {}
    for key, (uk_re, ru_re, _) in COMPILED.items():
        uk = next((name for name, _ in used["uk"].most_common() if uk_re.search(name) and UK_FORM.search(name)), None)
        ru = next((name for name, _ in used["ru"].most_common() if ru_re.search(name) and RU_FORM.search(name)), None)
        if uk and ru:
            labels[key] = (uk, ru)
    return labels


def guess(dish_names, restaurant_name):
    """[(кухня, кількість страв)] - від найпомітнішої; порожньо, якщо меню нічого певного не каже."""
    hits = Counter()
    for name in dish_names:
        for key, (_, _, words) in COMPILED.items():
            if words.search(name):
                hits[key] += 1
    for key, (_, _, words) in COMPILED.items():
        if words.search(restaurant_name or ""):
            hits[key] += MIN_HITS  # «Піцерія ...», «Sushi ...» - назва закладу каже сама
    need = max(MIN_HITS, MIN_SHARE * len(dish_names))
    return [(k, n) for k, n in hits.most_common() if n >= need][:MAX_CUISINES]


def restore(db):
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    cur.execute("SELECT RestaurantId, OldCuisine FROM CuisineFillLog")
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute("UPDATE RestaurantInfo SET Cuisine = %s WHERE RestaurantId = %s", (row["OldCuisine"], row["RestaurantId"]))
    write.execute("DELETE FROM CuisineFillLog")
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
    labels = labels_in_use(cur)
    missing = [k for k in CUISINES if k not in labels]
    print("назви кухонь з бази: " + "; ".join(f"{k}: {uk} / {ru}" for k, (uk, ru) in labels.items()))
    if missing:
        print("не вживаються на сайті (пропускаю): " + ", ".join(missing))

    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, ri.Cuisine, rs.Url
                   FROM Restaurants r JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   WHERE r.Status = 0""")
    empty = [r for r in cur.fetchall() if is_localizable_empty(r["Cuisine"])]
    cur.execute("SELECT CONCAT(RestaurantId, '') AS RestaurantId, Name FROM Dishes WHERE Status = 0")
    dishes = {}
    for row in cur.fetchall():
        dishes.setdefault(row["RestaurantId"], []).append(names(row["Name"]))

    planned, too_small, unclear = [], 0, 0
    for r in empty:
        menu = dishes.get(r["Id"], [])
        if len(menu) < MIN_DISHES:
            too_small += 1
            continue
        found = [(k, n) for k, n in guess(menu, r["Name"]) if k in labels]
        if not found:
            unclear += 1
            continue
        uk = ", ".join(labels[k][0] for k, _ in found)
        ru = ", ".join(labels[k][1] for k, _ in found)
        planned.append((r, uk, ru))
        print(f"  {r['Url'] or r['Id']}: {uk} / {ru}  ({', '.join(f'{k} {n}/{len(menu)}' for k, n in found)})")

    print(f"\nбез кухні: {len(empty)}; заповнити: {len(planned)}; меню менше {MIN_DISHES} страв: {too_small}; "
          f"кухня не очевидна: {unclear}")
    if not args.apply:
        print("нічого не записано; запиши з --apply")
        db.close()
        return

    now = datetime.now()
    write = db.cursor()
    for r, uk, ru in planned:
        value = localizable_json(uk, ru)
        write.execute("""INSERT INTO CuisineFillLog (RestaurantId, OldCuisine, NewCuisine, CreatedAt) VALUES (%s, %s, %s, %s)
                         ON DUPLICATE KEY UPDATE NewCuisine = VALUES(NewCuisine), CreatedAt = VALUES(CreatedAt)""",
                      (r["Id"], r["Cuisine"], value, now))
        write.execute("UPDATE RestaurantInfo SET Cuisine = %s WHERE RestaurantId = %s", (value, r["Id"]))
    db.commit()
    db.close()
    print(f"записано: {len(planned)}. Сторінки оновляться з кешу Cloudflare протягом 30 хв.")


if __name__ == "__main__":
    main()

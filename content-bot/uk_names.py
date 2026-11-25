"""Російські назви закладів на українських сторінках («Царское село», «Любимый Дядя») - пропозиція української назви.

Українська сторінка показує назву закладу (Restaurants.Name - одна на всі мови) у H1 і title; якщо вона російська,
у Google українською нас шукають іншими словами. Скрипт знаходить такі заклади кодом (ы/э/ъ/ё, закінчення -ая/-ое/-ый),
пропонує українську назву (словник поширених слів, решта - DeepL) і пише пропозиції в UkNameProposals.
Адмін підтверджує чи виправляє їх у «Звіти» -> «Українські назви»: назва йде в українську версію H1 (RestaurantSeo.Header),
звідки її бере і title. Сама назва закладу й російська сторінка не змінюються.

Заклади, де в українському H1 уже є українська назва, пропускаються. DeepL недоступний (ліміт, мережа) - пропозиція
без перекладу: адмін впише назву сам або скрипт допише її наступного разу.

Запуск:  python3 uk_names.py --dry-run   # показати, нічого не записувати
         python3 uk_names.py             # записати пропозиції
"""
import argparse
import json
import os
import re

import requests

from sync_restaurants import get_db

RUSSIAN = re.compile(r"[ыэъё]|\w(ая|ое|ый)\b", re.I)
MENU_PREFIX = re.compile(r"^(меню|menu)\s+((ресторану|кафе|бару|кав'ярні|піцерії|ресторана|бара|кофейни|пиццерии)\s+)?", re.I)
CLICKS_DAYS = 90

# поширені слова в назвах закладів - кодом; якщо всі слова назви тут або латиницею/цифрами - DeepL не потрібен
WORDS = {
    "кафе": "кафе", "ресторан": "ресторан", "бар": "бар", "пиццерия": "піцерія", "кофейня": "кав'ярня", "суши": "суші",
    "городское": "міське", "старое": "старе", "старый": "старий", "новый": "новий", "новое": "нове", "царское": "царське",
    "село": "село", "дом": "дім", "домашняя": "домашня", "кухня": "кухня", "итальянская": "італійська", "редакция": "редакція",
    "любимый": "улюблений", "дядя": "дядько", "белый": "білий", "черный": "чорний", "красный": "червоний", "золотой": "золотий",
    "морская": "морська", "морской": "морський", "вкусная": "смачна", "уютное": "затишне", "место": "місце", "хинкальная": "хінкальня",
    "чебуречная": "чебуречна", "пельменная": "пельменна", "вареничная": "варенична", "блинная": "млинцева", "шашлычная": "шашлична",
    "закусочная": "закусочна", "пекарня": "пекарня", "кондитерская": "кондитерська", "гриль": "гриль", "и": "і", "на": "на",
    "в": "в", "у": "у", "с": "з", "по": "по", "мясо": "м'ясо", "рыба": "риба", "вино": "вино", "пиво": "пиво", "чай": "чай",
}


def by_words(name):
    """Переклад словом за словом зі збереженням регістру; None - якщо якесь кириличне слово невідоме."""
    out = []
    for token in re.split(r"(\W+)", name):
        low = token.lower()
        if not token or not re.search(r"[а-яёіїєґ]", low):
            out.append(token)
        elif low in WORDS:
            word = WORDS[low]
            out.append(word[:1].upper() + word[1:] if token[:1].isupper() else word)
        else:
            return None
    return "".join(out)


def deepl(names):
    """{російська назва: українська}; помилка (ліміт 456, мережа) - порожній словник з повідомленням."""
    if not names:
        return {}
    key = os.environ.get("DEEPL_API_KEY")
    if not key:
        print("! DEEPL_API_KEY немає - решта без перекладу")
        return {}
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    try:
        r = requests.post(url + "/v2/translate", timeout=60, headers={"Authorization": "DeepL-Auth-Key " + key},
                          json={"text": names, "source_lang": "RU", "target_lang": "UK",
                                "context": "Назва ресторану чи кафе (власна назва закладу)"})
        r.raise_for_status()
    except requests.RequestException as e:
        code = getattr(getattr(e, "response", None), "status_code", None)
        print(f"! DeepL {code or e}: {'ліміт символів вичерпано' if code == 456 else 'недоступний'} - {len(names)} назв без перекладу")
        return {}
    return {src: t["text"].strip() for src, t in zip(names, r.json()["translations"])}


def uk_header(header_json):
    try:
        values = (json.loads(header_json or "{}") or {}).get("Values") or []
    except (json.JSONDecodeError, AttributeError):
        return ""
    return next((v.get("Value") or "" for v in values if isinstance(v, dict) and v.get("Locale") == "uk"), "")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("""CREATE TABLE IF NOT EXISTS UkNameProposals (
        RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY, Name VARCHAR(255) NOT NULL, Proposed VARCHAR(255) NULL,
        Source VARCHAR(32) NULL, Clicks INT NOT NULL DEFAULT 0, Status TINYINT NOT NULL DEFAULT 0, OldHeader TEXT NULL,
        CreatedAt DATETIME NOT NULL, DecidedAt DATETIME NULL) DEFAULT CHARSET=utf8mb4""")
    cur.execute("SELECT RestaurantId, Status FROM UkNameProposals")
    decided = {row["RestaurantId"] for row in cur.fetchall() if row["Status"] != 0}
    cur.execute("""SELECT LOWER(SUBSTRING_INDEX(SUBSTRING_INDEX(Page, '/restaurant/', -1), '/', 1)) AS Slug,
                          CAST(SUM(Clicks) AS SIGNED) AS Clicks
                   FROM SeoPageDays WHERE Day >= CURDATE() - INTERVAL %s DAY AND Page LIKE '%%/restaurant/%%' GROUP BY 1""",
                (CLICKS_DAYS,))
    clicks = {row["Slug"]: row["Clicks"] for row in cur.fetchall()}
    cur.execute("""SELECT CONCAT(r.Id, '') AS Id, r.Name, rs.Header, LOWER(rs.Url) AS Slug
                   FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id WHERE r.Status = 0""")
    found = []
    for r in cur.fetchall():
        name = (r["Name"] or "").strip()
        if r["Id"] in decided or not RUSSIAN.search(name):
            continue
        header = MENU_PREFIX.sub("", uk_header(r["Header"]).strip()).strip()
        if header and header.lower() != name.lower() and not RUSSIAN.search(header):
            continue  # в українському H1 уже є своя назва (її бере title)
        found.append((r, name, clicks.get(r["Slug"] or "", 0)))

    proposals = {name: by_words(name) for _, name, _ in found}
    translated = deepl(sorted({n for n, p in proposals.items() if not p}))
    rows = []
    for r, name, n in sorted(found, key=lambda x: -x[2]):
        proposed = proposals[name] or translated.get(name)
        source = "словник" if proposals[name] else ("DeepL" if proposed else None)
        if proposed and proposed.strip().lower() == name.lower():
            proposed, source = None, None
        rows.append((r["Id"], name[:255], (proposed or None) and proposed[:255], source, n))
        print(f"{n:>5} кл.  {name[:40]:<40} -> {proposed or '(без перекладу)'}")
    print(f"\nзакладів з російською назвою без української в H1: {len(rows)}")

    if args.dry_run:
        print("нічого не записано (--dry-run)")
    else:
        write = db.cursor()
        write.executemany("""INSERT INTO UkNameProposals (RestaurantId, Name, Proposed, Source, Clicks, Status, CreatedAt)
                             VALUES (%s, %s, %s, %s, %s, 0, NOW())
                             ON DUPLICATE KEY UPDATE Name = VALUES(Name), Clicks = VALUES(Clicks),
                                 Proposed = IF(Status = 0 AND Proposed IS NULL, VALUES(Proposed), Proposed),
                                 Source = IF(Status = 0 AND Source IS NULL, VALUES(Source), Source)""", rows)
        db.commit()
        print(f"записано: {len(rows)} (адмінка «Звіти» -> «Українські назви»)")
    db.close()


if __name__ == "__main__":
    main()

"""Чистить описи страв, які бот уже записав (нові описи бот чистить сам, menu_platforms.clean_description).

- HTML-теги (<p>...</p> з Expirenza/ChoiceQR) - прибираються, у всіх мовах, зокрема перекладах DeepL;
- вага замість опису («250/50 г», «0,5 л») - прибирається: вона й так показується окремо;
- опис не тією мовою (назва страви латиницею - опис «Україна» записано як en) - переноситься на мову
  тексту, якщо вона однозначна (sure_lang) і там порожньо; тоді DeepL (translate_missing.py) допише інші мови;
- нові пропозиції у «Звітах» - теги й вага замість опису.

Запуск:  python3 clean_descriptions.py --dry-run   # скільки й приклади, нічого не пише
         python3 clean_descriptions.py             # записати
"""
import argparse
import json
from collections import Counter

from menu_platforms import clean_description, sure_lang
from sync_restaurants import get_db

EXAMPLES = 3
LOCALES = ("uk", "ru", "en")


def clean_localizable(value):
    """(новий JSON або None - опис порожній, Counter змін) для локалізованого опису; зміна None - чистити нічого."""
    try:
        parsed = json.loads(value)
    except ValueError:
        parsed = {"Values": [{"Locale": "uk", "Value": value}]}
    if not isinstance(parsed, dict):
        return value, Counter()
    changes = Counter()
    texts = {}
    for item in parsed.get("Values") or []:
        raw = item.get("Value") or ""
        text = clean_description(raw)
        if raw.strip() and not text:
            changes["вага/порожній замість опису"] += 1
        elif text != raw.strip():
            changes["теги"] += 1
        if text:
            texts[item.get("Locale")] = text
    for locale, text in list(texts.items()):
        actual = sure_lang(text)
        if locale in LOCALES and actual and actual != locale and actual not in texts:
            texts[actual] = texts.pop(locale)
            changes["мова"] += 1
    if not changes:
        return value, changes
    if not texts:
        return None, changes
    return json.dumps({"Values": [{"Locale": l, "Value": t} for l, t in texts.items()]}, ensure_ascii=False), changes


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT CONCAT(Id, '') AS Id, Description FROM Dishes WHERE Description IS NOT NULL AND Description <> ''")
    dishes, totals, examples = [], Counter(), {}
    for row in cur.fetchall():
        after, changes = clean_localizable(row["Description"])
        if changes:
            dishes.append((row["Id"], after))
            totals.update(changes)
            for kind in changes:
                examples.setdefault(kind, [])
                if len(examples[kind]) < EXAMPLES:
                    examples[kind].append((row["Description"], after))

    cur.execute("SELECT CONCAT(Id, '') AS Id, Description FROM MenuSyncProposals WHERE Status = 'new' AND Description IS NOT NULL")
    proposals = [(row["Id"], clean_description(row["Description"]) or None) for row in cur.fetchall()
                 if (clean_description(row["Description"]) or None) != row["Description"]]

    print(f"страв зі зміненим описом: {len(dishes)}; змін: " + ", ".join(f"{k} {v}" for k, v in totals.items()))
    print(f"нових пропозицій у «Звітах»: {len(proposals)}")
    for kind, items in examples.items():
        print(f"\n{kind}:")
        for before, after in items:
            print(f"  було:  {before[:200]}\n  стало: {(after or 'порожньо')[:200]}")
    if args.dry_run:
        db.close()
        return

    write = db.cursor()
    write.executemany("UPDATE Dishes SET Description = %s WHERE Id = %s", [(after, i) for i, after in dishes])
    write.executemany("UPDATE MenuSyncProposals SET Description = %s WHERE Id = %s", [(after, i) for i, after in proposals])
    db.commit()
    db.close()
    print("\nзаписано")


if __name__ == "__main__":
    main()

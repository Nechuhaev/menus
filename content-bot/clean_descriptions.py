"""Прибирає HTML-теги з описів страв, які бот уже записав (<p>...</p> з Expirenza/ChoiceQR).

Нові описи бот чистить сам (menu_platforms.plain_text); цей скрипт - для вже записаних: опис страви
(усі мови, зокрема переклади DeepL) і нові пропозиції у «Звітах».

Запуск:  python3 clean_descriptions.py --dry-run   # скільки й приклади, нічого не пише
         python3 clean_descriptions.py             # записати
"""
import argparse
import json

from menu_platforms import plain_text
from sync_restaurants import get_db

TAGGED = "Description REGEXP '<[a-zA-Z/][^>]*>|&(amp|nbsp|quot|lt|gt);'"
EXAMPLES = 3


def clean_localizable(value):
    """Локалізований JSON ({"Values": [...]}) з очищеними значеннями; None - чистити нічого."""
    try:
        parsed = json.loads(value)
    except ValueError:
        cleaned = plain_text(value)
        return cleaned if cleaned != value else None
    if not isinstance(parsed, dict):
        return None
    changed = False
    for item in parsed.get("Values") or []:
        cleaned = plain_text(item.get("Value"))
        if item.get("Value") and cleaned != item["Value"]:
            item["Value"] = cleaned
            changed = True
    return json.dumps(parsed, ensure_ascii=False) if changed else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(f"SELECT CONCAT(Id, '') AS Id, Description FROM Dishes WHERE {TAGGED}")
    dishes = [(row["Id"], row["Description"], clean_localizable(row["Description"])) for row in cur.fetchall()]
    dishes = [d for d in dishes if d[2]]
    cur.execute(f"SELECT CONCAT(Id, '') AS Id, Description FROM MenuSyncProposals WHERE Status = 'new' AND {TAGGED}")
    proposals = [(row["Id"], row["Description"], plain_text(row["Description"])) for row in cur.fetchall()]
    proposals = [p for p in proposals if p[2] != p[1]]

    print(f"страв з тегами в описі: {len(dishes)}, нових пропозицій у «Звітах»: {len(proposals)}")
    for _, before, after in dishes[:EXAMPLES]:
        print(f"\n  було:  {before[:200]}\n  стало: {after[:200]}")
    if args.dry_run:
        db.close()
        return

    write = db.cursor()
    write.executemany("UPDATE Dishes SET Description = %s WHERE Id = %s", [(after, i) for i, _, after in dishes])
    write.executemany("UPDATE MenuSyncProposals SET Description = %s WHERE Id = %s",
                      [(after or None, i) for i, _, after in proposals])
    db.commit()
    db.close()
    print("\nзаписано")


if __name__ == "__main__":
    main()

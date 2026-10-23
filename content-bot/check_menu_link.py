"""Сухий прогін меню з Expirenza/ChoiceQR для одного ресторану: нічого не пише в базу.

Показує, скільки страв на сервісі, скільки збіглося з нашими, скільки наших отримали б
фото/опис/вагу і скільки нових страв бот запропонував би у «Звітах».

Запуск:  python3 check_menu_link.py "Musafir" https://expz.menu/d0838ea9-b9ae-44dd-b99d-993f0a0206fd
         python3 check_menu_link.py "Citronelle"          # посилання з адмінки (RestaurantMenuLinks)
         python3 check_menu_link.py "Citronelle" --unmatched   # + 20 наших страв без пари й найсхожіші назви з сервісу
"""
import difflib
import sys

from menu_platforms import platform_menu
from sync_restaurants import (MENU_LINKS_TABLE_SQL, RESTAURANT_COLUMNS, fetch_active_dishes, get_db,
                              dish_display_name, is_localizable_empty, match_key, match_menu, pair_by_stems)

UNMATCHED_SHOWN = 20


def closest(name, site_names):
    """Найсхожіша назва з сервісу і схожість 0..1 (за назвами без розмірів і дужок)."""
    key = match_key(name)
    scored = [(difflib.SequenceMatcher(None, key, match_key(s)).ratio(), s) for s in site_names]
    return max(scored) if scored else (0, "")


def main():
    show_unmatched = "--unmatched" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--unmatched"]
    if not args:
        print(__doc__)
        return
    name, url = args[0], args[1] if len(args) > 1 else None
    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(MENU_LINKS_TABLE_SQL)
    cur.execute(RESTAURANT_COLUMNS + " WHERE r.Name LIKE %s AND r.Status = 0", (f"%{name}%",))
    restaurants = cur.fetchall()
    if not restaurants:
        print(f"Активного ресторану «{name}» не знайдено")
        return

    for restaurant in restaurants:
        link = url or restaurant.get("MenuUrl")
        print(f"\n=== {restaurant['Name']} ({restaurant['Address']}) - {link}")
        if not link:
            print("  посилання на меню немає (вкажіть його в адмінці або другим аргументом)")
            continue
        site = platform_menu(link)
        if site is None:
            print("  адреса не з Expirenza/ChoiceQR")
            continue
        ours = fetch_active_dishes(db, restaurant["Id"])
        matches, unmatched = match_menu(ours, site, restaurant["Name"])
        paired = pair_by_stems([d for d in ours if d["Id"] not in matches], unmatched, restaurant["Name"])
        matches.update(paired)
        unmatched = [d for d in unmatched if all(d is not p for p in paired.values())]
        by_id = {d["Id"]: d for d in ours}

        def gains(field, ours_empty):
            return sum(1 for dish_id, s in matches.items() if s.get(field) and ours_empty(by_id[dish_id]))

        print(f"  на сервісі: {len(site)} страв, з фото {sum(1 for d in site if d['image_url'])}, "
              f"з описом {sum(1 for d in site if d['description'])}, розділів {len({d['category'] for d in site})}")
        print(f"  у нас: {len(ours)}, збіглося: {len(matches)}")
        print(f"  отримали б: фото {gains('image_url', lambda d: not d['PhotoLink'] or d['PhotoLink'] == '[]')}, "
              f"опис {gains('description', lambda d: is_localizable_empty(d['Description']))}, "
              f"вагу {gains('weight', lambda d: not d['Weight'])}")
        print(f"  нових страв (пропозиції у «Звітах»): {len(unmatched)}; наших немає на сервісі: {len(ours) - len(matches)}")
        for d in site[:5]:
            print(f"    {d['category']} | {d['name']} | {d['price']} | {d['weight']} | фото: {'так' if d['image_url'] else 'ні'}")
        if show_unmatched:
            # найсхожіші першими: висока схожість без пари - зіставлення пропустило ту саму страву
            names = [d["name"] for d in unmatched]
            rows = sorted(((*closest(dish_display_name(d), names), dish_display_name(d)) for d in ours
                           if d["Id"] not in matches), reverse=True)
            print(f"  наші без пари (найсхожіші з сервісу, {UNMATCHED_SHOWN} з {len(rows)}):")
            for score, site_name, our_name in rows[:UNMATCHED_SHOWN]:
                print(f"    {score:.2f}  {our_name}  ~  {site_name}")
    db.close()


if __name__ == "__main__":
    main()

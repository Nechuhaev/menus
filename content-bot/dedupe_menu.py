"""Прибирає дублі в меню ресторанів, які з'явились від подвійного застосування пропозицій у «Звітах».

- категорії: дві категорії ресторану з тією ж назвою під тим самим розділом - зливаються в одну (страви
  переносяться, порожня копія видаляється);
- той самий зв'язок страва-категорія двічі - зайвий видаляється (страва показувалась у категорії двічі);
- страви: та сама назва й ціна в тих самих категоріях - лишається одна (з оцінками, фото, описом, адресою
  без «-one»), копія видаляється, якщо в неї немає оцінок і фото; її адреса віддає 301 на ресторан.
  Копія з оцінками чи фото не видаляється - лише показується в списку для ручної перевірки.

Запуск:  python3 dedupe_menu.py --dry-run              # що зміниться, нічого не пише
         python3 dedupe_menu.py --restaurant titka-klara-811 --dry-run
         python3 dedupe_menu.py                        # записати
"""
import argparse
import json
import re
from collections import defaultdict

from sync_restaurants import get_db


def first_value(localizable):
    try:
        parsed = json.loads(localizable or "")
    except ValueError:
        return (localizable or "").strip()
    if not isinstance(parsed, dict):
        return ""
    for v in parsed.get("Values") or []:
        if (v.get("Value") or "").strip():
            return v["Value"].strip()
    return ""


def norm(value):
    return re.sub(r"[^\w]+", " ", (value or "").lower().replace("ё", "е")).strip()


def has_text(localizable):
    return bool(first_value(localizable))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--restaurant", help="адреса ресторану (RestaurantSeo.Url) - лише він")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    where, params = "", ()
    if args.restaurant:
        where, params = " AND r.Id = (SELECT ItemId FROM RestaurantSeo WHERE Url = %s)", (args.restaurant,)

    cur.execute("""SELECT CONCAT(c.Id, '') AS Id, CONCAT(c.RestaurantId, '') AS RestaurantId, c.Name, c.`Index`,
                          (SELECT CONCAT(MIN(rel.ParentId), '') FROM MenuCategoryRelations rel WHERE rel.ChildId = c.Id) AS ParentId
                   FROM MenuCategories c JOIN Restaurants r ON r.Id = c.RestaurantId
                   WHERE c.IsTopLevelCategory = 0""" + where, params)
    categories = cur.fetchall()
    cur.execute("""SELECT CONCAT(dc.Id, '') AS Id, CONCAT(dc.DishId, '') AS DishId, CONCAT(dc.MenuCategoryId, '') AS CategoryId
                   FROM DishCategories dc JOIN Dishes d ON d.Id = dc.DishId JOIN Restaurants r ON r.Id = d.RestaurantId
                   WHERE 1 = 1""" + where, params)
    links = cur.fetchall()
    cur.execute("""SELECT CONCAT(d.Id, '') AS Id, CONCAT(d.RestaurantId, '') AS RestaurantId, d.Name, d.Price, d.RatingsCount,
                          d.PhotoLink, d.Description, d.ShortDescription, ds.Url, rs.Url AS RestaurantUrl,
                          (SELECT COUNT(*) FROM DishPhotos p WHERE p.DishId = d.Id) AS Photos
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   LEFT JOIN DishSeo ds ON ds.ItemId = d.Id LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                   WHERE d.Status = 0""" + where, params)
    dishes = cur.fetchall()

    # 1. категорії з однаковою назвою під тим самим розділом ресторану -> перша за порядком
    #    («кава» в «Кухні» і «кава» в «Барі» - різні категорії, не зливаються)
    keep_category = {}
    groups = defaultdict(list)
    for c in categories:
        name = norm(first_value(c["Name"]))
        if name:
            groups[(c["RestaurantId"], c["ParentId"], name)].append(c)
    merged_categories = []
    for group in groups.values():
        group.sort(key=lambda c: (c["Index"], c["Id"]))
        for dup in group[1:]:
            keep_category[dup["Id"]] = group[0]["Id"]
            merged_categories.append((dup["Id"], group[0]["Id"]))

    # 2. зв'язки страва-категорія після злиття: повтори - зайві
    seen, link_moves, link_deletes = set(), [], []
    dish_categories = defaultdict(set)
    for link in links:
        target = keep_category.get(link["CategoryId"], link["CategoryId"])
        key = (link["DishId"], target)
        if key in seen:
            link_deletes.append(link["Id"])
            continue
        seen.add(key)
        dish_categories[link["DishId"]].add(target)
        if target != link["CategoryId"]:
            link_moves.append((target, link["Id"]))

    # 3. страви: назва + ціна + категорії
    def keep_rank(d):
        url = d["Url"] or ""
        has_photo = d["Photos"] or (d["PhotoLink"] and d["PhotoLink"] not in ("[]", ""))
        return (-(d["RatingsCount"] or 0), not has_photo, not (has_text(d["Description"]) or has_text(d["ShortDescription"])),
                bool(re.search(r"-one$|-\d+$", url)), not url, d["Id"])

    dish_groups = defaultdict(list)
    for d in dishes:
        name = norm(first_value(d["Name"]))
        if name:
            dish_groups[(d["RestaurantId"], name, float(d["Price"] or 0), frozenset(dish_categories[d["Id"]]))].append(d)
    to_delete, to_check = [], []
    for group in dish_groups.values():
        if len(group) < 2:
            continue
        group.sort(key=keep_rank)
        for dup in group[1:]:
            has_photo = dup["Photos"] or (dup["PhotoLink"] and dup["PhotoLink"] not in ("[]", ""))
            (to_check if (dup["RatingsCount"] or has_photo) else to_delete).append((dup, group[0]))

    restaurants = {d["RestaurantId"] for d, _ in to_delete} | {k[0] for k, g in groups.items() if len(g) > 1}
    print(f"категорій-дублів: {len(merged_categories)}; зв'язків перенести: {len(link_moves)}, зайвих зв'язків: {len(link_deletes)}")
    print(f"страв-дублів видалити: {len(to_delete)}; з оцінками/фото (лишаються, перевірити вручну): {len(to_check)}; "
          f"ресторанів зачеплено: {len(restaurants)}")
    for dup, keep in to_delete[:10]:
        print(f"  {dup['RestaurantUrl']}: {first_value(dup['Name'])} - видалити /{dup['Url']}, лишити /{keep['Url']}")
    for dup, keep in to_check[:20]:
        print(f"  перевірити: {dup['RestaurantUrl']}/{dup['Url']} (дубль /{keep['Url']}, оцінок {dup['RatingsCount']})")
    if args.dry_run:
        db.close()
        return

    write = db.cursor()
    write.executemany("UPDATE DishCategories SET MenuCategoryId = %s WHERE Id = %s", link_moves)
    write.executemany("DELETE FROM DishCategories WHERE Id = %s", [(i,) for i in link_deletes])
    for dup_id, _ in merged_categories:
        write.execute("DELETE FROM MenuCategoryRelations WHERE ChildId = %s", (dup_id,))
        write.execute("DELETE FROM MenuCategories WHERE Id = %s AND NOT EXISTS "
                      "(SELECT 1 FROM DishCategories WHERE MenuCategoryId = %s)", (dup_id, dup_id))
    db.commit()
    deleted = 0
    for dup, _ in to_delete:
        try:
            for table, column in (("DishCategories", "DishId"), ("DishVariations", "DishId"), ("DishSeo", "ItemId")):
                write.execute(f"DELETE FROM {table} WHERE {column} = %s", (dup["Id"],))
            write.execute("DELETE FROM Dishes WHERE Id = %s", (dup["Id"],))
            db.commit()
            deleted += 1
        except Exception as e:  # пов'язані дані, про які скрипт не знає - страву лише ховаємо
            db.rollback()
            write.execute("UPDATE Dishes SET Status = 1 WHERE Id = %s", (dup["Id"],))
            db.commit()
            print(f"  {dup['Id']}: не видалено ({e}), приховано")
    db.close()
    print(f"\nвидалено страв: {deleted}. Перегенеруй кеш ресторанів і sitemap.")


if __name__ == "__main__":
    main()

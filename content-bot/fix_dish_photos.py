"""Прибирає зі страв фото, які насправді не фото страви, а загальне фото закладу: боти (sync_restaurants,
enrich_dishes) брали og:image сторінки чи картинку шапки сайту, однакову для всіх страв.

Фото страви вважається загальним, якщо в межах ресторану:
- те саме зображення стоїть на 3+ стравах (порівнюється вміст, а не адреса: бот завантажував ту саму
  картинку щоразу під новою адресою), або
- воно збігається з фото самого ресторану (ri.Photos).
Порівняння точне - за вмістом файлу (MD5): бот завантажував ту саму картинку тим самим способом, тож копії
однакові байт у байт. Схожі, але різні фото (страва по центру білої тарілки) не склеюються.

Файли в сховищі не видаляються; старе значення PhotoLink пишеться в DishPhotoCleanupLog -
--restore повертає. Без --dry-run лише записує.

Запуск:  python3 fix_dish_photos.py --dry-run                     # скільки й посилання на приклади
         python3 fix_dish_photos.py --dry-run --restaurant pastamore
         python3 fix_dish_photos.py                               # прибрати
         python3 fix_dish_photos.py --restore all                 # повернути все прибране
"""
import argparse
import hashlib
import json
import os
import re
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime

import requests

from sync_restaurants import get_db

SAME_PHOTO_DISHES = 3
WORKERS = 16
GUID = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", re.I)
SITE_ENV = "/root/menus.env"
CACHE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dish_photo_md5.json")


def media_base():
    """Публічна адреса медіа, з якої сайт показує фото (Media__BaseUrl сайту); фото беремо звідти ж - без ключів сховища."""
    value = os.environ.get("Media__BaseUrl")
    if not value and os.path.exists(SITE_ENV):
        for line in open(SITE_ENV, encoding="utf-8"):
            if line.startswith("Media__BaseUrl="):
                value = line.split("=", 1)[1].strip()
    return (value or "https://cdn-tipmymenus-com.fra1.digitaloceanspaces.com").rstrip("/")


MEDIA_BASE = media_base()
LOG_TABLE_SQL = """CREATE TABLE IF NOT EXISTS DishPhotoCleanupLog (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    DishId VARCHAR(255) NOT NULL,
    OldPhotoLink TEXT NOT NULL,
    Reason VARCHAR(255) NOT NULL,
    CreatedAt DATETIME NOT NULL,
    KEY IX_DishPhotoCleanupLog_Dish (DishId)
) DEFAULT CHARSET=utf8mb4"""

def photo_ids(value):
    """Ідентифікатори фото з PhotoLink/Photos (JSON-список або одне значення, як Dish.Photos на сайті)."""
    try:
        items = json.loads(value or "[]")
    except ValueError:
        items = [value]
    if isinstance(items, str):
        items = [items]
    return [m.group(0).lower() for m in (GUID.search(str(i or "")) for i in items or []) if m]


def fingerprint(photo_id):
    """MD5 вмісту зменшеної копії фото; None - файла немає."""
    for name in (f"{photo_id}-small.jpg", f"{photo_id}.jpg"):
        try:
            r = requests.get(f"{MEDIA_BASE}/files/{name}", timeout=20)
            r.raise_for_status()
            return hashlib.md5(r.content).hexdigest()
        except Exception:
            continue
    return None


def restore(db, target):
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    where, params = "", ()
    if target != "all":
        where = " AND d.RestaurantId = (SELECT ItemId FROM RestaurantSeo WHERE Url = %s)"
        params = (target,)
    cur.execute("""SELECT l.DishId, l.OldPhotoLink FROM DishPhotoCleanupLog l
                   JOIN (SELECT DishId, MIN(Id) AS Id FROM DishPhotoCleanupLog GROUP BY DishId) f ON f.Id = l.Id
                   JOIN Dishes d ON d.Id = l.DishId WHERE 1 = 1""" + where, params)
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute("UPDATE Dishes SET PhotoLink = %s WHERE Id = %s", (row["OldPhotoLink"], row["DishId"]))
        write.execute("DELETE FROM DishPhotoCleanupLog WHERE DishId = %s", (row["DishId"],))
    db.commit()
    print(f"повернено фото страв: {len(rows)}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--restaurant", help="адреса ресторану (RestaurantSeo.Url) - лише він")
    parser.add_argument("--restore", help="адреса ресторану або all - повернути прибрані фото")
    args = parser.parse_args()

    db = get_db()
    if args.restore:
        restore(db, args.restore)
        db.close()
        return
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_TABLE_SQL)
    where, params = "", ()
    if args.restaurant:
        where, params = " AND r.Id = (SELECT ItemId FROM RestaurantSeo WHERE Url = %s)", (args.restaurant,)
    cur.execute("""SELECT CONCAT(d.Id, '') AS Id, CONCAT(d.RestaurantId, '') AS RestaurantId, d.Name, d.PhotoLink,
                          rs.Url AS RestaurantUrl, ds.Url AS DishUrl
                   FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                   LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id LEFT JOIN DishSeo ds ON ds.ItemId = d.Id
                   WHERE d.PhotoLink IS NOT NULL AND d.PhotoLink NOT IN ('', '[]')""" + where, params)
    dishes = [d for d in cur.fetchall() if photo_ids(d["PhotoLink"])]
    restaurant_ids = {d["RestaurantId"] for d in dishes}
    cur.execute("SELECT CONCAT(RestaurantId, '') AS RestaurantId, Photos FROM RestaurantInfo")
    restaurant_photos = {r["RestaurantId"]: photo_ids(r["Photos"]) for r in cur.fetchall() if r["RestaurantId"] in restaurant_ids}

    # відбиток першого (головного) фото страви і всіх фото ресторану
    ids = {photo_ids(d["PhotoLink"])[0] for d in dishes} | {i for ps in restaurant_photos.values() for i in ps}
    print(f"фото з {MEDIA_BASE}")
    print(f"страв із фото: {len(dishes)}; ресторанів: {len(restaurant_ids)}; файлів перевірити: {len(ids)}")
    # відбиток файлу не змінюється - зберігаються між запусками, повторний запуск не качає все заново
    try:
        cache = json.load(open(CACHE_FILE, encoding="utf-8"))
    except (OSError, ValueError):
        cache = {}
    todo = sorted(i for i in ids if i not in cache)
    print(f"уже перевірено раніше: {len(ids) - len(todo)}; завантажити: {len(todo)}", flush=True)
    with ThreadPoolExecutor(WORKERS) as pool:
        for n, (photo_id, fp) in enumerate(zip(todo, pool.map(fingerprint, todo)), 1):
            if fp:
                cache[photo_id] = fp
            if n % 500 == 0 or n == len(todo):
                print(f"  завантажено {n}/{len(todo)}", flush=True)
                json.dump(cache, open(CACHE_FILE, "w", encoding="utf-8"))
    prints = {i: cache.get(i) for i in ids}

    by_restaurant = defaultdict(list)
    for d in dishes:
        fp = prints.get(photo_ids(d["PhotoLink"])[0])
        if fp:
            by_restaurant[d["RestaurantId"]].append((fp, d))
    to_clear = []
    for rid, items in by_restaurant.items():
        own = [prints.get(i) for i in restaurant_photos.get(rid, []) if prints.get(i)]
        groups = defaultdict(list)
        for fp, d in items:
            groups[fp].append(d)
        for fp, group in groups.items():
            if fp in own:
                to_clear += [(d, "фото ресторану") for d in group]
            elif len(group) >= SAME_PHOTO_DISHES:
                to_clear += [(d, f"те саме фото на {len(group)} стравах") for d in group]

    missing = sum(1 for i in ids if prints[i] is None)
    print(f"файлів не знайдено (пропущено): {missing}")
    print(f"прибрати фото зі страв: {len(to_clear)}; ресторанів: {len({d['RestaurantId'] for d, _ in to_clear})}")
    shown = defaultdict(int)
    for d, reason in to_clear:
        if shown[d["RestaurantUrl"]] < 2:
            shown[d["RestaurantUrl"]] += 1
            print(f"  {d['RestaurantUrl']}/{d['DishUrl']}: {reason} - {MEDIA_BASE}/files/{photo_ids(d['PhotoLink'])[0]}-small.jpg")
    if args.dry_run:
        db.close()
        return

    now = datetime.utcnow()
    write = db.cursor()
    for d, reason in to_clear:
        write.execute("INSERT INTO DishPhotoCleanupLog (DishId, OldPhotoLink, Reason, CreatedAt) VALUES (%s, %s, %s, %s)",
                      (d["Id"], d["PhotoLink"], reason, now))
        write.execute("UPDATE Dishes SET PhotoLink = '[]' WHERE Id = %s", (d["Id"],))
    db.commit()
    db.close()
    print("\nзаписано. Сторінки оновляться, коли спливе кеш Cloudflare (до 30 хв).")


if __name__ == "__main__":
    main()

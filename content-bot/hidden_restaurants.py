"""Ресторани, приховані з сайту (Pending/Closed), і повернення їх на сайт.

До 22.10.2026 бот сам ставив Closed (AI-пошук «схоже закрито», мертвий сайт), потім - Pending. Сторінка
прихованого ресторану віддає 404, і Google викидає її з індексу: у вересні-жовтні так зникли сторінки, що мали
тисячі показів (Search Console). Скрипт показує приховані ресторани з причиною з журналу бота й кодом відповіді
сторінки зараз, а --restore повертає вибрані на сайт (Status = 0). Повернене пишеться в RestaurantRestoreLog:
бот більше не ховає такий ресторан сам (лише адмін), --undo повертає попередній статус.

Запуск:  python3 hidden_restaurants.py                                   # усі приховані ботом
         python3 hidden_restaurants.py --slugs very-well-cafe-sykorskoho,titka-klara-811
         python3 hidden_restaurants.py --slugs-file lost.txt               # адреси з файлу, по одній у рядку
         python3 hidden_restaurants.py --slugs ... --restore             # повернути ці на сайт
         python3 hidden_restaurants.py --undo very-well-cafe-sykorskoho  # сховати назад (попередній статус)
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime

import requests

from sync_restaurants import get_db, get_localizable_value

STATUS = {0: "Active", 1: "Pending", 2: "Blocked", 3: "Closed"}
SITE = "https://tipmymenu.com"
RESTORE_TABLE_SQL = """CREATE TABLE IF NOT EXISTS RestaurantRestoreLog (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    OldStatus INT NOT NULL,
    CreatedAt DATETIME NOT NULL,
    KEY IX_RestaurantRestoreLog_Restaurant (RestaurantId)
) DEFAULT CHARSET=utf8mb4"""


def page_status(url):
    try:
        return requests.get(url, timeout=30, allow_redirects=False, headers={"User-Agent": "TipMyMenuBot/1.0"}).status_code
    except requests.RequestException as e:
        return type(e).__name__


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slugs", help="адреси ресторанів (RestaurantSeo.Url) через кому; без них - усі приховані ботом")
    parser.add_argument("--slugs-file", help="файл з адресами ресторанів, по одній у рядку (для довгого списку)")
    parser.add_argument("--restore", action="store_true", help="повернути на сайт (Status = 0) показані приховані")
    parser.add_argument("--undo", help="адреса ресторану - повернути статус, який був до --restore")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(RESTORE_TABLE_SQL)
    if args.undo:
        cur.execute("""SELECT l.RestaurantId, l.OldStatus FROM RestaurantRestoreLog l
                       JOIN RestaurantSeo rs ON rs.ItemId = l.RestaurantId WHERE rs.Url = %s ORDER BY l.Id DESC LIMIT 1""", (args.undo,))
        row = cur.fetchone()
        if not row:
            print("у журналі повернень цього ресторану немає")
        else:
            write = db.cursor()
            write.execute("UPDATE Restaurants SET Status = %s WHERE Id = %s", (row["OldStatus"], row["RestaurantId"]))
            write.execute("DELETE FROM RestaurantRestoreLog WHERE RestaurantId = %s", (row["RestaurantId"],))
            db.commit()
            print(f"статус повернено: {STATUS.get(row['OldStatus'], row['OldStatus'])}")
        db.close()
        return

    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: (get_localizable_value(row["Name"], "en") or get_localizable_value(row["Name"], "uk")).lower()
              for row in cur.fetchall()}
    sql = """SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, r.Status, rs.Url, l.LastCheckedAt, l.Notes, l.ClosedDetected,
                    (SELECT COUNT(*) FROM Dishes d WHERE d.RestaurantId = r.Id AND d.Status = 0) AS Dishes
             FROM Restaurants r JOIN RestaurantSeo rs ON rs.ItemId = r.Id
             LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id"""
    if args.slugs_file:
        args.slugs = ",".join(open(args.slugs_file, encoding="utf-8").read().split())
    if args.slugs:
        slugs = [s.strip() for s in args.slugs.split(",") if s.strip()]
        cur.execute(sql + " WHERE rs.Url IN (" + ",".join(["%s"] * len(slugs)) + ")", slugs)
    else:
        cur.execute(sql + " WHERE r.Status IN (1, 3) AND l.ClosedDetected = 1 ORDER BY l.LastCheckedAt DESC")
    rows = cur.fetchall()
    found = {r["Url"] for r in rows}
    if args.slugs:
        missing = [s for s in slugs if s not in found]
        deleted = set()
        if missing:
            # SEO-запис лишився, а ресторану немає - ресторан видалено з бази (адмінка чи скрипт)
            cur.execute("SELECT rs.Url FROM RestaurantSeo rs LEFT JOIN Restaurants r ON r.Id = rs.ItemId WHERE r.Id IS NULL AND rs.Url IN ("
                        + ",".join(["%s"] * len(missing)) + ")", missing)
            deleted = {row["Url"] for row in cur.fetchall()}
        for s in missing:
            print(f"  {s}: " + ("ресторан видалено з бази (лишився лише SEO-запис)" if s in deleted else "адреси немає в базі"))
        print(f"видалено з бази: {len(deleted)}; адреси немає: {len(missing) - len(deleted)}")

    urls = [f"{SITE}/uk/{cities.get(r['City'], '')}/restaurant/{r['Url']}" for r in rows]
    with ThreadPoolExecutor(8) as pool:
        codes = list(pool.map(page_status, urls))

    hidden = []
    for r, code in zip(rows, codes):
        state = STATUS.get(r["Status"], r["Status"])
        note = (r["Notes"] or "").replace("\n", " ")[:140]
        checked = r["LastCheckedAt"].strftime("%d.%m") if r["LastCheckedAt"] else "-"
        print(f"  {r['Url']}: {state}, сторінка {code}, страв {r['Dishes']}; бот {checked}: {note}")
        if r["Status"] in (1, 3):
            hidden.append(r)
    print(f"\nпоказано: {len(rows)}; приховано (Pending/Closed): {len(hidden)}; "
          f"активні, але сторінка не 200: {sum(1 for r, c in zip(rows, codes) if r['Status'] == 0 and c != 200)}")
    if not args.restore:
        db.close()
        return

    now = datetime.utcnow()
    write = db.cursor()
    for r in hidden:
        write.execute("INSERT INTO RestaurantRestoreLog (RestaurantId, OldStatus, CreatedAt) VALUES (%s, %s, %s)",
                      (r["Id"], r["Status"], now))
        write.execute("UPDATE Restaurants SET Status = 0 WHERE Id = %s", (r["Id"],))
    db.commit()
    db.close()
    print(f"повернено на сайт: {len(hidden)}. Перегенеруй sitemap (sitemap/runupdatejob).")


if __name__ == "__main__":
    main()

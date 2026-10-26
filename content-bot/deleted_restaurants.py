"""Ресторани, видалені з бази, від яких лишився лише SEO-запис (адреса сторінки, заголовок, опис).

Сторінки таких ресторанів віддають 404. Якщо в мережі є активна філія (спільний початок адреси), сайт віддає
301 на неї (DeletedRestaurantRedirectService); решту можна завести заново. Скрипт лише показує, хто куди
потрапляє, нічого не пише.

Запуск:  python3 deleted_restaurants.py                          # усі видалені
         python3 deleted_restaurants.py --slugs-file /root/lost.txt
"""
import argparse

from sync_restaurants import get_db, get_localizable_value

# має збігатися з DeletedRestaurantRedirectService.GenericWords / MinPrefixLength
GENERIC_WORDS = {"the", "cafe", "kafe", "kafe-bar", "bar", "pub", "pab", "restoran", "restaurant", "restoratsiia",
                 "restoratsyia", "pizza", "pitstsa", "sushi", "grill", "hryl", "bistro", "kava", "coffee", "kofe",
                 "bufet", "pyvnaia", "pyvna"}
MIN_PREFIX = 5


def sibling(slug, active):
    """Активна філія мережі (адреса, місто) або None - як на сайті, але без прив'язки до міста."""
    words = slug.lower().split("-")
    for n in range(len(words) - 1, 0, -1):
        prefix = "-".join(words[:n])
        if len(prefix) < MIN_PREFIX or (n == 1 and prefix in GENERIC_WORDS):
            continue
        hits = sorted((u for u in active if u.startswith(prefix + "-")), key=lambda u: (len(u), u))
        if hits:
            return hits[0], active[hits[0]]
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--slugs-file", help="файл з адресами, по одній у рядку; без нього - усі видалені")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute("SELECT Id, Name FROM Cities")
    cities = {row["Id"]: get_localizable_value(row["Name"], "uk") for row in cur.fetchall()}
    cur.execute("""SELECT rs.Url, r.City FROM RestaurantSeo rs JOIN Restaurants r ON r.Id = rs.ItemId WHERE r.Status = 0""")
    active = {row["Url"]: cities.get(row["City"], "") for row in cur.fetchall()}
    cur.execute("""SELECT rs.Url, rs.Title FROM RestaurantSeo rs LEFT JOIN Restaurants r ON r.Id = rs.ItemId
                   WHERE r.Id IS NULL ORDER BY rs.Url""")
    deleted = {row["Url"]: row["Title"] for row in cur.fetchall()}
    db.close()

    slugs = sorted(deleted)
    if args.slugs_file:
        wanted = open(args.slugs_file, encoding="utf-8").read().split()
        slugs = [s for s in wanted if s in deleted]

    redirected, alone = [], []
    for slug in slugs:
        title = get_localizable_value(deleted[slug], "uk") or get_localizable_value(deleted[slug], "ru") or ""
        hit = sibling(slug, active)
        (redirected if hit else alone).append((slug, title[:90], hit))
    print(f"видалених: {len(slugs)}; 301 на філію мережі: {len(redirected)}; без філії (завести заново або лишити 404): {len(alone)}")
    print("\n301 на філію (місто філії - перевір, що це те саме місто):")
    for slug, title, (target, city) in redirected:
        print(f"  {slug} -> {target} ({city}) | {title}")
    print("\nбез філії:")
    for slug, title, _ in alone:
        print(f"  {slug} | {title}")


if __name__ == "__main__":
    main()

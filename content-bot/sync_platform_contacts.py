"""Години роботи, телефон, сайт та email ресторану з його QR-меню (Expirenza, ChoiceQR). Кодом, без AI.

Звідки:
- Expirenza: https://static.shaketopay.com.ua/menu/prod/cache/menu/<id>/uk/restaurant_menu_info.json
  (той самий кеш, з якого беруться страви): settings - години по днях (dayOfWeek 1 = Пн ... 7 = Нд),
  contacts - телефони / email / сайт.
- ChoiceQR: головна сторінка закладу, __NEXT_DATA__ -> props.app.place:
  contactInfo (phone, email, website), workTimeAll - години по днях (dayOfWeek 0 = Нд ... 6 = Сб, як у JavaScript).

Що змінюється (лише якщо в QR-меню значення є і воно інше):
- RestaurantInfo.OpenHours - графік по днях {"perDay":true,"data":[7 днів з понеділка]};
- Restaurants.Phone - перший телефон (порівняння за останніми 9 цифрами, формат не чіпаємо, якщо номер той самий);
- RestaurantInfo.Website - якщо в QR-меню вказано сайт закладу (не соцмережу й не саме QR-меню);
- Restaurants.Email - лише якщо в нас порожньо (email може бути логіном власника).
Кожна зміна пишеться в PlatformContactSync зі старим значенням - відкат: --rollback <RestaurantId>.

Запуск:  python3 sync_platform_contacts.py            # показати, що змінилося б (нічого не пише)
         python3 sync_platform_contacts.py --apply    # записати
         python3 sync_platform_contacts.py --apply --only <RestaurantId>
         python3 sync_platform_contacts.py --rollback <RestaurantId>
"""
import argparse
import json
import re
from collections import Counter
from urllib.parse import urlparse

from menu_platforms import EXPZ_ROOT, fetch
from sync_restaurants import get_db

EXPZ_INFO = "https://static.shaketopay.com.ua/menu/prod/cache/menu/{id}/uk/restaurant_menu_info.json"
NOT_SITE = re.compile(r"choiceqr\.com|expz\.menu|expirenza|shaketopay|instagram\.com|facebook\.com|fb\.com|t\.me/|tiktok\.com|"
                      r"google\.|goo\.gl|maps\.app|linktr\.ee|taplink|wa\.me|viber", re.I)
EMAIL = re.compile(r"^[^@\s]+@[^@\s]+\.[a-z]{2,}$", re.I)

LOG_SQL = """CREATE TABLE IF NOT EXISTS PlatformContactSync (
    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    RestaurantId VARCHAR(255) NOT NULL,
    Field VARCHAR(32) NOT NULL,
    OldValue TEXT NULL,
    NewValue TEXT NULL,
    Source VARCHAR(255) NULL,
    ChangedAt DATETIME NOT NULL,
    KEY (RestaurantId)
) DEFAULT CHARSET=utf8mb4"""


def clock(value):
    """'12:00', '12:00:00.000' -> '12:00'; None, якщо не час."""
    m = re.match(r"^\s*(\d{1,2}):(\d{2})", value or "")
    if not m or int(m.group(1)) > 24 or int(m.group(2)) > 59:
        return None
    return f"{int(m.group(1)):02d}:{m.group(2)}"


def week(by_day):
    """{0..6 з понеділка: (from, to) | None} -> data для OpenHours; None, якщо днів немає взагалі."""
    if not any(by_day.get(i) for i in range(7)):
        return None
    return [{"from": by_day[i][0], "to": by_day[i][1], "active": True} if by_day.get(i)
            else {"from": "", "to": "", "active": False} for i in range(7)]


def split_contacts(values):
    phones, emails, sites = [], [], []
    for v in values:
        v = (v or "").strip()
        if not v:
            continue
        if EMAIL.match(v):
            emails.append(v)
        elif re.match(r"^(https?://|www\.)", v, re.I) or re.match(r"^[a-z0-9-]+(\.[a-z0-9-]+)+(/.*)?$", v, re.I):
            sites.append(v)
        elif len(re.sub(r"\D", "", v)) >= 9:
            phones.append(v)
    return phones, emails, sites


def read_expz(url):
    info = json.loads(fetch(EXPZ_INFO.format(id=EXPZ_ROOT.match(url).group(1))))
    by_day, many = {}, False
    for s in info.get("settings") or []:
        day = (s.get("dayOfWeek") or 0) - 1
        if not 0 <= day <= 6:
            continue
        if day in by_day:
            many = True  # кілька проміжків за день (перерва) - графік не показати, не чіпаємо
        f, t = clock(s.get("startTime")), clock(s.get("endTime"))
        by_day[day] = (f, t) if s.get("active") is not False and f and t else None
    phones, emails, sites = split_contacts(c.get("value") for c in info.get("contacts") or [])
    return {"hours": None if many else week(by_day), "phone": phones[0] if phones else None,
            "email": emails[0] if emails else None, "website": sites[0] if sites else None}


def read_choiceqr(url):
    parsed = urlparse(url)
    page = fetch(f"{parsed.scheme or 'https'}://{parsed.netloc}/")
    m = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', page, re.S)
    if not m:
        return None
    place = (json.loads(m.group(1)).get("props", {}).get("app") or {}).get("place") or {}
    contact = place.get("contactInfo") or {}
    by_day, many = {}, False
    for w in place.get("workTimeAll") or []:
        day = w.get("dayOfWeek")
        if not isinstance(day, int) or not 0 <= day <= 6:
            continue
        day = (day + 6) % 7  # 0 = неділя -> 6, 1 = понеділок -> 0
        if day in by_day:
            many = True
        f, t = clock(w.get("from")), clock(w.get("till"))
        by_day[day] = (f, t) if w.get("active") is not False and f and t else None
    phones = [contact.get("phone")] + list(contact.get("additionalPhones") or [])
    return {"hours": None if many else week(by_day),
            "phone": next((p for p in phones if isinstance(p, str) and len(re.sub(r"\D", "", p)) >= 9), None),
            "email": (contact.get("email") or "").strip() if EMAIL.match((contact.get("email") or "").strip()) else None,
            "website": (contact.get("website") or "").strip() or None}


def platform_contacts(url):
    if EXPZ_ROOT.match(url):
        return read_expz(url)
    if urlparse(url).netloc.lower().endswith(".choiceqr.com"):
        return read_choiceqr(url)
    return None


def digits(phone):
    return re.sub(r"\D", "", phone or "")[-9:]


def site_key(url):
    host = urlparse(url if re.match(r"https?://", url or "", re.I) else "https://" + (url or "")).hostname or ""
    return re.sub(r"^www\.", "", host.lower())


def current_hours(raw):
    try:
        data = json.loads(raw or "")
        return data.get("data") if data.get("perDay") else None
    except (ValueError, AttributeError):
        return None


def changes_for(r, found):
    """[(поле, старе, нове)] - лише те, що в QR-меню є і відрізняється."""
    result = []
    if found["hours"] and current_hours(r["OpenHours"]) != found["hours"]:
        result.append(("OpenHours", r["OpenHours"], json.dumps({"perDay": True, "data": found["hours"]}, ensure_ascii=False)))
    if found["phone"] and digits(found["phone"]) != digits(r["Phone"]):
        result.append(("Phone", r["Phone"], found["phone"]))
    site = found["website"]
    if site and not NOT_SITE.search(site) and site_key(site) != site_key(r["Website"]):
        result.append(("Website", r["Website"], site if re.match(r"https?://", site, re.I) else "https://" + site))
    if found["email"] and not (r["Email"] or "").strip():
        result.append(("Email", r["Email"], found["email"]))
    return result


def show_hours(value):
    data = current_hours(value)
    if not data:
        return (value or "")[:60]
    names = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Нд"]
    return ", ".join(f"{n} {d['from']}-{d['to']}" if d.get("active") else f"{n} вих." for n, d in zip(names, data))


UPDATE_SQL = {
    "OpenHours": "UPDATE RestaurantInfo SET OpenHours = %s WHERE RestaurantId = %s",
    "Website": "UPDATE RestaurantInfo SET Website = %s WHERE RestaurantId = %s",
    "Phone": "UPDATE Restaurants SET Phone = %s WHERE Id = %s",
    "Email": "UPDATE Restaurants SET Email = %s WHERE Id = %s",
}


def rollback(db, restaurant_id):
    cur = db.cursor(dictionary=True)
    cur.execute("""SELECT Id, Field, OldValue FROM PlatformContactSync WHERE RestaurantId = %s
                   AND Id IN (SELECT MIN(Id) FROM PlatformContactSync WHERE RestaurantId = %s GROUP BY Field)""",
                (restaurant_id, restaurant_id))
    rows = cur.fetchall()
    write = db.cursor()
    for row in rows:
        write.execute(UPDATE_SQL[row["Field"]], (row["OldValue"], restaurant_id))
        print(f"повернуто {row['Field']}: {(row['OldValue'] or '')[:80]}")
    write.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (restaurant_id,))
    db.commit()
    print(f"відкат: {len(rows)} полів" if rows else "змін цього ресторану в журналі немає")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true", help="записати (без нього - лише показати)")
    parser.add_argument("--only", help="один ресторан (Id)")
    parser.add_argument("--rollback", metavar="RESTAURANT_ID", help="повернути значення до першої синхронізації")
    args = parser.parse_args()

    db = get_db()
    cur = db.cursor(dictionary=True)
    cur.execute(LOG_SQL)
    if args.rollback:
        rollback(db, args.rollback)
        db.close()
        return

    only = " AND r.Id = %s" if args.only else ""
    cur.execute(f"""SELECT CONCAT(r.Id, '') AS Id, r.Name, r.Phone, r.Email, ri.Website, ri.OpenHours,
                           (SELECT ml.Url FROM RestaurantMenuLinks ml WHERE ml.RestaurantId = r.Id LIMIT 1) AS MenuUrl
                    FROM Restaurants r JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                    WHERE r.Status = 0{only}""", (args.only,) if args.only else ())
    restaurants = cur.fetchall()

    stats, write = Counter(), db.cursor()
    for r in restaurants:
        url = next((u for u in (r["MenuUrl"], r["Website"]) if u and (EXPZ_ROOT.match(u.strip()) or ".choiceqr.com" in u.lower())), None)
        if not url:
            continue
        url = url.strip()
        stats["з QR-меню"] += 1
        try:
            found = platform_contacts(url)
        except Exception as e:
            stats["QR-меню не прочиталось"] += 1
            print(f"!  {r['Name'][:40]}: {url} - {type(e).__name__}")
            continue
        if not found:
            stats["QR-меню не прочиталось"] += 1
            continue
        changes = changes_for(r, found)
        if not changes:
            stats["без змін"] += 1
            continue
        stats["зі змінами"] += 1
        print(f"\n{r['Name']} ({r['Id']}) <- {url}")
        for field, old, new in changes:
            stats[field] += 1
            if field == "OpenHours":
                print(f"   години:  {show_hours(old)}\n         -> {show_hours(new)}")
            else:
                print(f"   {field}: {old or '—'} -> {new}")
            if args.apply:
                write.execute(UPDATE_SQL[field], (new, r["Id"]))
                write.execute("""INSERT INTO PlatformContactSync (RestaurantId, Field, OldValue, NewValue, Source, ChangedAt)
                                 VALUES (%s, %s, %s, %s, %s, NOW())""", (r["Id"], field, old, new, url[:255]))
        if args.apply:
            # сторінка й iframe віддають Last-Modified з UpdateDate - без цього браузер і CDN тримали б старе
            write.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (r["Id"],))
            db.commit()

    print(f"\n{dict(stats)}")
    print("записано (журнал і відкат: PlatformContactSync, --rollback <Id>)" if args.apply else "нічого не записано - для запису --apply")
    db.close()


if __name__ == "__main__":
    main()

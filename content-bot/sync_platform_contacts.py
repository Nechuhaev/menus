"""Години роботи, телефон, сайт та email ресторану з його QR-меню (Expirenza, ChoiceQR, RestoApp), а якщо QR-меню
немає - з його сайту (розмітка schema.org, посилання tel:/mailto:, текст «Пн-Нд 10:00-22:00»). Кодом, без AI.
Картка ресторану - єдине джерело: «Розсилка» та сайт показують саме її, тому скрипт проходить по всіх закладах.

Звідки:
- Expirenza: https://static.shaketopay.com.ua/menu/prod/cache/menu/<id>/uk/restaurant_menu_info.json
  (той самий кеш, з якого беруться страви): settings - години по днях (dayOfWeek 1 = Пн ... 7 = Нд),
  contacts - телефони / email / сайт.
- ChoiceQR: головна сторінка закладу, __NEXT_DATA__ -> props.app.place:
  contactInfo (phone, email, website), workTimeAll - години по днях (dayOfWeek 0 = Нд ... 6 = Сб, як у JavaScript).
- RestoApp: https://<заклад>.restoapp.eu/b/restoapp/spots?locale=ua&with=schedule - phones, schedule
  (dayOfWeek "1" = Пн ... "7" = Нд); email і сайту там немає.

Що змінюється (лише якщо в QR-меню значення є і воно інше):
- RestaurantInfo.OpenHours - графік по днях {"perDay":true,"data":[7 днів з понеділка]};
- Restaurants.Phone - перший телефон (порівняння за останніми 9 цифрами, формат не чіпаємо, якщо номер той самий);
- RestaurantInfo.Website - лише якщо в нас порожньо і в QR-меню вказано сайт закладу (не соцмережу й не саме QR-меню);
- Restaurants.Email - лише якщо в нас порожньо (email може бути логіном власника).
- RestaurantInfo.Website - мітки реклами (fbclid, utm_*, gclid) прибираються з адреси сайту.
Сайт закладу - лише якщо QR-меню немає; QR-меню має перевагу (його веде сам заклад).
Не чіпаємо:
- телефон і години, коли одне QR-меню прив'язане до кількох наших ресторанів (мережа: у кожної філії свої);
- увесь ресторан, коли в QR-меню іноземний телефон / сайт / email - посилання, ймовірно, на інший заклад;
- години з помилкою в QR-меню (день «02:00-02:00»).
Кожна зміна пишеться в PlatformContactSync зі старим значенням - відкат: --rollback <RestaurantId>.

Запуск:  python3 sync_platform_contacts.py            # показати, що змінилося б (нічого не пише)
         python3 sync_platform_contacts.py --apply    # записати
         python3 sync_platform_contacts.py --apply --only <RestaurantId>
         python3 sync_platform_contacts.py --rollback <RestaurantId>
         python3 sync_platform_contacts.py --no-sites   # лише QR-меню, без сайтів закладів
"""
import argparse
import json
import re
from collections import Counter
from urllib.parse import urlparse

from menu_platforms import EXPZ_ROOT, RESTOAPP_API, fetch, is_restoapp
from concurrent.futures import ThreadPoolExecutor
from urllib.parse import parse_qsl, urlencode, urlunparse

import requests

from sync_restaurants import extract_restaurant_info, get_db, osm_to_week, page_text

EXPZ_INFO = "https://static.shaketopay.com.ua/menu/prod/cache/menu/{id}/uk/restaurant_menu_info.json"
NOT_SITE = re.compile(r"choiceqr\.com|expz\.menu|expirenza|shaketopay|restoapp\.eu|instagram\.com|facebook\.com|fb\.com|t\.me/|tiktok\.com|"
                      r"google\.|goo\.gl|maps\.app|linktr\.ee|taplink|wa\.me|viber|business\.site", re.I)
FOREIGN_TLD = re.compile(r"\.(cz|pl|sk|de|at|it|es|fr|uk|us|ee|lt|lv|ge|md|ro|hu|bg)(/|$)", re.I)
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
    # «24:00» сайт не читає як час (TimeSpan) - до півночі це «00:00», кінець після початку розуміється як нічна зміна
    return "00:00" if (int(m.group(1)), m.group(2)) == (24, "00") else f"{int(m.group(1)):02d}:{m.group(2)}"


def week(by_day):
    """{0..6 з понеділка: (from, to) | None} -> data для OpenHours; None, якщо днів немає взагалі
    або в QR-меню помилка (початок = кінець, напр. «02:00-02:00»)."""
    if not any(by_day.get(i) for i in range(7)):
        return None
    if any(d and d[0] == d[1] for d in by_day.values()):
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


def read_restoapp(url):
    spot = json.loads(fetch(RESTOAPP_API.format(host=urlparse(url).hostname, path="spots?locale=ua&with=schedule"))).get("data") or {}
    by_day, many = {}, False
    for s in spot.get("schedule") or []:
        try:
            day = int(s.get("dayOfWeek")) - 1
        except (TypeError, ValueError):
            continue
        if not 0 <= day <= 6:
            continue
        if day in by_day:
            many = True
        f, t = clock(s.get("startTime")), clock(s.get("endTime"))
        by_day[day] = (f, t) if s.get("state", "active") == "active" and f and t else None
    phones, _, _ = split_contacts(spot.get("phones") or [])
    return {"hours": None if many else week(by_day), "phone": phones[0] if phones else None, "email": None, "website": None}


def is_platform(url):
    url = (url or "").strip()
    return bool(url) and (bool(EXPZ_ROOT.match(url)) or ".choiceqr.com" in url.lower() or is_restoapp(url))


def platform_contacts(url):
    if EXPZ_ROOT.match(url):
        return read_expz(url)
    if is_restoapp(url):
        return read_restoapp(url)
    if urlparse(url).netloc.lower().endswith(".choiceqr.com"):
        return read_choiceqr(url)
    return None


TRACKING_PARAM = re.compile(r"^(fbclid|gclid|yclid|msclkid|igshid|utm_\w+)$", re.I)
SITE_HEADERS = {"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0; +https://tipmymenu.com)"}
CONTACTS_LINK = re.compile(r'<a[^>]+href=["\']([^"\'#]+)["\'][^>]*>(?:(?!</a>).){0,80}?(?:контакт|contact)', re.I | re.S)
MAILTO = re.compile(r'mailto:([A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,24})', re.I)


def clean_site(url):
    """Адреса сайту без міток реклами: «https://svkus.com/?fbclid=...» -> «https://svkus.com/»."""
    u = urlparse((url or "").strip())
    if not u.query:
        return (url or "").strip()
    query = [(k, v) for k, v in parse_qsl(u.query, keep_blank_values=True) if not TRACKING_PARAM.match(k)]
    return urlunparse(u._replace(query=urlencode(query)))


def site_contacts(url):
    """Години, телефон і email з сайту закладу (головна + сторінка «Контакти») - у форматі platform_contacts."""
    resp = requests.get(url, timeout=15, headers=SITE_HEADERS)
    resp.raise_for_status()
    pages = [resp.text]
    m = CONTACTS_LINK.search(resp.text)
    if m:
        link = requests.compat.urljoin(resp.url, m.group(1))
        if site_key(link) == site_key(resp.url):
            try:
                more = requests.get(link, timeout=15, headers=SITE_HEADERS)
                if more.ok:
                    pages.append(more.text)
            except requests.RequestException:
                pass
    found = {"hours": None, "phone": None, "email": None, "website": None}
    for raw in pages:
        info = extract_restaurant_info(raw, page_text(raw))
        found["hours"] = found["hours"] or osm_to_week(info.get("opening_hours"))
        found["phone"] = found["phone"] or info.get("phone")
        if not found["email"]:
            mail = MAILTO.search(raw)
            found["email"] = mail.group(1) if mail else None
    return found if any(found.values()) else None


def digits(phone):
    return re.sub(r"\D", "", phone or "")[-9:]


def ua_phone(phone):
    """'+38 (067) 123-45-67', '0671234567', '380671234567' -> '+380 67 123 45 67'; None - не український номер."""
    d = re.sub(r"\D", "", phone or "")
    if re.fullmatch(r"0800\d{6}", d):
        return f"0 800 {d[4:7]} {d[7:]}"  # безкоштовна лінія - без +380
    if re.fullmatch(r"380\d{9}", d):
        d = d[3:]
    elif re.fullmatch(r"80\d{9}", d):
        d = d[2:]
    elif re.fullmatch(r"0\d{9}", d):
        d = d[1:]
    else:
        return None
    return f"+380 {d[:2]} {d[2:5]} {d[5:7]} {d[7:]}"


def foreign(found):
    """Іноземний телефон / сайт / email у QR-меню - посилання, ймовірно, не на цей заклад."""
    if found["phone"] and not ua_phone(found["phone"]):
        return f"телефон {found['phone'].strip()}"
    for key in ("website", "email"):
        if found[key] and FOREIGN_TLD.search(found[key].split("@")[-1]):
            return f"{key} {found[key]}"
    return None


def link_key(url):
    """Одне QR-меню під різними адресами: Expirenza - id, ChoiceQR і RestoApp - піддомен."""
    m = EXPZ_ROOT.match(url)
    return m.group(1).lower() if m else urlparse(url).netloc.lower()


def site_key(url):
    host = urlparse(url if re.match(r"https?://", url or "", re.I) else "https://" + (url or "")).hostname or ""
    return re.sub(r"^www\.", "", host.lower())


def current_hours(raw):
    try:
        data = json.loads(raw or "")
        return data.get("data") if data.get("perDay") else None
    except (ValueError, AttributeError):
        return None


def changes_for(r, found, shared=False):
    """[(поле, старе, нове)] - лише те, що в QR-меню є і відрізняється.
    shared - QR-меню однієї мережі на кількох наших ресторанах: телефон і години не чіпаємо."""
    result = []
    if not shared and found["hours"] and current_hours(r["OpenHours"]) != found["hours"]:
        result.append(("OpenHours", r["OpenHours"], json.dumps({"perDay": True, "data": found["hours"]}, ensure_ascii=False)))
    phone = ua_phone(found["phone"]) or (found["phone"] or "").strip() or None
    if not shared and phone and digits(phone) != digits(r["Phone"]):
        result.append(("Phone", r["Phone"], phone))
    site = found["website"]
    if site and not NOT_SITE.search(site) and not (r["Website"] or "").strip():
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


def apply_changes(db, write, r, changes, source, apply, stats):
    for field, old, new in changes:
        stats[field] += 1
        if field == "OpenHours":
            print(f"   години:  {show_hours(old)}\n         -> {show_hours(new)}")
        else:
            print(f"   {field}: {old or '—'} -> {new}")
        if apply:
            write.execute(UPDATE_SQL[field], (new, r["Id"]))
            write.execute("""INSERT INTO PlatformContactSync (RestaurantId, Field, OldValue, NewValue, Source, ChangedAt)
                             VALUES (%s, %s, %s, %s, %s, NOW())""", (r["Id"], field, old, new, source[:255]))
    if apply:
        # сторінка й iframe віддають Last-Modified з UpdateDate - без цього браузер і CDN тримали б старе
        write.execute("UPDATE Restaurants SET UpdateDate = NOW() WHERE Id = %s", (r["Id"],))
        db.commit()


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
    parser.add_argument("--no-sites", dest="sites", action="store_false", help="лише QR-меню, без сайтів закладів")
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
    for r in restaurants:
        url = next((u for u in (r["MenuUrl"], r["Website"]) if is_platform(u)), None)
        r["Url"] = url.strip() if url else None
        site = (r["Website"] or "").strip()
        # сайт закладу - джерело, лише коли QR-меню немає (і це сайт, а не соцмережа чи QR-меню)
        r["Site"] = clean_site(site) if not r["Url"] and args.sites and re.match(r"https?://", site, re.I) and not NOT_SITE.search(site) else None
    # скільки наших ресторанів дивляться на те саме QR-меню (за --only рахуємо по всіх)
    if args.only:
        cur.execute("""SELECT (SELECT ml.Url FROM RestaurantMenuLinks ml WHERE ml.RestaurantId = r.Id LIMIT 1) AS MenuUrl, ri.Website
                       FROM Restaurants r JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id WHERE r.Status = 0""")
        everyone = cur.fetchall()
    else:
        everyone = restaurants
    uses = Counter(link_key(u.strip()) for x in everyone for u in [next((v for v in (x["MenuUrl"], x["Website"]) if is_platform(v)), None)] if u)
    # той самий сайт у кількох наших ресторанів - мережа: телефон і години з сайту не беремо
    site_uses = Counter(site_key(x["Website"]) for x in everyone if (x["Website"] or "").strip())

    def read(r):
        try:
            return platform_contacts(r["Url"]) if r["Url"] else site_contacts(r["Site"]) if r["Site"] else None, None
        except Exception as e:
            return None, type(e).__name__

    # сайти й QR-меню читаються паралельно (сотні закладів), записи - по черзі нижче
    with ThreadPoolExecutor(max_workers=8) as pool:
        results = list(pool.map(read, restaurants))

    stats, write = Counter(), db.cursor()
    for r, (found, error) in zip(restaurants, results):
        url = r["Url"] or r["Site"]
        source = "QR-меню" if r["Url"] else "сайт"
        changes = []
        cleaned = clean_site(r["Website"])
        if (r["Website"] or "").strip() and cleaned != (r["Website"] or "").strip():
            changes.append(("Website", r["Website"], cleaned))
        if not url:
            if changes:
                stats["зі змінами"] += 1
                print(f"\n{r['Name']} ({r['Id']})")
                apply_changes(db, write, r, changes, "clean_site", args.apply, stats)
            continue
        stats[f"з {source}"] += 1
        if error or not found:
            stats[f"{source} не прочиталось"] += 1
            if error:
                print(f"!  {r['Name'][:40]}: {url} - {error}")
            if changes:
                apply_changes(db, write, r, changes, "clean_site", args.apply, stats)
            continue
        suspicious = foreign(found)
        if suspicious:
            stats["QR-меню, ймовірно, іншого закладу"] += 1
            print(f"\n?  {r['Name']} ({r['Id']}) <- {url}\n   не чіпаємо: у QR-меню {suspicious} - перевірте посилання на меню")
            continue
        shared = uses[link_key(url)] > 1 if r["Url"] else site_uses[site_key(url)] > 1
        if shared:
            stats["мережа: телефон і години не чіпаємо"] += 1
        changes += [c for c in changes_for(r, found, shared) if c[0] != "Website" or not changes]
        if not changes:
            stats["без змін"] += 1
            continue
        stats["зі змінами"] += 1
        print(f"\n{r['Name']} ({r['Id']}) <- {source} {url}")
        apply_changes(db, write, r, changes, url, args.apply, stats)

    print(f"\n{dict(stats)}")
    print("записано (журнал і відкат: PlatformContactSync, --rollback <Id>)" if args.apply else "нічого не записано - для запису --apply")
    db.close()


if __name__ == "__main__":
    main()

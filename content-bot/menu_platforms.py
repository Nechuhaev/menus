"""Меню з QR-меню сервісів без AI: Expirenza (expz.menu), ChoiceQR (*.choiceqr.com) і RestoApp (*.restoapp.eu).

Обидва віддають готовий HTML з чіткою розміткою, тож назва, ціна, опис, вага, фото й розділ
беруться кодом. platform_menu(url) повертає страви в тому ж форматі, що й extract_menu
(name, lang, category, price, weight, description, image_url, url), або None, якщо адреса
не з цих сервісів. Запит не частіше ніж раз на секунду.
"""
import html as html_lib
import json
import re
import time
from urllib.parse import urlparse

import requests

HEADERS = {"User-Agent": "Mozilla/5.0 (compatible; TipMyMenuBot/1.0)", "Accept-Language": "uk,ru;q=0.8,en;q=0.5"}
MAX_PAGES = 30
_last_request = [0.0]


def fetch(url):
    wait = 1.0 - (time.time() - _last_request[0])
    if wait > 0:
        time.sleep(wait)
    _last_request[0] = time.time()
    resp = requests.get(url, timeout=20, headers=HEADERS)
    resp.raise_for_status()
    return resp.text


def text(fragment):
    return html_lib.unescape(re.sub(r"\s+", " ", re.sub(r"<[^>]+>", " ", fragment or ""))).strip()


def detect_lang(value):
    if re.search(r"[іїєґ]", value, re.I):
        return "uk"
    if re.search(r"[ыэъё]", value, re.I):
        return "ru"
    if re.search(r"[а-я]", value, re.I):
        return "uk"
    return "en"


def sure_lang(value):
    """Мова, лише коли текст її однозначно показує: і/ї/є/ґ - uk, ы/э/ъ/ё - ru, лише латиниця - en; інакше None
    («Говядина, лук» може бути й ru, й uk; у змішаному тексті є літери обох)."""
    uk, ru = re.search(r"[іїєґ]", value or "", re.I), re.search(r"[ыэъё]", value or "", re.I)
    if uk and ru:
        return None  # змішаний текст («Пиво Оболонь Світле, легкое»)
    if uk:
        return "uk"
    if ru:
        return "ru"
    if re.search(r"[a-z]", value or "", re.I) and not re.search(r"[а-я]", value or "", re.I):
        return "en"
    return None


def parse_price(value):
    digits = re.sub(r"[^\d.,]", "", value or "").replace(",", ".")
    try:
        return float(digits) if digits else None
    except ValueError:
        return None


def plain_text(value):
    """Опис без HTML: сервіси віддають його з тегами («<p>Японський рис...</p><p>Подаємо...</p>»), а сайт
    показує опис як текст. Абзаци й <br> - переноси рядків, сутності (&amp;, &nbsp;) - символи."""
    if not value:
        return ""
    if not re.search(r"<[a-zA-Z/][^>]*>|&#?\w+;", value):
        return value.strip()  # звичайний текст - як є, з порожніми рядками між абзацами
    value = re.sub(r"(?i)<br\s*/?>|</(?:p|div|li|h[1-6])\s*>", "\n", value)
    value = html_lib.unescape(re.sub(r"<[^>]+>", "", value))
    lines = (re.sub(r"[ \t\u00a0]+", " ", line).strip() for line in value.split("\n"))
    return "\n".join(line for line in lines if line)


WEIGHT_UNITS = re.compile(r"(?<!\w)(?:г|гр|кг|мл|л|шт|см|g|gr|kg|ml|l|pcs|cm|oz)(?!\w)\.?", re.I)


def is_weight_only(value):
    """«250/50 г», «0,5 л», «1 шт» - вага чи кількість замість опису (вона й так показується окремо)."""
    # спершу цифри й знаки («300g» -> « g»), потім одиниці окремими словами
    rest = WEIGHT_UNITS.sub("", re.sub(r"[\d.,/×+\-–()]", " ", value or ""))
    rest = re.sub(r"(?<!\w)[xх](?!\w)|\s", "", rest)
    return bool(value and value.strip()) and not rest


def clean_description(value):
    """Опис для страви: без HTML; вага замість опису - порожньо."""
    value = plain_text(value)
    return "" if is_weight_only(value) else value


def dish(name, category, price, weight, description, image_url):
    name = plain_text(name)
    return {"name": name, "lang": detect_lang(name), "category": category or None, "price": price,
            "weight": weight or None, "description": clean_description(description) or None,
            "image_url": image_url or None, "url": None}


# картинка шапки/логотипа сайту чи og:image - не фото страви (боти ставили фото закладу на всі страви)
GENERIC_IMAGE = re.compile(r"logo|favicon|banner|hero|cover|header|background|placeholder|no[-_]?(?:image|photo)|og[-_]?image|share", re.I)
SAME_IMAGE_DISHES = 3


def drop_generic_images(dishes, *site_images):
    """Прибирає image_url, що не є фото саме цієї страви: та сама адреса на SAME_IMAGE_DISHES+ стравах,
    фото з сайту закладу (og:image головної) чи назва файлу на кшталт logo/banner/hero."""
    counts = {}
    for d in dishes:
        if d.get("image_url"):
            counts[d["image_url"]] = counts.get(d["image_url"], 0) + 1
    site = {u for u in site_images if u}
    for d in dishes:
        url = d.get("image_url")
        if url and (counts[url] >= SAME_IMAGE_DISHES or url in site
                    or GENERIC_IMAGE.search(urlparse(url).path.rsplit("/", 1)[-1])):
            d["image_url"] = None


# --- Expirenza: https://expz.menu/<uuid> ---
# Сторінку малює JavaScript, а дані сайт бере з публічного кешу меню на CDN - усі меню закладу
# одним JSON (menus, categories, dishes з title, description, imageUrl, minPrice, dishVariants)

EXPZ_ROOT = re.compile(r"^https?://(?:www\.)?expz\.menu/([0-9a-f-]{36})", re.I)
EXPZ_STATIC = "https://static.shaketopay.com.ua/menu/prod/cache/menu/{id}/uk/menu.json"


def parse_expz_json(data):
    categories = {c.get("id"): c.get("name") for c in data.get("categories") or []}
    items = []
    for d in data.get("dishes") or []:
        if d.get("active") is False:
            continue
        variants = [v for v in d.get("dishVariants") or [] if v.get("active") is not False]
        weight = None
        if len(variants) == 1 and variants[0].get("amount"):
            amount = variants[0]["amount"]
            weight = f"{amount:g} {variants[0].get('unit') or ''}".strip()
        price = d.get("minPrice") or (variants[0].get("price") if variants else None)
        item = dish(d.get("title"), categories.get(d.get("categoryId")), price, weight,
                    (d.get("description") or "").strip(), (d.get("imageUrl") or "").split("?")[0] or None)
        # кілька варіантів з різною ціною - ціна «від», як IsPriceMultiple у нас
        item["price_from"] = bool(d.get("maxPrice") and d.get("minPrice") and d["maxPrice"] > d["minPrice"])
        items.append(item)
    return items


def read_expz(url):
    return parse_expz_json(json.loads(fetch(EXPZ_STATIC.format(id=EXPZ_ROOT.match(url).group(1)))))


# --- ChoiceQR: https://<заклад>.choiceqr.com/section:<меню>/<розділ> ---

# розділи-магазин (подарунки, посуд, сертифікати) - не страви; у назві й у адресі (section:podarunki-...)
CHOICEQR_SKIP_SECTION = re.compile(r"подарун|подарок|порцеля|посуд|сертиф|podarun|podarok|porcel|posud|sertyf|sertif|gift|merch", re.I)
CHOICEQR_UNITS = {"g": "г", "ml": "мл", "kg": "кг", "l": "л", "pcs": "шт"}


def parse_choiceqr_next_data(page_html):
    """Сторінка меню: дані Next.js (__NEXT_DATA__) - страви цього меню з ціною в копійках,
    вагою, фото й розділом. None, якщо на сторінці їх немає."""
    m = re.search(r'<script id="__NEXT_DATA__"[^>]*>(.*?)</script>', page_html, re.S)
    if not m:
        return None
    try:
        app = json.loads(m.group(1))["props"]["app"]
    except (ValueError, KeyError, TypeError):
        return None
    return parse_choiceqr_app(app)


def parse_choiceqr_app(app):
    """Страви з даних застосунку ChoiceQR ({menu, categories, sections}) - з __NEXT_DATA__ сторінки розділу
    або з API /api/public/menu (лендинги). None, якщо поля menu немає."""
    menu = (app or {}).get("menu")
    if menu is None:
        return None
    categories = {c.get("_id"): (c.get("name") or "").strip() for c in app.get("categories") or []}
    skip = {s.get("_id") for s in app.get("sections") or [] if CHOICEQR_SKIP_SECTION.search(s.get("name") or "")}
    items = []
    for d in menu or []:
        if d.get("available") is False or d.get("section") in skip:
            continue
        weight = (d.get("weight") or "").strip()
        # «за 100» - ціна за 100 г, як IsPriceMultiple у нас
        price_from = bool(re.match(r"за\s", weight, re.I))
        if weight and re.fullmatch(r"[\d.,/ ]+", weight):
            weight = f"{weight} {CHOICEQR_UNITS.get(d.get('weightType'), d.get('weightType') or '')}".strip()
        elif price_from:
            weight = None
        media = (d.get("media") or [{}])[0]
        item = dish(d.get("name"), categories.get(d.get("category")),
                    d["price"] / 100 if d.get("price") else None, weight,
                    (d.get("description") or "").strip(), media.get("url") or media.get("big"))
        item["price_from"] = price_from
        items.append(item)
    return items


def parse_choiceqr_page(page_html):
    """Сторінка розділу: дані Next.js, а якщо їх немає - JSON-LD Menu -> MenuSection -> MenuItem
    (усі розділи цього меню), фото - з HTML за alt = назва страви."""
    items = parse_choiceqr_next_data(page_html)
    if items is not None:
        return items
    images = {html_lib.unescape(alt).strip(): src for src, alt in
              re.findall(r'<img[^>]+src="(https://cdn-media\.choiceqr\.com/[^"]+)"[^>]*alt="([^"]*)"', page_html)}
    items = []
    for raw in re.findall(r'<script[^>]+type="application/ld\+json"[^>]*>(.*?)</script>', page_html, re.S):
        try:
            data = json.loads(raw)
        except ValueError:
            continue
        menus = data.get("hasMenu") if isinstance(data, dict) else None
        for menu in (menus if isinstance(menus, list) else [menus] if menus else []):
            for section in menu.get("hasMenuSection") or []:
                for item in section.get("hasMenuItem") or []:
                    name = (item.get("name") or "").strip()
                    offers = item.get("offers") or {}
                    if isinstance(offers, list):
                        offers = offers[0] if offers else {}
                    items.append(dish(name, section.get("name"), parse_price(str(offers.get("price") or "")), None,
                                      (item.get("description") or "").strip(), images.get(name)))
    return items


def is_choiceqr_domain(url):
    """ChoiceQR на власному домені закладу: адреси розділів «/section:menyu» є лише в ChoiceQR (coast.dp.ua)."""
    return bool(re.search(r"/section:[a-z0-9-]+", urlparse(url or "").path, re.I))


def read_choiceqr(url):
    parsed = urlparse(url)
    root = f"{parsed.scheme or 'https'}://{parsed.netloc}/"
    pages_html = fetch(root)
    if url.rstrip("/") + "/" != root:
        pages_html += fetch(url)
    # одна сторінка розділу на кожне меню: JSON-LD містить усі розділи свого меню
    menus = {}
    # у HTML без JavaScript розділи - не лише в href, а й у data-url та JSON сторінки
    for path in re.findall(r'section:[a-z0-9-]+(?:/[a-z0-9-]+)?', pages_html, re.I):
        if not CHOICEQR_SKIP_SECTION.search(path):
            menus.setdefault(path.split("/")[0], root + path)
    items = []
    for page_url in list(menus.values())[:MAX_PAGES]:
        items += parse_choiceqr_page(fetch(page_url))
    if not menus:
        # шаблон «Лендинг» (з листопада 2026): головна - візитка, без розділів і страв у HTML; меню браузер бере
        # з API того ж домену, і лише з Referer/Origin сторінки (без них - «domain.cannotParse»)
        items = read_choiceqr_api(root)
    return items


def choiceqr_api(root, query):
    resp = requests.get(f"{root}api/public/menu?{query}", timeout=20,
                        headers={**HEADERS, "Accept": "application/json",
                                 "Referer": f"{root}online-menu", "Origin": root.rstrip("/")})
    resp.raise_for_status()
    return resp.json()


QR_SUSPENDED = set()  # адреси QR-меню, де сервіс призупинено (ChoiceQR: place.expired - підписку не продовжено)


def choiceqr_expired(root):
    """True - підписка ChoiceQR закладу закінчилась (place.expired): меню приховане й від гостей."""
    try:
        return bool((choiceqr_api(root, "screen=desktop&lang=uk").get("place") or {}).get("expired"))
    except Exception:
        return False


def read_choiceqr_api(root):
    """Меню ChoiceQR-лендингу з API: перша відповідь - страви першого розділу меню й список розділів;
    решта розділів - запитом ?section=<id> (не більше MAX_PAGES)."""
    base = "screen=desktop&lang=uk"
    app = choiceqr_api(root, base)
    if (app.get("place") or {}).get("expired"):
        QR_SUSPENDED.add(root)
        return []
    items = parse_choiceqr_app(app) or []
    seen = {d.get("section") for d in app.get("menu") or []}
    for section in (app.get("sections") or [])[:MAX_PAGES]:
        sid = section.get("_id")
        if not sid or sid in seen or CHOICEQR_SKIP_SECTION.search(section.get("name") or ""):
            continue
        time.sleep(1)
        part = choiceqr_api(root, f"{base}&section={sid}")
        items += parse_choiceqr_app(part) or []
        seen.add(sid)
    return items


# --- RestoApp (Binotel): https://<заклад>.restoapp.eu/#/ ---
# Сторінку малює JavaScript, дані - публічний JSON того ж піддомену: /b/restoapp/menu-sections
# (розділи -> категорії -> страви з name, description, price.amount у копійках, weight + units, image)
# і /b/restoapp/spots (телефони, Instagram, графік). Мова - locale=ua.

RESTOAPP_API = "https://{host}/b/restoapp/{path}"
RESTOAPP_UNITS = {"gram": "г", "ml": "мл", "kilogram": "кг", "liter": "л", "piece": "шт"}


def is_restoapp(url):
    return (urlparse(url).hostname or "").lower().endswith(".restoapp.eu")


def parse_restoapp_sections(data):
    items = []
    for section in data.get("data") or []:
        if section.get("state") != "active":
            continue
        groups = [(c.get("name"), c.get("products") or []) for c in section.get("categories") or [] if c.get("state") == "active"]
        groups.append((section.get("name"), section.get("products") or []))
        for category, products in groups:
            for p in products:
                if p.get("state") != "active":
                    continue
                amount = (p.get("price") or {}).get("amount")
                weight = f"{p['weight']:g} {RESTOAPP_UNITS.get(p.get('units'), p.get('units') or '')}".strip() if p.get("weight") else None
                item = dish(p.get("name"), category, int(amount) / 100 if amount else None, weight,
                            (p.get("description") or "").strip(), p.get("image"))
                # модифікації (розмір, порція) з іншою ціною - ціна «від»
                item["price_from"] = bool(p.get("modifications"))
                items.append(item)
    return items


def read_restoapp(url):
    host = urlparse(url).hostname
    return parse_restoapp_sections(json.loads(fetch(RESTOAPP_API.format(
        host=host, path="menu-sections?pagination=0&type=section&locale=ua&with=categories.products,products"))))


# --- ps.me: https://<заклад>.ps.me/category/<розділ> ---
# Сторінка Next.js: __NEXT_DATA__ -> props.pageProps.products - УСІ страви меню (name, price, description з вагою
# «<p>350 г</p>», photo, categoryName), а settings.contacts - телефон, email, графік («12:00 – 21:00»).

def is_psme(url):
    host = (urlparse(url or "").hostname or "").lower()
    return host.endswith(".ps.me") and not host.startswith("www.")


def psme_page_props(url):
    parsed = urlparse(url)
    page = fetch(f"{parsed.scheme or 'https'}://{parsed.netloc}{parsed.path or '/'}")
    m = re.search(r'<script id="__NEXT_DATA__"[^>]*>([\s\S]*?)</script>', page)
    props = json.loads(m.group(1))["props"]["pageProps"] if m else {}
    if not props.get("products") and parsed.path not in ("", "/"):
        return psme_page_props(f"{parsed.scheme or 'https'}://{parsed.netloc}/")
    return props


def read_psme(url):
    items = []
    for p in psme_page_props(url).get("products") or []:
        description = plain_text(p.get("description"))
        weight = description if is_weight_only(description) else None
        price = p.get("price")
        items.append(dish(p.get("name"), p.get("categoryName"), float(price) if price else None, weight,
                          description, p.get("photo")))
    return [d for d in items if d["name"]]


PLATFORM_LINK = re.compile(r'https?://(?:www\.)?expz\.menu/[0-9a-f-]{36}|https?://[a-z0-9-]+\.choiceqr\.com[^"\'\s<>]*|'
                           r'https?://(?!www\.)[a-z0-9_-]+\.restoapp\.eu[^"\'\s<>]*|'
                           r'https?://(?!www\.)[a-z0-9_-]+\.ps\.me[^"\'\s<>]*', re.I)


def find_platform_link(raw_html):
    """Посилання на меню в Expirenza/ChoiceQR/RestoApp/ps.me на сайті ресторану (кнопка «Меню», QR тощо)."""
    m = PLATFORM_LINK.search(raw_html or "")
    return m.group(0) if m else None


# Poster (каса): посилання на меню «poster://{акаунт}?spot={точка}» ставить сайт, коли власник підключив Poster
# у кабінеті (PosterConnections: токен OAuth). Меню - з API каси: категорії, страви, ціни (копійки), вага, фото.
POSTER_LINK = re.compile(r"^poster://([a-z0-9_-]+)(?:\?spot=(\d+))?", re.I)
POSTER_API = "https://joinposter.com/api/{method}"


def poster_token(account):
    from sync_restaurants import get_db  # тут, щоб не було циклічного імпорту
    db = get_db()
    try:
        cur = db.cursor()
        cur.execute("SELECT Token FROM PosterConnections WHERE Account = %s ORDER BY ConnectedAt DESC LIMIT 1", (account,))
        row = cur.fetchone()
        return row[0] if row else None
    finally:
        db.close()


def poster_call(method, token, **params):
    resp = requests.get(POSTER_API.format(method=method), params={"token": token, **params}, timeout=30, headers=HEADERS)
    resp.raise_for_status()
    data = resp.json()
    if "error" in data:
        raise RuntimeError(f"Poster {method}: {data.get('error')} {data.get('message', '')}".strip())
    return data.get("response") or []


def parse_poster_menu(products, categories, spot, account):
    """Страви з menu.getProducts: лише страви й товари (не напівфабрикати), не приховані, видимі на точці."""
    hidden_categories = {str(c.get("category_id")) for c in categories if str(c.get("category_hidden", "0")) == "1"}
    items = []
    for p in products:
        if str(p.get("type")) == "1" or str(p.get("hidden", "0")) == "1":
            continue
        if str(p.get("menu_category_id")) in hidden_categories:
            continue
        spots = p.get("spots") or []
        chosen = next((x for x in spots if spot and str(x.get("spot_id")) == str(spot)), None) or (spots[0] if spots else None)
        if chosen is not None and str(chosen.get("visible", "1")) == "0":
            continue
        cents = (chosen or {}).get("price")
        if cents is None:
            cents = next(iter((p.get("price") or {}).values()), None)
        try:
            price = round(int(cents) / 100, 2) if cents not in (None, "") else None
        except (TypeError, ValueError):
            price = None
        out = p.get("out")
        try:
            out = int(float(out or 0))
        except (TypeError, ValueError):
            out = 0
        unit = (p.get("unit") or "").lower()
        weight = f"{out} {'мл' if unit in ('l', 'ml') else 'г'}" if out > 0 else ""
        photo = p.get("photo_origin") or p.get("photo") or ""
        image = f"https://{account}.joinposter.com{photo}" if photo.startswith("/") else (photo or None)
        name = text(p.get("product_name") or "")
        if not name:
            continue
        items.append(dish(name, text(p.get("category_name") or ""), price, weight,
                          clean_description(text(p.get("product_production_description") or "")), image))
    return items


def read_poster(url):
    m = POSTER_LINK.match(url)
    account, spot = m.group(1), m.group(2)
    token = poster_token(account)
    if not token:
        print(f"    Poster: немає токена для акаунта {account} (власник відключив застосунок?)")
        return []
    categories = poster_call("menu.getCategories", token)
    products = poster_call("menu.getProducts", token)
    return parse_poster_menu(products, categories, spot, account)


# McDonald's Україна: «mcdonalds://ua» у RestaurantMenuLinks - одне меню на всі філії (content-bot/mcdonalds.py).
# Цін на сайті немає: назва, розділ, фото, вага й опис; ціни - окремим джерелом (доставка), коли буде.
MCD_LINK = re.compile(r"^mcdonalds://ua", re.I)
MCD_BASE = "https://www.mcdonalds.com/ua/uk-ua"
MCD_PAGES = [("eat/fullmenu.html", None), ("eat/fullmenu/breakfast.html", "Сніданок")]
MCD_SKIP_HEADINGS = re.compile(r"«МакДональдз»|мета і вплив|працюй з нами|прескімната", re.I)
_MCD_CACHE = {}


def mcd_get(url):
    for attempt in range(3):
        try:
            r = requests.get(url, headers=HEADERS, timeout=30)
            if r.status_code == 200:
                return r.text
        except requests.RequestException:
            pass
        time.sleep(2 * (attempt + 1))
    return ""


def mcd_product(product_id):
    """Опис і вага зі сторінки страви (og:description, «212 г/g» / «250 мл/ml»)."""
    page = mcd_get(f"{MCD_BASE}/product/{product_id}.html")
    desc = re.search(r'<meta property="og:description" content="([^"]*)"', page)
    weight = re.search(r"(\d+(?:[.,]\d+)?)\s*(г|мл)\s*/\s*(?:g|ml)", page)
    return (html_lib.unescape(desc.group(1)).strip() if desc else ""), (f"{weight.group(1)} {weight.group(2)}" if weight else "")


def parse_mcd_category_page(page, default_category):
    """[(розділ, id, назва, картинка)] у порядку на сторінці: заголовок h2 - розділ, далі плитки страв."""
    out, category = [], default_category
    token = re.compile(r'<h2 class="cmp-title__text">\s*([^<]+?)\s*</h2>|<a href="[^"]*/product/(\d+)\.html"[^>]*cmp-category__item-link[^>]*>(.*?)</a>', re.S)
    for m in token.finditer(page):
        if m.group(1):
            heading = html_lib.unescape(m.group(1)).strip()
            category = None if MCD_SKIP_HEADINGS.search(heading) else heading
            continue
        if not category:
            continue
        inner = m.group(3)
        name = re.search(r'cmp-category__item-name"[^>]*>\s*([^<]+?)\s*<', inner)
        img = re.search(r'<img[^>]+src="(https://s7d1\.scene7\.com/is/image/mcdonalds/[^":?]+)', inner)
        if name:
            out.append((category, m.group(2), html_lib.unescape(name.group(1)).strip(), img.group(1) if img else None))
    return out


def read_mcdonalds(url):
    if "items" in _MCD_CACHE:
        return _MCD_CACHE["items"]
    seen, items = set(), []
    for path, default_category in MCD_PAGES:
        for category, product_id, name, image in parse_mcd_category_page(mcd_get(f"{MCD_BASE}/{path}"), default_category):
            if product_id in seen:
                continue
            seen.add(product_id)
            description, weight = mcd_product(product_id)
            time.sleep(0.3)
            items.append(dish(name, category, None, weight, description, f"{image}?wid=800&fmt=jpg&bgc=255,255,255" if image else None))
    _MCD_CACHE["items"] = items
    return items


def platform_menu(url):
    """Страви з QR-меню сервісу або None, якщо адреса не з підтримуваних сервісів."""
    if not url:
        return None
    host = urlparse(url).netloc.lower()
    if POSTER_LINK.match(url):
        reader = read_poster
    elif MCD_LINK.match(url):
        reader = read_mcdonalds
    elif EXPZ_ROOT.match(url):
        reader = read_expz
    elif host.endswith(".choiceqr.com") or is_choiceqr_domain(url):
        reader = read_choiceqr
    elif host.endswith(".restoapp.eu"):
        reader = read_restoapp
    elif is_psme(url):
        reader = read_psme
    else:
        return None
    try:
        items = reader(url)
    except Exception as e:
        print(f"    QR menu read failed for {url}: {e}")
        return []
    seen, result = set(), []
    for d in items:
        key = d["name"].lower()
        if key not in seen:
            seen.add(key)
            result.append(d)
    return result

"""Меню з QR-меню сервісів без AI: Expirenza (expz.menu) і ChoiceQR (*.choiceqr.com).

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
        menu = app["menu"]
    except (ValueError, KeyError, TypeError):
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
    return items


PLATFORM_LINK = re.compile(r'https?://(?:www\.)?expz\.menu/[0-9a-f-]{36}|https?://[a-z0-9-]+\.choiceqr\.com[^"\'\s<>]*', re.I)


def find_platform_link(raw_html):
    """Посилання на меню в Expirenza/ChoiceQR на сайті ресторану (кнопка «Меню», QR тощо)."""
    m = PLATFORM_LINK.search(raw_html or "")
    return m.group(0) if m else None


def platform_menu(url):
    """Страви з QR-меню сервісу або None, якщо адреса не з підтримуваних сервісів."""
    if not url:
        return None
    host = urlparse(url).netloc.lower()
    if EXPZ_ROOT.match(url):
        reader = read_expz
    elif host.endswith(".choiceqr.com"):
        reader = read_choiceqr
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

"""Аналіз конкурентів через Serpstat API (Google Україна): чого немає на tipmymenu.com.

Для кожного конкурента: загальні показники домену, топ сторінок за трафіком, згруповані за типом сторінки
(шаблон адреси), і ключові слова, за якими конкурент у топ-10, а ми - ні (getUniqKeywords).
Без AI. Ключ - SERPSTAT_TOKEN або SERPSTAT_API_KEY із середовища (.env на сервері чи секрети GitHub).

Запуск:  python3 serpstat_competitors.py                       # усі конкуренти з COMPETITORS
         python3 serpstat_competitors.py --pages 100 --gap 300  # розміри вибірок (рядки = кредити Serpstat)
Результат: друк зведення + serpstat_report.json (повні дані).
"""
import argparse
import json
import os
import re
import sys
import time
import uuid
from collections import defaultdict
from urllib.parse import urlparse, unquote

import requests

API = "https://api.serpstat.com/v4"
SE = "g_ua"
OUR = "tipmymenu.com"
COMPETITORS = ["reston.ua", "mesta.com.ua", "tomato.ua", "topclub.ua", "lasoon.net"]

TOKEN = os.environ.get("SERPSTAT_TOKEN") or os.environ.get("SERPSTAT_API_KEY")


_debugged = set()


def call(method, params):
    time.sleep(1.2)  # ліміт Serpstat - 1 запит на секунду
    resp = requests.post(f"{API}?token={TOKEN}", json={"id": str(uuid.uuid4()), "method": method, "params": params}, timeout=60)
    data = resp.json()
    if "error" in data:
        raise RuntimeError(f"{method}: {data['error'].get('message')}")
    result = data.get("result") or {}
    if method not in _debugged:  # назви полів відповіді - для перевірки
        _debugged.add(method)
        sample = rows(result)[:1]
        print(f"  [{method}] поля: {sorted(sample[0].keys()) if sample and isinstance(sample[0], dict) else type(result.get('data'))}")
    return result


def rows(result):
    data = result.get("data")
    if isinstance(data, dict):
        return list(data.values())
    return data or []


FOOD = re.compile(r"ресторан|кафе|кафé|кав.ярн|кофейн|бар\b|бары|бари|паб|pub|bar|cafe|restaurant|пиццер|піцер|пицц|піц|суши|суші|sushi|"
                  r"кухн|меню|menu|бургер|burger|стейк|шашлык|шашлик|хинкал|хінкал|хачапур|плов|вареник|борщ|еда|їжа|ужин|вечер|обед|обід|"
                  r"сніданок|завтрак|бранч|кальян|караоке|банкет|корпоратив|свадьб|весіл|день рожд|день народ|терас|летн|доставк|"
                  r"кондитер|десерт|морожен|морозив|вино|пиво|пив|крафт|рамен|фо бо|wok|вок|пельмен|чебурек|шаурм|шаверм", re.I)

CATEGORIES = [
    ("меню/ціни конкретного закладу", r"меню|menu|цен|ціни|цін"),
    ("доставка", r"доставк|delivery"),
    ("привід/подія (банкет, весілля, ДН, корпоратив, свята)", r"банкет|свадьб|весіл|день рожд|день народ|корпоратив|новый год|новий рік|8 марта|романт|выпускн|випускн|юбилей|ювіле|праздн|свят"),
    ("особливість (тераса, вид, музика, діти, недорого, цілодобово)", r"терас|летн|літн|вид|панорам|живая музыка|жива музика|дет|діт|недорог|дешев|бюджет|круглосуточ|цілодоб|24|кальян|караоке|танц|veg|вега|халял|романт"),
    ("кухня/страва", r"кухн|итальян|італ|грузин|японск|япон|корей|китай|мексик|армян|вірмен|украинск|українськ|азиат|азій|узбек|французск|француз|суши|суші|пицц|піц|бургер|стейк|шашлык|шашлик|хинкал|хінкал|хачапур|плов|рамен|вок|wok|паст|морепрод|рыб|риб|мяс|м.яс|десерт|морожен|морозив|вареник|борщ|пельмен|шаурм|шаверм"),
    ("тип закладу в місті/районі (кафе, бари, кав'ярні)", r"ресторан|кафе|бар|паб|pub|кофейн|кав.ярн|пиццер|піцер|клуб|столов|їдальн|ресторани|рестораны"),
]


def category(keyword):
    for name, rx in CATEGORIES:
        if re.search(rx, keyword, re.I):
            return name
    return "інше (назва закладу/бренд)"


ALL = defaultdict(list)


def num(row, *keys):
    for k in keys:
        v = row.get(k)
        if v not in (None, ""):
            try:
                return float(v)
            except (TypeError, ValueError):
                pass
    return 0.0


def pattern(url):
    """Шаблон адреси: змінні частини (slug, id) -> *, мовні префікси лишаються."""
    path = unquote(urlparse(url).path or "/")
    parts = []
    for seg in path.strip("/").split("/"):
        if not seg:
            continue
        if re.fullmatch(r"(uk|ua|ru|en)", seg) or (len(seg) <= 14 and not re.search(r"\d", seg) and seg.count("-") <= 1):
            parts.append(seg)
        else:
            parts.append("*")
    return "/" + "/".join(parts)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", type=int, default=100, help="топ сторінок кожного конкурента")
    ap.add_argument("--gap-pages", type=int, default=2, help="сторінок по 1000 запитів конкурента (топ-10, частота >= 50), яких немає в нас")
    ap.add_argument("--out", default="serpstat_report.json")
    args = ap.parse_args()
    if not TOKEN:
        sys.exit("Немає SERPSTAT_TOKEN / SERPSTAT_API_KEY")

    report = {"se": SE, "our": OUR, "domains": {}, "competitors": {}}

    info = call("SerpstatDomainProcedure.getDomainsInfo", {"domains": [OUR] + COMPETITORS, "se": SE})
    print("=== Домени (Google UA): видимість, ключових слів, трафік/міс ===")
    for d in rows(info):
        report["domains"][d.get("domain")] = d
        print(f"  {d.get('domain'):<16} видимість {d.get('visible', 0):>8}  слів {d.get('keywords', 0):>7}  "
              f"трафік {d.get('traff', 0):>8}  топ-10 {d.get('keywords_top', d.get('top10', '-'))}")

    our_patterns = set()
    try:
        ours = call("SerpstatDomainProcedure.getTopUrls", {"domain": OUR, "se": SE, "page": 1, "size": 200})
        our_patterns = {pattern(u.get("url", "")) for u in rows(ours)}
    except Exception as e:
        print(f"  наші сторінки: {e}")

    for comp in COMPETITORS:
        entry = {"top_urls": [], "patterns": {}, "gap": []}
        print(f"\n=== {comp} ===")
        try:
            urls = rows(call("SerpstatDomainProcedure.getTopUrls", {"domain": comp, "se": SE, "page": 1, "size": args.pages}))
            entry["top_urls"] = urls
            groups = defaultdict(lambda: {"pages": 0, "keywords": 0, "traffic": 0, "examples": []})
            for u in urls:
                g = groups[pattern(u.get("url", ""))]
                g["pages"] += 1
                g["keywords"] += int(num(u, "organic_keywords", "keywords"))
                g["traffic"] += num(u, "potencial_traff", "traff")
                if len(g["examples"]) < 2:
                    g["examples"].append(u.get("url"))
            entry["patterns"] = groups
            print("  Типи сторінок (шаблон | сторінок | слів | трафік | є в нас?):")
            for p, g in sorted(groups.items(), key=lambda x: -x[1]["traffic"])[:15]:
                print(f"    {p[:60]:<60} {g['pages']:>3} {g['keywords']:>6} {g['traffic']:>8.0f}  "
                      f"{'так' if p in our_patterns else '-'}  {g['examples'][0]}")
        except Exception as e:
            print(f"  топ сторінок: {e}")
        try:
            gap = []
            for page in range(1, args.gap_pages + 1):
                part = rows(call("SerpstatDomainProcedure.getDomainsUniqKeywords", {
                    "se": SE, "domains": [comp], "minusDomain": OUR, "page": page, "size": 1000,
                    "filters": {"position_to": 10, "queries_from": 50}}))
                gap += part
                if len(part) < 1000:
                    break
            gap = [k for k in gap if FOOD.search(k.get("keyword", ""))]
            gap.sort(key=lambda k: -num(k, "traff"))
            entry["gap"] = gap
            cats = defaultdict(list)
            for k in gap:
                cats[category(k.get("keyword", ""))].append(k)
            print(f"  Запити про їжу/заклади, де {comp} у топ-10, а нас немає: {len(gap)}")
            for cat, items in sorted(cats.items(), key=lambda x: -sum(num(k, 'traff') for k in x[1])):
                traff = sum(num(k, "traff") for k in items)
                vol = sum(num(k, "region_queries_count") for k in items)
                print(f"   [{cat}] запитів {len(items)}, частота {vol:.0f}/міс, трафік конкурента {traff:.0f}")
                for k in items[:8]:
                    print(f"      {k.get('keyword', '')[:50]:<50} поз {k.get('position', '-'):>2}  част {num(k, 'region_queries_count'):>6.0f}  "
                          f"{unquote(k.get('url') or '')[len('https://'):][:80]}")
                ALL[cat] += items
        except Exception as e:
            print(f"  унікальні запити: {e}")
        report["competitors"][comp] = entry

    print("\n=== Разом по всіх конкурентах: категорії запитів, яких у нас немає ===")
    for cat, items in sorted(ALL.items(), key=lambda x: -sum(num(k, 'traff') for k in x[1])):
        uniq = {k.get("keyword"): k for k in items}
        print(f"  [{cat}] унікальних запитів {len(uniq)}, частота {sum(num(k, 'region_queries_count') for k in uniq.values()):.0f}/міс, "
              f"трафік конкурентів {sum(num(k, 'traff') for k in items):.0f}")
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1, default=dict)
    print(f"\nПовні дані: {args.out}")


if __name__ == "__main__":
    main()

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
    ap.add_argument("--gap", type=int, default=300, help="ключових слів конкурента, яких немає в нас")
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
                g["keywords"] += int(num(u, "keywords", "keywords_count", "count_keywords", "keywords_count_top"))
                g["traffic"] += num(u, "traff", "traffic", "url_traff", "organic_traffic")
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
            gap = rows(call("SerpstatDomainProcedure.getDomainsUniqKeywords", {
                "se": SE, "domains": [comp], "minusDomain": OUR, "page": 1, "size": args.gap,
                "sort": {"traff": "desc"}}))
            entry["gap"] = gap
            print(f"  Запити, де {comp} є, а нас немає (топ за трафіком):")
            for k in gap[:40]:
                print(f"    {k.get('keyword', '')[:55]:<55} поз {k.get('position', '-'):>3}  част {k.get('region_queries_count', '-'):>6}  "
                      f"тр {num(k, 'traff', 'traffic'):>6.0f}  {(k.get('url') or '')[:70]}")
        except Exception as e:
            print(f"  унікальні запити: {e}")
        report["competitors"][comp] = entry

    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1, default=dict)
    print(f"\nПовні дані: {args.out}")


if __name__ == "__main__":
    main()

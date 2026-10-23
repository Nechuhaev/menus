"""Переклад рядків інтерфейсу (resx) через DeepL.

Джерело - *.ru.resx. Для uk та en перекладаються ключі, яких немає в цільовому файлі,
а також значення, що лишилися російськими (uk з літерами ы/э/ъ/ё або фраза без змін від ru;
en з кирилицею). Нічого не пише в репозиторій на сервері - лише друкує JSON, який
потім застосовується в репозиторії через --apply.

Запуск:  python3 translate_resx.py > resx.json             # DeepL, друкує переклади
         python3 translate_resx.py --apply resx.json       # записати в resx (без DeepL)
Змінні в .env: DEEPL_API_KEY.
"""
import glob
import html
import json
import os
import re
import sys

import requests
from dotenv import load_dotenv

load_dotenv(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))

RESOURCES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Menus.Web", "Resources")
TARGETS = {"uk": "UK", "en": "EN-US"}
DATA_RE = re.compile(r'(<data name="([^"]+)"[^>]*>\s*<value>)(.*?)(</value>)', re.S)
CYRILLIC = re.compile(r"[а-яіїєґё]", re.I)
RUSSIAN_ONLY = re.compile(r"[ыэъё]", re.I)


def read(path):
    raw = open(path, "rb").read()
    return raw.decode("utf-8-sig"), raw.startswith(b"\xef\xbb\xbf")


def write(path, text, bom):
    open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))


def values(text):
    return {m.group(2): html.unescape(m.group(3)) for m in DATA_RE.finditer(text)}


def needs_translation(lang, ru, value):
    if value is None:
        return True
    if lang == "uk":
        # однакові однослівні (Фото, Пароль) - норма, однакові фрази - неперекладені
        return bool(RUSSIAN_ONLY.search(value)) or (value == ru and " " in ru.strip() and bool(CYRILLIC.search(ru)))
    return bool(CYRILLIC.search(value))


def collect():
    todo = []
    for ru_path in sorted(glob.glob(os.path.join(RESOURCES, "*.ru.resx"))):
        base = os.path.basename(ru_path)[:-len(".ru.resx")]
        ru = values(read(ru_path)[0])
        for lang in TARGETS:
            path = os.path.join(RESOURCES, "%s.%s.resx" % (base, lang))
            target = values(read(path)[0]) if os.path.exists(path) else {}
            for key, text in ru.items():
                if text.strip() and needs_translation(lang, text, target.get(key)):
                    todo.append({"file": base, "lang": lang, "key": key, "ru": text, "old": target.get(key)})
    return todo


def deepl(texts, lang):
    key = os.environ["DEEPL_API_KEY"]
    url = "https://api-free.deepl.com" if key.endswith(":fx") else "https://api.deepl.com"
    out = []
    for i in range(0, len(texts), 50):
        batch = texts[i:i + 50]
        r = requests.post(url + "/v2/translate", timeout=60,
                          headers={"Authorization": "DeepL-Auth-Key " + key},
                          json={"text": batch, "source_lang": "RU", "target_lang": lang,
                                "tag_handling": "html", "context": "Інтерфейс сайту меню ресторанів"})
        r.raise_for_status()
        out += [t["text"] for t in r.json()["translations"]]
    return out


def translate():
    todo = collect()
    for lang, code in TARGETS.items():
        items = [t for t in todo if t["lang"] == lang]
        if items:
            for item, text in zip(items, deepl([t["ru"] for t in items], code)):
                item["new"] = text
    print("chars:", sum(len(t["ru"]) for t in todo), file=sys.stderr)
    json.dump(todo, sys.stdout, ensure_ascii=False, indent=1)


def apply(path):
    for item in json.load(open(path, encoding="utf-8")):
        file = os.path.join(RESOURCES, "%s.%s.resx" % (item["file"], item["lang"]))
        text, bom = read(file)
        value = html.escape(item["new"], quote=False)
        found = [False]

        def repl(m):
            if m.group(2) != item["key"]:
                return m.group(0)
            found[0] = True
            return m.group(1) + value + m.group(4)

        text = DATA_RE.sub(repl, text)
        if not found[0]:
            text = text.replace("</root>", '  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n</root>'
                                % (item["key"], value))
        write(file, text, bom)
        print("%s.%s %s: %s" % (item["file"], item["lang"], item["key"], item["new"]))


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--apply":
        apply(sys.argv[2])
    else:
        translate()

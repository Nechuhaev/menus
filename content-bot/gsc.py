"""Search Console API (сервісний акаунт, google-auth): статистика сторінок для скриптів бота.

Ключ - GSC_KEY_FILE у .env (за замовчуванням /root/gsc-key.json), ресурс - GSC_SITE (https://tipmymenu.com/).
Сервісний акаунт доданий у Search Console користувачем з «Обмеженим» доступом (лише читання).
"""
import os
from datetime import date, timedelta
from urllib.parse import quote

KEY_FILE = os.environ.get("GSC_KEY_FILE", "/root/gsc-key.json")
SITE = os.environ.get("GSC_SITE", "https://tipmymenu.com/")
PAGE_SIZE = 25000


def available():
    return os.path.exists(KEY_FILE)


def query(dimensions, days=28, end_offset=3):
    """[(ключі, кліки, покази, позиція)] за days днів до (сьогодні - end_offset): останні дні ще неповні."""
    from google.auth.transport.requests import AuthorizedSession
    from google.oauth2 import service_account

    credentials = service_account.Credentials.from_service_account_file(
        KEY_FILE, scopes=["https://www.googleapis.com/auth/webmasters.readonly"])
    session = AuthorizedSession(credentials)
    url = f"https://searchconsole.googleapis.com/webmasters/v3/sites/{quote(SITE, safe='')}/searchAnalytics/query"
    end = date.today() - timedelta(days=end_offset)
    rows, start_row = [], 0
    while True:
        resp = session.post(url, json={"startDate": str(end - timedelta(days=days - 1)), "endDate": str(end),
                                       "dimensions": dimensions, "rowLimit": PAGE_SIZE, "startRow": start_row}, timeout=60)
        resp.raise_for_status()
        batch = resp.json().get("rows", [])
        rows += [(r["keys"], r["clicks"], r["impressions"], r["position"]) for r in batch]
        if len(batch) < PAGE_SIZE:
            return rows
        start_row += PAGE_SIZE


def pages(days=28, end_offset=3):
    """[(адреса, кліки, покази, позиція)]"""
    return [(keys[0], clicks, impressions, position) for keys, clicks, impressions, position in query(["page"], days, end_offset)]

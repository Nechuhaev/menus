using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // SEO-дашборд: щоденні дані Search Console і GA4 (content-bot/seo_daily.py) - період проти такого самого
    // попереднього, графік по днях для всього сайту, виду сторінок чи однієї сторінки
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class SeoController : Controller
    {
        public const int Rows = 50;

        private readonly SeoDailyService _daily;

        public SeoController(SeoDailyService daily)
        {
            _daily = daily;
        }

        // period: 1, 7, 28, 90 або custom (from/to); list: gain, loss, new, top; url - графік однієї сторінки
        public async Task<IActionResult> Index(string period = "7", DateTime? from = null, DateTime? to = null,
            string kind = null, string list = "gain", string url = null)
        {
            ViewData["period"] = period;
            ViewData["kind"] = kind;
            ViewData["list"] = list;
            ViewData["page"] = url;
            var last = await _daily.GetLastDay();
            ViewData["last"] = last;
            if (last == null) return View(new List<SeoPagePeriod>());

            var range = SeoDailyService.Range(last.Value, period, from, to);
            ViewData["from"] = range.From;
            ViewData["to"] = range.To;
            ViewData["prevFrom"] = range.PrevFrom;
            ViewData["prevTo"] = range.PrevTo;

            var pages = await _daily.GetPages(range.From, range.To, range.PrevFrom, range.PrevTo);
            ViewData["totals"] = SeoDailyService.Totals(pages);
            ViewData["daily"] = await _daily.GetDaily(range.From, range.To, kind, url);

            var filtered = string.IsNullOrEmpty(kind) ? pages : pages.Where(x => x.Kind == kind).ToList();
            switch (list)
            {
                case "loss":
                    filtered = filtered.OrderBy(x => x.Clicks - x.PrevClicks).ThenBy(x => x.Impressions - x.PrevImpressions).ToList();
                    break;
                case "new":
                    filtered = filtered.Where(x => x.IsNew).OrderByDescending(x => x.Impressions).ToList();
                    break;
                case "top":
                    filtered = filtered.OrderByDescending(x => x.Impressions).ToList();
                    break;
                default:
                    filtered = filtered.OrderByDescending(x => x.Clicks - x.PrevClicks).ThenByDescending(x => x.Impressions - x.PrevImpressions).ToList();
                    break;
            }
            return View(filtered.Take(Rows).ToList());
        }
    }
}

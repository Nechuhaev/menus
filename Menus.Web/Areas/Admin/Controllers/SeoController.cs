using System;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // Тижневий звіт SEO з Search Console (content-bot/seo_weekly.py): тиждень проти попереднього
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class SeoController : Controller
    {
        public const int Rows = 50;

        private readonly SeoReportService _report;

        public SeoController(SeoReportService report)
        {
            _report = report;
        }

        // list: gain - найбільший ріст кліків/показів, loss - падіння, new - нові сторінки, top - найбільше показів
        public async Task<IActionResult> Index(DateTime? week, string kind, string list = "gain")
        {
            var weeks = await _report.GetWeeks();
            ViewData["weeks"] = weeks;
            ViewData["kind"] = kind;
            ViewData["list"] = list;
            if (!weeks.Any()) return View(new System.Collections.Generic.List<SeoPageWeek>());

            var current = week.HasValue && weeks.Contains(week.Value.Date) ? week.Value.Date : weeks.First();
            var previous = weeks.Where(x => x < current).Cast<DateTime?>().FirstOrDefault();
            var pages = await _report.GetPages(current, previous);
            ViewData["week"] = current;
            ViewData["previous"] = previous;
            ViewData["totals"] = SeoReportService.Totals(pages);

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

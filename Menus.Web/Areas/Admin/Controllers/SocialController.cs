using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Menus.Web.Areas.Admin.Controllers
{
    // «Соцмережі»: перегляди й активність постів Facebook / Instagram / TikTok, підсумки по мережах і рубриках,
    // JSON-імпорт свіжих цифр (формат - див. smm/README.md)
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class SocialController : Controller
    {
        private readonly SocialStatsService _social;

        public SocialController(SocialStatsService social)
        {
            _social = social;
        }

        // days: 7, 30, 90, 365; sort: date, views, er
        public async Task<IActionResult> Index(int days = 30, string network = null, string audience = null, string sort = "date")
        {
            if (!new[] { 7, 30, 90, 365 }.Contains(days)) days = 30;
            ViewData["days"] = days;
            ViewData["network"] = network;
            ViewData["audience"] = audience;
            ViewData["sort"] = sort;
            ViewData["last"] = await _social.LastUpdate();

            var posts = await _social.List(DateTime.UtcNow.Date.AddDays(-days), network, audience);
            ViewData["all"] = posts;
            IEnumerable<SocialPost> sorted;
            if (sort == "views") sorted = posts.OrderByDescending(x => x.Views);
            else if (sort == "er") sorted = posts.OrderByDescending(x => x.EngagementRate);
            else sorted = posts.OrderByDescending(x => x.PublishedAt);
            return View(sorted.ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(string json)
        {
            try
            {
                var posts = JsonConvert.DeserializeObject<List<SocialPost>>(json ?? "[]") ?? new List<SocialPost>();
                var saved = await _social.Upsert(posts);
                TempData["message"] = $"Імпортовано постів: {saved} з {posts.Count}";
            }
            catch (JsonException e)
            {
                TempData["message"] = "Помилка JSON: " + e.Message;
            }
            return RedirectToAction("Index");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // Сторінки «страва + місто»: кандидатів щоночі рахує content-bot, тут адмін вмикає потрібні
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class DishCityPagesController : Controller
    {
        private readonly Core _core;
        private readonly DishCityPageService _pages;
        private readonly SeoDailyService _daily;

        public DishCityPagesController(Core core, DishCityPageService pages, SeoDailyService daily)
        {
            _core = core;
            _pages = pages;
            _daily = daily;
        }

        // period: 7, 28, 90 або custom (from/to) - переходи на сторінки за період: Google (Search Console) і GA4
        public async Task<IActionResult> Index(string cityId, string period = "28", System.DateTime? from = null, System.DateTime? to = null)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            cityId = cityId ?? cities.FirstOrDefault(x => x.Name.Get("en").ToLower() == "kyiv")?.Id ?? cities.First().Id;
            var citySlug = cities.First(x => x.Id == cityId).Name.Get("en").ToLower();
            ViewData["cities"] = cities;
            ViewData["cityId"] = cityId;
            ViewData["citySlug"] = citySlug;
            ViewData["period"] = period;
            var last = await _daily.GetLastDay();
            ViewData["last"] = last;
            if (last != null)
            {
                var range = SeoDailyService.Range(last.Value, period, from, to);
                ViewData["from"] = range.From;
                ViewData["to"] = range.To;
                ViewData["stats"] = await _daily.GetDishCityStats(citySlug, range.From, range.To);
            }
            return View(await _pages.GetAllForCity(cityId));
        }

        // Власний SEO-текст сторінки (uk/ru/en); порожнє поле - автоматичний текст з даних
        public async Task<IActionResult> Edit(string id)
        {
            var dishPage = await _pages.GetById(id);
            if (dishPage == null) return NotFound();
            var city = (await _core.Restaurants.GetCities()).FirstOrDefault(x => x.Id == dishPage.CityId);
            ViewData["citySlug"] = city?.Name.Get("en").ToLower();
            return View(dishPage);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string uk, string ru, string en)
        {
            var dishPage = await _pages.GetById(id);
            if (dishPage == null) return NotFound();
            await _pages.SaveCustomText(id, new Dictionary<string, string> { ["uk"] = uk, ["ru"] = ru, ["en"] = en });
            TempData["saved"] = true;
            return RedirectToAction("Index", new { cityId = dishPage.CityId });
        }

        // shownIds - усі сторінки таблиці на екрані, enabledIds - з поставленою галочкою
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(string cityId, List<string> shownIds, List<string> enabledIds)
        {
            await _pages.SetEnabled(enabledIds ?? new List<string>(), shownIds ?? new List<string>());
            TempData["saved"] = true;
            return RedirectToAction("Index", new { cityId });
        }
    }
}

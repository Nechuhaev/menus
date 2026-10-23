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

        public DishCityPagesController(Core core, DishCityPageService pages)
        {
            _core = core;
            _pages = pages;
        }

        public async Task<IActionResult> Index(string cityId)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            cityId = cityId ?? cities.FirstOrDefault(x => x.Name.Get("en").ToLower() == "kyiv")?.Id ?? cities.First().Id;
            ViewData["cities"] = cities;
            ViewData["cityId"] = cityId;
            ViewData["citySlug"] = cities.First(x => x.Id == cityId).Name.Get("en").ToLower();
            return View(await _pages.GetAllForCity(cityId));
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

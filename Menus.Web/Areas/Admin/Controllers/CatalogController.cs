using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Menus.Web.Areas.Admin.Controllers
{
    // «Каталог»: сторінки міста за типами (/uk/kyiv/restaurants/bary) - показ, SEO-текст, переходи з Google/GA4,
    // тип і район кожного закладу (ручна зміна - бот venue_types.py її не перезаписує).
    // Закриті в «Ресторанах» / «Розсилці» (Status = Closed) у каталог не потрапляють.
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class CatalogController : Controller
    {
        private readonly Core _core;
        private readonly CatalogService _catalog;
        private readonly SeoDailyService _daily;

        public CatalogController(Core core, CatalogService catalog, SeoDailyService daily)
        {
            _core = core;
            _catalog = catalog;
            _daily = daily;
        }

        public async Task<IActionResult> Index(string cityId, string period = "28", DateTime? from = null, DateTime? to = null)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            cityId = cityId ?? cities.FirstOrDefault(x => x.Name.Get("en").ToLower() == "kyiv")?.Id ?? cities.First().Id;
            var citySlug = cities.First(x => x.Id == cityId).Name.Get("en").ToLower();
            var venues = await _catalog.GetAll(fresh: true);
            ViewData["cities"] = cities;
            ViewData["cityId"] = cityId;
            ViewData["citySlug"] = citySlug;
            ViewData["cityCounts"] = venues.GroupBy(x => x.City).ToDictionary(g => g.Key, g => g.Count());
            ViewData["settings"] = await _catalog.GetSettings();
            ViewData["period"] = period;
            var last = await _daily.GetLastDay();
            ViewData["last"] = last;
            if (last != null)
            {
                var range = SeoDailyService.Range(last.Value, period, from, to);
                ViewData["from"] = range.From;
                ViewData["to"] = range.To;
                ViewData["stats"] = await _daily.GetCatalogStats(citySlug, range.From, range.To);
            }
            return View(venues.Where(x => x.City == cityId).ToList());
        }

        // заклади міста (тип: null - усі) з типом і районом
        public async Task<IActionResult> Venues(string cityId, string type, string cuisine, string feature, string q, string chain = null)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var city = cities.FirstOrDefault(x => x.Id == cityId);
            if (city == null) return NotFound();
            var venues = (await _catalog.GetAll(fresh: true)).Where(x => x.City == cityId).ToList();
            ViewData["cityId"] = cityId;
            ViewData["cityName"] = city.Name.Get("uk");
            ViewData["citySlug"] = city.Name.Get("en").ToLower();
            ViewData["type"] = type;
            ViewData["cuisine"] = cuisine;
            ViewData["feature"] = feature;
            ViewData["q"] = q;
            ViewData["chain"] = chain;
            ViewData["chainName"] = string.IsNullOrEmpty(chain) ? null : CatalogService.Chains(venues).FirstOrDefault(x => x.Slug == chain).Name;
            // райони міста для вибору (slug -> назви)
            ViewData["districts"] = venues.Where(x => !string.IsNullOrEmpty(x.DistrictSlug)).GroupBy(x => x.DistrictSlug)
                .Select(g => g.First()).OrderBy(x => x.DistrictUk).ToList();
            IEnumerable<CatalogVenue> list = venues;
            if (!string.IsNullOrEmpty(type)) list = list.Where(x => x.Type == type);
            var cheapLimit = CatalogService.CheapLimit(venues);
            ViewData["cheapLimit"] = cheapLimit;
            if (!string.IsNullOrEmpty(feature)) list = list.Where(x => CatalogService.HasFeature(x, feature, cheapLimit));
            if (!string.IsNullOrEmpty(cuisine)) list = list.Where(x => cuisine == "-" ? !x.CuisineList.Any() : x.CuisineList.Contains(cuisine));
            if (!string.IsNullOrEmpty(chain)) list = list.Where(x => x.ChainSlug == chain);
            if (!string.IsNullOrWhiteSpace(q)) list = list.Where(x => (x.Name ?? "").IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            return View(list.OrderBy(x => x.Name).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetVenue(string restaurantId, string type, string district, List<string> cuisines, bool cuisinesPosted,
            List<string> features, bool featuresPosted, string returnUrl, string chain = null, bool chainPosted = false)
        {
            // незмінене поле не фіксуємо вручну - бот і далі оновлює його сам
            var current = (await _catalog.GetAll()).FirstOrDefault(x => x.Id == restaurantId);
            if (current != null && type == current.Type) type = null;
            if (current != null && district == (current.DistrictSlug ?? "")) district = null;
            // кухні - лише якщо форма їх надіслала і набір змінився
            List<string> newCuisines = null;
            if (cuisinesPosted)
            {
                newCuisines = (cuisines ?? new List<string>()).Where(c => CatalogService.Cuisines.ContainsKey(c)).Distinct().ToList();
                if (current != null && new HashSet<string>(newCuisines).SetEquals(current.CuisineList)) newCuisines = null;
            }
            List<string> newFeatures = null;
            if (featuresPosted)
            {
                newFeatures = (features ?? new List<string>()).Where(f => CatalogService.Features.ContainsKey(f) && f != "nedorogo").Distinct().ToList();
                if (current != null && new HashSet<string>(newFeatures).SetEquals(current.FeatureList)) newFeatures = null;
            }
            // мережа: назва вручну; та сама назва, що в іншої мережі міста, - приєднати до неї; порожньо - не мережа
            string chainSlug = null, chainName = null;
            if (chainPosted && current != null)
            {
                chainName = (chain ?? "").Trim();
                if (chainName == (current.ChainName ?? "").Trim()) chainName = null;
                else if (chainName == "") chainSlug = "";
                else
                {
                    var key = CatalogService.ChainNameKey(chainName);
                    var same = (await _catalog.GetAll()).FirstOrDefault(x => x.City == current.City && !string.IsNullOrEmpty(x.ChainSlug) &&
                        CatalogService.ChainNameKey(x.ChainName) == key);
                    if (same != null)
                    {
                        chainSlug = same.ChainSlug;
                        chainName = same.ChainName;
                    }
                    else
                    {
                        chainSlug = System.Text.RegularExpressions.Regex.Replace(UrlGenerateService.Translit(chainName), "[^a-z0-9-]", "").Trim('-');
                        if (chainSlug.Length > 128) chainSlug = chainSlug.Substring(0, 128);
                        if (chainSlug == "") chainSlug = null;
                    }
                }
            }
            string uk = null, ru = null, en = null, slug = null;
            if (district != null) // "" - прибрати район; slug - вибрати з наявних районів міста
            {
                slug = district;
                if (district != "")
                {
                    var sample = (await _catalog.GetAll()).FirstOrDefault(x => x.DistrictSlug == district);
                    uk = sample?.DistrictUk;
                    ru = sample?.DistrictRu;
                    en = sample?.DistrictEn;
                }
            }
            await _catalog.SetVenue(restaurantId, type, uk, ru, en, slug, newCuisines, newFeatures, chainSlug, chainName);
            TempData["message"] = type == null && district == null && newCuisines == null && newFeatures == null && chainSlug == null
                ? "Нічого не змінилось"
                : "Збережено: змінене вручну бот більше не перезаписує";
            return Url.IsLocalUrl(returnUrl) ? (IActionResult)Redirect(returnUrl) : RedirectToAction("Index");
        }

        public async Task<IActionResult> Page(string cityId, string type)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var city = cities.FirstOrDefault(x => x.Id == cityId);
            if (city == null) return NotFound();
            var settings = await _catalog.GetSettings(cityId, type);
            ViewData["cityName"] = city.Name.Get("uk");
            ViewData["citySlug"] = city.Name.Get("en").ToLower();
            if ((type ?? "").StartsWith("m:"))
                ViewData["chainName"] = CatalogService.Chains((await _catalog.GetCity(cityId))).FirstOrDefault(x => x.Slug == type.Substring(2)).Name;
            ViewData["texts"] = string.IsNullOrWhiteSpace(settings.CustomText)
                ? new Dictionary<string, string>()
                : JsonConvert.DeserializeObject<Dictionary<string, string>>(settings.CustomText) ?? new Dictionary<string, string>();
            return View(settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Page(string cityId, string type, bool enabled, string textUk, string textRu, string textEn)
        {
            var texts = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(textUk)) texts["uk"] = textUk.Trim();
            if (!string.IsNullOrWhiteSpace(textRu)) texts["ru"] = textRu.Trim();
            if (!string.IsNullOrWhiteSpace(textEn)) texts["en"] = textEn.Trim();
            await _catalog.SaveSettings(cityId, type ?? "", enabled, texts.Count == 0 ? null : JsonConvert.SerializeObject(texts));
            TempData["message"] = "Збережено. Сторінка на сайті оновиться протягом 30 хвилин (кеш).";
            return RedirectToAction("Index", new { cityId });
        }
    }
}

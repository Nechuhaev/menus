using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Helpers;
using Menus.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Menus.Web.Controllers
{
    public class CatalogViewModel
    {
        public string CityId { get; set; }
        public string CitySlug { get; set; }
        public string CityName { get; set; }
        public string Type { get; set; }               // null - усі заклади міста
        public string District { get; set; }           // slug фільтра
        public bool Cheap { get; set; }
        public string Sort { get; set; }               // popular | cheap | new
        public int PageIndex { get; set; }
        public int PageCount { get; set; }
        public int Total { get; set; }                 // закладів після фільтрів
        public List<CatalogVenue> AllOfType { get; set; } // тип без фільтрів (для вступу й FAQ)
        public List<CatalogVenue> Items { get; set; }  // поточна сторінка
        public List<(string Type, int Venues)> Types { get; set; }
        public int CityTotal { get; set; }
        public List<(string Slug, string Name, int Venues)> Districts { get; set; }
        public decimal CheapLimit { get; set; }
        public string CustomText { get; set; }
        public List<(string Slug, string Name)> OtherCities { get; set; }
        public bool HasDishPages { get; set; }
    }

    // Каталог міста: /uk/kyiv/restaurants (усі заклади) і /uk/kyiv/restaurants/bary (тип).
    // Фільтри району, «недорого» і сортування - ?district=&price=low&sort= (noindex, canonical - сторінка без фільтрів)
    public class CatalogController : Controller
    {
        public const int PageSize = 30;

        private readonly Core _core;
        private readonly CatalogService _catalog;
        private readonly DishCityPageService _dishPages;
        private readonly IConfiguration _config;

        public CatalogController(Core core, CatalogService catalog, DishCityPageService dishPages, IConfiguration config)
        {
            _core = core;
            _catalog = catalog;
            _dishPages = dishPages;
            _config = config;
        }

        [EdgeCache(1800)]
        public async Task<IActionResult> Index(string culture, string city, string type, string district, string price, string sort, int page = 1)
        {
            if ((culture != "ru" && culture != "uk" && culture != "en") ||
                (_config["DisableLanguages:uk"]?.ToLower() != "false" && culture == "uk") ||
                (_config["DisableLanguages:en"]?.ToLower() != "false" && culture == "en"))
            {
                return NotFoundView();
            }

            var cities = (await _core.Restaurants.GetCities()).ToList();
            var cityObj = cities.FirstOrDefault(x => x.Name.Get("en").ToLower() == city);
            if (cityObj == null) return NotFoundView();

            type = string.IsNullOrEmpty(type) ? null : type.ToLowerInvariant();
            var venues = await _catalog.GetCity(cityObj.Id);
            var ofType = type == null ? venues : venues.Where(x => x.Type == type).ToList();
            var settings = await _catalog.GetSettings(cityObj.Id, type);
            if (!CatalogService.IsPublished(type, ofType.Count, settings)) return NotFoundView();

            // фільтри
            var cheapLimit = CheapLimit(venues);
            IEnumerable<CatalogVenue> filtered = ofType;
            if (!string.IsNullOrEmpty(district)) filtered = filtered.Where(x => x.DistrictSlug == district);
            var cheap = price == "low" && cheapLimit > 0;
            if (cheap) filtered = filtered.Where(x => x.MedianPrice > 0 && x.MedianPrice <= cheapLimit);
            sort = sort == "cheap" || sort == "new" ? sort : "popular";
            switch (sort)
            {
                case "cheap":
                    filtered = filtered.OrderBy(x => x.MedianPrice <= 0).ThenBy(x => x.MedianPrice);
                    break;
                case "new":
                    filtered = filtered.OrderByDescending(x => x.CreateDate);
                    break;
                default:
                    filtered = filtered.OrderByDescending(x => x.Photo != null && x.Dishes > 0).ThenByDescending(x => x.Popularity).ThenBy(x => x.Name);
                    break;
            }
            var list = filtered.ToList();
            var pageCount = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
            if (page < 1 || page > pageCount) return NotFoundView();

            // місто сторінки, а не з куки: сторінка кешується однаковою для всіх гостей
            ViewData["pageCity"] = cityObj.Id;
            ViewData["selectedCity"] = cityObj.Id;
            ViewData["selectedCityName"] = city;
            // фільтри й сортування - не окремі сторінки для Google
            if (!string.IsNullOrEmpty(district) || cheap || sort != "popular")
            {
                ViewData["Robots"] = "noindex, follow";
            }

            var customJson = settings.CustomText;
            string customText = null;
            if (!string.IsNullOrWhiteSpace(customJson))
            {
                try
                {
                    var texts = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(customJson);
                    if (texts != null && texts.TryGetValue(culture, out var t) && !string.IsNullOrWhiteSpace(t)) customText = t.Trim();
                }
                catch (Exception)
                {
                    // некоректний JSON - текст з даних
                }
            }

            var model = new CatalogViewModel
            {
                CityId = cityObj.Id,
                CitySlug = city,
                CityName = cityObj.Name.Get(culture),
                Type = type,
                District = district,
                Cheap = cheap,
                Sort = sort,
                PageIndex = page,
                PageCount = pageCount,
                Total = list.Count,
                AllOfType = ofType,
                Items = list.Skip((page - 1) * PageSize).Take(PageSize).ToList(),
                Types = await _catalog.PublishedTypes(cityObj.Id),
                CityTotal = venues.Count,
                Districts = ofType.Where(x => !string.IsNullOrEmpty(x.DistrictSlug))
                    .GroupBy(x => x.DistrictSlug)
                    .Select(g => (Slug: g.Key, Name: g.First().District(culture), Venues: g.Count()))
                    .Where(x => x.Venues >= 2 && !string.IsNullOrEmpty(x.Name))
                    .OrderByDescending(x => x.Venues).Take(15).ToList(),
                CheapLimit = cheapLimit,
                CustomText = customText,
                HasDishPages = (await _dishPages.GetPublishedForCity(cityObj.Id)).Any(),
            };

            // інші міста з такою сторінкою (перелінковка)
            var allVenues = await _catalog.GetAll();
            var settingsAll = await _catalog.GetSettings();
            model.OtherCities = cities.Where(c => c.Id != cityObj.Id)
                .Select(c => (City: c, Count: allVenues.Count(v => v.City == c.Id && (type == null || v.Type == type))))
                .Where(x => CatalogService.IsPublished(type, x.Count, settingsAll.FirstOrDefault(s => s.CityId == x.City.Id && s.VenueType == (type ?? ""))))
                .OrderByDescending(x => x.Count)
                .Select(x => (Slug: x.City.Name.Get("en").ToLower(), Name: x.City.Name.Get(culture)))
                .ToList();

            return View("Index", model);
        }

        // «недорого»: типова ціна страви закладу в нижній третині міста
        public static decimal CheapLimit(List<CatalogVenue> venues)
        {
            var medians = venues.Where(x => x.MedianPrice > 0).Select(x => x.MedianPrice).OrderBy(x => x).ToList();
            return medians.Count < 9 ? 0 : medians[medians.Count / 3];
        }

        private IActionResult NotFoundView()
        {
            Response.StatusCode = 404;
            return View("NotFound");
        }
    }
}

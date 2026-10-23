using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Helpers;
using Menus.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Menus.Web.Controllers
{
    // Сторінки «страва + місто» (/uk/kyiv/dishes/borshch) і хаб міста (/uk/kyiv/dishes)
    public class DishCityController : Controller
    {
        private readonly Core _core;
        private readonly DishCityPageService _pages;
        private readonly IConfiguration _config;

        public DishCityController(Core core, DishCityPageService pages, IConfiguration config)
        {
            _core = core;
            _pages = pages;
            _config = config;
        }

        [EdgeCache(1800)]
        public async Task<IActionResult> Index(string culture, string city, string slug)
        {
            if ((culture != "ru" && culture != "uk" && culture != "en") ||
                (_config["DisableLanguages:uk"]?.ToLower() != "false" && culture == "uk") ||
                (_config["DisableLanguages:en"]?.ToLower() != "false" && culture == "en"))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var cityObj = (await _core.Restaurants.GetCities()).FirstOrDefault(x => x.Name.Get("en").ToLower() == city);
            if (cityObj == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            // місто сторінки, а не з куки: сторінка кешується однаковою для всіх гостей
            ViewData["pageCity"] = cityObj.Id;
            ViewData["selectedCity"] = cityObj.Id;
            ViewData["selectedCityName"] = city;
            ViewData["cityName"] = cityObj.Name.Get(culture);
            ViewData["citySlug"] = city;

            if (string.IsNullOrEmpty(slug))
            {
                return View("Hub", await _pages.GetPublishedForCity(cityObj.Id));
            }

            var page = await _pages.GetPublished(cityObj.Id, slug);
            if (page == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            ViewData["items"] = await _pages.GetItems(page, culture);
            ViewData["related"] = (await _pages.GetPublishedForCity(cityObj.Id)).Where(x => x.Id != page.Id).Take(12).ToList();
            return View("Page", page);
        }
    }
}

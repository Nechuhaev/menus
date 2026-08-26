using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace Menus.Web.Controllers
{
    [Produces("application/json")]
    public class TelegramBotController : Controller
    {
        private readonly IDbContext _db;
        private readonly Core _core;

        public TelegramBotController(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task<IActionResult> GetNearestRestaurants(string lat, string lon)
        {
            var restaurants = (await _core.Restaurants.FindNearest(lat, lon)).Take(5).ToList();

            if (restaurants.Count == 0) return Ok();

            return Ok(new
            {
                restaurants = JsonConvert.SerializeObject(restaurants)
            });
        }

        public async Task<IActionResult> GetCitiesForRestaurant(string name)
        {
            var seos = _db.RestaurantSeo.Where(x => x.SearchTerms.Contains(name)).ToList();
            if (!seos.Any()) return Ok();

            var seoRestaurants = seos.Select(x => x.ItemId);
            var restaurants = _db.Restaurants.Where(x => seoRestaurants.Contains(x.Id));
            var cities = _db.Cities.ToList();

            var resultCities = restaurants
                .GroupBy(x => x.City).ToList()
                .Select(x => cities.First(y => y.Id == x.Key).Name.Get()).ToList();

            return Ok(new
            {
                cities = JsonConvert.SerializeObject(resultCities)
            });
        }

        public async Task<IActionResult> GetRestaurants(string name, string city)
        {
            var seos = _db.RestaurantSeo.Where(x => x.SearchTerms.Contains(name)).ToList();
            if (!seos.Any()) return Ok();

            var seoRestaurants = seos.Select(x => x.ItemId);
            var cities = _db.Cities.ToList();
            var cityId = cities.First(x => x.Name.Get() == city).Id;
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seoRestaurants.Contains(x.Id) && x.City == cityId).ToList();

            return Ok(new
            {
                restaurants = JsonConvert.SerializeObject(restaurants)
            });
        }
    }
}
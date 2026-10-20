// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Domain.Dishes;
using Menus.Web.Helpers;
using Menus.Web.Models;
using Menus.Web.Models.ViewModels;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Controllers
{
    public partial class HomeController
    {
        public async Task<IActionResult> SelectCity(string cityId)
        {
            Response.Cookies.Append(
                ".Menus.SelectedCity",
                cityId,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
            );

            var cities = (await _core.Restaurants.GetCities()).ToList();

            var city = cities.First(x => x.Id == cityId);

            Response.Cookies.Append(
                ".Menus.SelectedCityName",
                city.Name.Get("en").ToLower(),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
            );

            return Ok();
        }

        public IActionResult SearchDish(string dish_q, string sort)
        {
            return RedirectToAction("SearchResult", new { q = dish_q, sort });
        }

        public async Task<IActionResult> SearchResult(string q, string sort, int page = 0)
        {
            if (string.IsNullOrEmpty(q))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            ViewData["featuredRestaurants"] = _core.Restaurants.GetCompleteRestaurants().Where(x => x.City == selectedCity).Take(5).ToList();
            CompleteDishesOrderBy rSort = CompleteDishesOrderBy.Rating;
            switch (sort)
            {
                case "rating":
                    rSort = CompleteDishesOrderBy.Rating;
                    break;
                case "rating-count":
                    rSort = CompleteDishesOrderBy.RatingsCount;
                    break;
                case "distance":
                    rSort = CompleteDishesOrderBy.Distance;
                    break;
            }
            var results = await _core.Dishes.GetCompleteDishes(q, lat, lon, selectedCity, rSort, page * 28, 28);
            results = DishSearchFormatter.AddCategoryToNames(results, q);

            if (!results.Any())
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            return page == 0 ? View(results) : View("_SearchResultAjax", results);
        }

        public async Task<IActionResult> DishAutocomplete(string q, string sortOrder)
        {
           CompleteDishesOrderBy dishSortOrder = CompleteDishesOrderBy.Rating;

            if (string.IsNullOrEmpty(q))
            {
                return BadRequest("q is empty");
            }

            switch (sortOrder)
            {
                case "rating":
                    dishSortOrder = CompleteDishesOrderBy.Rating;
                    break;
                case "rating-count":
                    dishSortOrder = CompleteDishesOrderBy.RatingsCount;
                    break;
                case "distance":
                    dishSortOrder = CompleteDishesOrderBy.Distance;
                    break;
            }

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            var result = (await _core.Dishes.GetCompleteDishes(q, lat, lon, selectedCity, dishSortOrder, 0, 10));
            result = DishSearchFormatter.AddCategoryToNames(result, q);

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            var resultModel = result.Where(x => x.Name.Get().IndexOf(q, StringComparison.InvariantCultureIgnoreCase) >= 0)
                .Select(x => new
                {
                    name = x.Name.Get(),
                    id = x.Name.Get(),
                    terms = x.SearchTerms,
                    location = $"{x.RestaurantName} - {x.RestaurantDisplayAddress.Get(culture)}",
                    distance = (x.RestaurantDistance?.ToString("F1") ?? "--") + " km",
                    rating = x.RatingFloat.ToString("F1"),
                    ratingPercent = x.Rating * 20
                }).ToList();

            return Ok(resultModel);
        }

        public async Task<IActionResult> RestaurantsAutocomplete(string q)
        {
            if (string.IsNullOrEmpty(q)) return BadRequest("q is empty");

            //q = MySql.Data.MySqlClient.MySqlHelper.EscapeString(q);

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            var seos = (await _core.Restaurants.FindRestaurantsAutocomplete(q, selectedCity)).ToList();
            var seosIds = seos.Select(x => x.ItemId);
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seosIds.Contains(x.Id)).ToList();
            var result = seos.Where(x => restaurants.Any(y => y.Id == x.ItemId)).Select(seo =>
            {
                var restaurant = restaurants.FirstOrDefault(x => x.Id == seo.ItemId);

                string distance = null;
                double distanceDouble = 0;

                if (restaurant.GpsLatitude != null)
                {
                    var udLat = Convert.ToDouble(lat, new CultureInfo("en-US"));
                    var udLon = Convert.ToDouble(lon, new CultureInfo("en-US"));

                    var rdLat = Convert.ToDouble(restaurant.GpsLatitude, new CultureInfo("en-US"));
                    var rdLon = Convert.ToDouble(restaurant.GpsLongitude, new CultureInfo("en-US"));

                    var userCoord = new GeoCoordinate(udLat, udLon);
                    var restaurantCoord = new GeoCoordinate(rdLat, rdLon);

                    distanceDouble = userCoord.GetDistanceTo(restaurantCoord) / 1000;
                    distance = (distanceDouble).ToString("F1") + " km";
                }

                //var bestDish = await _core.Dishes.GetBestDishForRestaurant(restaurant.Id);
                //bestDish = bestDish ?? new DishWithSeoAndRatings();

                return new
                {
                    name = restaurant.Name,
                    location = restaurant.DisplayAddress.Get(culture),
                    id = seo.Url,
                    terms = seo.SearchTerms,
                    distance,
                    distanceDouble
                    //rating = bestDish.RatingFloat.ToString("F1"),
                    //ratingPercent = bestDish.Rating * 20
                };
            }).ToList();

            return Ok(result.OrderBy(x => x.distanceDouble));
        }

        public async Task<IActionResult> GetCityRestaurants(string cityId)
        {
            var cityRestaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => x.City == cityId).ToList());

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            return Ok(cityRestaurants.Select(x => new { name = $"{x.Name} ({x.DisplayAddress.Get(culture)})", id = x.Url }).ToList());
        }

        public async Task<IActionResult> GetCities(string culture)
        {
            var cities = await _core.Restaurants.GetCities();

            return Ok(cities.OrderBy(x => x.Name.Get(culture)).Select(x => new { name = $"{x.Name.Get(culture)}", id = x.Id, terms = "" }).ToList());
        }

        public IActionResult FindRestaurant(string q, string q_id)
        {
            return RedirectToAction("Restaurant", new { id = string.IsNullOrEmpty(q_id) ? q : q_id });
        }

        public async Task<IActionResult> FindNearestRestaurants(string lat, string lon)
        {
            GetSelectedCityIdAndSetSelectedCityCookie();
            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon)) return Ok(new List<string>());
            var nearbyRestaurants = await _core.Restaurants.FindNearest(lat, lon);

            await LogEvent("UserPosition", new { latitude = lat, longitude = lon });

            return View("_NearestRestaurants", nearbyRestaurants.Take(10).ToList());
        }
    }
}

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
        public async Task<IActionResult> BestDishes(int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            var dishes = (await _core.Dishes.GetBestDishes(selectedCity, 12, page * 12)).ToList();

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            return page == 0 ? View(dishes) : View("_BestDishesAjax", dishes);
        }

        public async Task<IActionResult> Newest(string sort, int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            //var latestRestaurants = (await  _core.Restaurants.GetActiveRestaurants()).Where(x => x.City == selectedCity).OrderByDescending(x => x.CreateDate).Skip(page * 12).Take(12);
            //var latestRestaurantIds = latestRestaurants.Select(x => x.Id).ToList();

            //IEnumerable<ICompleteRestaurant> restaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => latestRestaurantIds.Contains(x.Id)).ToList());
            var restarauntsCreatedByAdmin = (await Task.Run(() => _core.Restaurants.GetRestaurantCreatedByAdmin())).Select(x => x.Id).ToList();


            var latestAddedRestaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants()
                //.Where(x => !string.IsNullOrEmpty(x.Photos) && x.Photos.Length > 4)
                .Where(x => x.City == selectedCity && restarauntsCreatedByAdmin.Contains(x.Id))
                .OrderByDescending(x => x.ShowOnIndex)
                .ThenByDescending(x => x.CreateDate)
                .Skip(page * 12)
                .Take(12)
                .ToList());

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            return page == 0 ? View(latestAddedRestaurants) : View("_FeaturedAndNearestAjax", latestAddedRestaurants);
        }

        public async Task<IActionResult> Featured(string sort, int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            IEnumerable<ICompleteRestaurant> restaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => x.IsFeatured && x.City == selectedCity).ToList());

            switch (sort)
            {
                case "rating-count":
                    restaurants = restaurants.OrderByDescending(x => x.RatingsCount);
                    break;

                case "rating":
                    restaurants = restaurants.Select(x =>
                    {
                        var bestDish = _core.Dishes.GetBestDishForRestaurant(x.Id).Result;
                        bestDish = bestDish ?? new DishWithSeoAndRatings();

                        return new
                        {
                            restaurant = x,
                            rating = bestDish.RatingFloat
                        };
                    }).OrderByDescending(x => x.rating).Select(x => x.restaurant);
                    break;
            }

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            restaurants = restaurants.Skip(page * 12).Take(12);

            return page == 0 ? View(restaurants) : View("_FeaturedAndNearestAjax", restaurants);
        }

        public async Task<IActionResult> Nearest(string sort, int page = 0)
        {
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

            IEnumerable<ICompleteRestaurant> restaurants = (await _core.Restaurants.FindNearest(lat, lon)).ToList();

            switch (sort)
            {
                case "rating-count":
                    restaurants = restaurants.OrderByDescending(x => x.RatingsCount);
                    break;

                case "rating":
                    restaurants = restaurants.Select(x =>
                    {
                        var bestDish = _core.Dishes.GetBestDishForRestaurant(x.Id).Result;
                        bestDish = bestDish ?? new DishWithSeoAndRatings();

                        return new
                        {
                            restaurant = x,
                            rating = bestDish.RatingFloat
                        };
                    }).OrderByDescending(x => x.rating).Select(x => x.restaurant);
                    break;
            }

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            restaurants = restaurants.Skip(page * 12).Take(12);

            return page == 0 ? View(restaurants) : View("_FeaturedAndNearestAjax", restaurants);
        }
    }
}

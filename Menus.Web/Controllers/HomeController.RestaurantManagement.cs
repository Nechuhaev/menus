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
        [HttpGet]
        [Authorize]
        public IActionResult RegisterRestaurant(string name)
        {
            ViewData["name"] = name;
            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> EditRestaurant(string id)
        {
            var restaurantRole = await _core.Restaurants.GetUserRoleForRestaurant(id, User.Identity.Name);
            if (restaurantRole == null)
                return RedirectToAction("Index");

            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(id);
            restaurant.Email = restaurantInfo.Cuisine.Get();
            return View(restaurant);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditRestaurant(Restaurant model, string culture)
        {
            culture = string.IsNullOrEmpty(culture) ? "ru" : culture;
            var cuisine = model.Email;

            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Empty name");

            var restaurantRole = await _core.Restaurants.GetUserRoleForRestaurant(model.Id, User.Identity.Name);
            if (restaurantRole == null)
                return RedirectToAction("Index");

            var restaurant = await _core.Restaurants.GetRestaurantById(model.Id);
            restaurant.Name = model.Name;
            restaurant.City = model.City;
            restaurant.Address = model.Address;
            restaurant.Phone = model.Phone;
            await _core.Restaurants.Update(restaurant);

            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(model.Id);
            restaurantInfo.Cuisine = string.IsNullOrEmpty(cuisine) ? new LocalizableString() : new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = cuisine, Locale = culture } } };
            restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = culture } } };
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);

            var seo = await _core.Restaurants.GetSeoByRestaurantId(model.Id);
            seo.Url = _urls.GetNewUrlForRestaurant(seo, restaurant);
            await _core.Restaurants.UpdateSeo(seo);

            return RedirectToAction("Restaurant", new { id = model.Id });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RegisterRestaurant(Restaurant model, string culture)
        {
            culture = string.IsNullOrEmpty(culture) ? "ru" : culture;
            var cuisine = model.Email;
            model.Email = "";

            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Empty name");

            var autoApproveUserRestaurants = _config["AutoApproveUserRestaurants"] == "true";

            model.Id = Guid.NewGuid().ToString();
            model.Status = autoApproveUserRestaurants ? RestaurantStatus.Active : RestaurantStatus.Pending;
            model.CreateDate = DateTime.Now;

            await _core.Restaurants.Create(model, new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                Role = RestaurantUserRole.Owner,
                UserId = User.Identity.Name
            });

            await _core.Restaurants.CreateRestaurantRole(new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                UserId = User.Identity.Name,
                Role = RestaurantUserRole.Creator
            });

            var restaurantInfo = new RestaurantInfo() as IRestaurantInfo;
            restaurantInfo.Id = Guid.NewGuid().ToString();
            restaurantInfo.Currency = "₴";
            restaurantInfo.Cuisine = string.IsNullOrEmpty(cuisine) ? new LocalizableString() : new LocalizableString{Values = new List<LocalizableStringValue>{ new LocalizableStringValue{ Value = cuisine, Locale = culture } }};
            restaurantInfo.FullInfo = new LocalizableString();
            restaurantInfo.ShortInfo = new LocalizableString();
            restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = culture } } };
            restaurantInfo.RestaurantId = model.Id;
            restaurantInfo.OpenHours = "{\"perDay\":false,\"data\":[{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true}]}";
            restaurantInfo.UserAllowedAddDishes = true;
            restaurantInfo.UserAllowedAddMenu = true;
            restaurantInfo.UserAllowedOpenMenu = true;
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);
            

            var seo = new RestaurantSeo() as IRestaurantSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.Title = new LocalizableString();
            seo.Header = new LocalizableString();
            seo.Description = new LocalizableString();
            seo.ItemId = model.Id;
            seo.Url = _urls.GetNewUrlForRestaurant(seo, model);
            seo.SearchTerms = model.Name + " " + seo.Url;
            await _core.Restaurants.CreateSeo(seo);

            return RedirectToAction("Restaurant", new { id = model.Id });
        }

        [Authorize]
        public async Task<IActionResult> CreateDish(string id)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            ViewData["selectedCity"] = selectedCity;

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            if (restaurant == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            UpdateLatestRestaurants(id, HttpContext);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurant.Id);

            //var reviews = await _core.Ratings.GetRatingForDish();
            if (!_userService.IsMobileUser(Request))
            {
                var menu = await GetMenuForRestaurant(restaurant);
                ViewData["menu"] = menu;
            }

            ViewData["restaurantId"] = id;
            ViewData["restaurant"] = restaurant;
            ViewData["restaurantInfo"] = restaurantInfo;

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadDishPhoto";
            ViewData["valueName"] = "dishId";

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDish(UserCreateDishViewModel model)
        {
            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(model.RestaurantId);
            if (restaurant == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Name is empty");

            if (model.Unit == null) model.Unit = " ";

            var dish = (IDish) new Dish();
            dish.Name = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Locale = model.Locale, Value = model.Name } } };
            dish.Description = new LocalizableString();
            dish.Id = Guid.NewGuid().ToString();
            dish.Ingredients = new LocalizableString();
            dish.IsPriceMultiple = false;
            dish.Photos = new List<string>();
            dish.Price = Convert.ToDecimal(model.Price);
            dish.RestaurantId = model.RestaurantId;
            dish.Status = DishStatus.Active;
            dish.Weight = model.Weight.ToString();
            dish.WeightType = model.Unit;
            dish.ShortDescription = new LocalizableString();

            await _core.Dishes.Create(dish);

            var price = new DishVariation
            {
                Id = Guid.NewGuid().ToString(),
                DishId = dish.Id,
                Price = dish.Price,
                Weight = model.Weight,
                Type = model.Unit
            };
            await _core.Dishes.CreateDishVariations(new List<IDishVariation> {price});

            var rating = new Rating
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.RestaurantId,
                Status = RatingStatus.Approved,
                Text = "",
                DishId = dish.Id,
                UserId = User.Identity.Name,
                Rate = Convert.ToInt32(model.Rating),
                UpdateDate = DateTime.Now
            };
            if (rating.Rate > 0)
            {
                await _core.Ratings.Create(rating);
                dish.RatingFloat = rating.Rate;
                dish.RatingsCount = 1;
                await _core.Dishes.Update(dish);
            }

            var seo = new DishSeo() as IDishSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.ItemId = dish.Id;
            seo.RestaurantId = dish.RestaurantId;
            seo.Url = await _urls.GetNewUrlForDish(seo, dish);
            seo.SearchTerms = dish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(dish.Name.Get());
            await _core.Dishes.CreateSeo(seo);

            if(!string.IsNullOrEmpty(model.Photo)) await UploadDishPhoto(model.Photo, dish.Id);

            await LogEvent("CreateDish", new { id = dish.Id, name = dish.Name.Get() });

            return RedirectToAction("Restaurant", new { id = restaurant.Id });
        }
    }
}

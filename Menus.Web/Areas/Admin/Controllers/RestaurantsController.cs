// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "admin, manager")]
    public class RestaurantsController : Controller
    {
        private readonly Core _core;

        public RestaurantsController(Core core)
        {
            _core = core;
        }

        public async Task<IActionResult> Index(string q, RestaurantStatus? status, RestaurantCreatedBy? createdBy, int page = 0)
        {
            int pageSize = 20;
            var restaurants = (await _core.Restaurants.Search(q, status, createdBy, pageSize, page)).ToList();

            ViewData["q"] = q;
            ViewData["page"] = page;
            ViewData["status"] = status;
            ViewData["createdBy"] = createdBy;
            ViewData["pageSize"] = pageSize;

            return View(restaurants);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateCreateDate(string id, DateTime createDate)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            restaurant.CreateDate = createDate;
            await _core.Restaurants.Update(restaurant);


            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> ToggleModerated(string id)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            restaurant.Moderated = !restaurant.Moderated;
            await _core.Restaurants.Update(restaurant);


            return Ok();
        }

        public async Task<IActionResult> Edit(string id)
        {
            var userId = await _core.Restaurants.GetRestaurantOwnerId(id);
            Response.Cookies.Append(".Menus.SelectedRestaurant", id, new Microsoft.AspNetCore.Http.CookieOptions { HttpOnly = true, IsEssential = true });
            Response.Cookies.Append(".Menus.UserId", userId ?? "1a950ec5-7fd3-4d82-adca-d27fda6025b5", new Microsoft.AspNetCore.Http.CookieOptions { HttpOnly = true, IsEssential = true });

            return RedirectToAction("Index", "About", new { area = "Restaurants", restaurantId = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Copy(string id)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            restaurant = restaurant.Clone() as IRestaurant;
            restaurant.Id = Guid.NewGuid().ToString();
            restaurant.Name = restaurant.Name + " (Copy)";
            restaurant.CreateDate = DateTime.Now;

            var restaurantSeo = await _core.Restaurants.GetSeoByRestaurantId(id);
            restaurantSeo = restaurantSeo.Clone() as IRestaurantSeo;
            restaurantSeo.Id = Guid.NewGuid().ToString();
            restaurantSeo.ItemId = restaurant.Id;
            restaurantSeo.Url = restaurantSeo.Url + "-copy";

            var restaurantInfo = (await _core.Restaurants.GetRestaurantInfo(id)).Clone() as IRestaurantInfo;
            restaurantInfo.Id = Guid.NewGuid().ToString();
            restaurantInfo.RestaurantId = restaurant.Id;

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(id)).Select(x => x.Clone() as IMenuCategory).ToList();
            var dishes = (await _core.Dishes.GetDishesForRestaurant(id)).Select(x => x.Clone() as IDish).ToList();
            var dishCategories = new List<IDishCategory>();
            var dishSeos = new List<IDishSeo>();
            var dishVariations = new List<IDishVariation>();
            foreach (var dish in dishes)
            {
                var dishCategory = (await _core.Dishes.GetDishCategories(dish.Id)).ToList();
                var variations = (await _core.Dishes.GetDishVariations(dish.Id)).Select(x => (IDishVariation)x.Clone()).ToList();

                var dishSeo = (await _core.Dishes.GetSeoByDishId(dish.Id));
                dish.Id = Guid.NewGuid().ToString();
                dish.RestaurantId = restaurant.Id;
                if (dishSeo != null)
                {
                    dishSeo = dishSeo.Clone() as IDishSeo;

                    dishSeo.Id = Guid.NewGuid().ToString();
                    dishSeo.RestaurantId = restaurant.Id;
                    dishSeo.ItemId = dish.Id;
                    dishSeo.Url = dishSeo.Url + "-copy";
                }

                foreach (var variation in variations)
                {
                    variation.Id = Guid.NewGuid().ToString();
                    variation.DishId = dish.Id;
                    dishVariations.Add(variation);
                }

                dishCategory = dishCategory.Select(x => {
                    var clone = x.Clone() as IDishCategory;
                    clone.Id = Guid.NewGuid().ToString();
                    clone.DishId = dish.Id;

                    return clone;
                }).ToList();

                if (dishSeo != null) dishSeos.Add(dishSeo);
                dishCategories.AddRange(dishCategory);
            }

            foreach (var category in categories)
            {
                var newid = Guid.NewGuid().ToString();
                foreach (var dishCategory in dishCategories.Where(x => x.MenuCategoryId == category.Id))
                {
                    dishCategory.MenuCategoryId = newid;
                }
                category.Id = newid;
                category.RestaurantId = restaurant.Id;
            }

            await _core.Restaurants.Create(restaurant, new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = restaurant.Id,
                Role = RestaurantUserRole.Owner,
                UserId = User.Identity.Name
            });
            await _core.Restaurants.CreateSeo(restaurantSeo);
            await _core.Restaurants.CreateMenuCategories(categories);
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);
            await _core.Dishes.Create(dishes);
            await _core.Dishes.CreateDishVariations(dishVariations);
            await _core.Dishes.CreateDishCategories(dishCategories);
            await _core.Dishes.CreateSeo(dishSeos);

            await LogEvent("CopyRestaurant", new { id, newId = restaurant.Id });

            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> ChangeStatus(string id, RestaurantStatus status)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            restaurant.Status = status;
            await _core.Restaurants.Update(restaurant);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            await _core.Restaurants.Delete(id);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> DeleteSeveral(IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                await _core.Restaurants.Delete(id);
            }
            return Ok();
        }

        private async Task LogEvent(string name, object data)
        {
            var logItem = new LogItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = User.Identity.Name,
                DeviceId = Request.Cookies[".Menus.DeviceId"],
                Event = name,
                EventData = JsonConvert.SerializeObject(data),
                Timestamp = DateTime.Now
            };
            await _core.LogItems.Create(logItem);
        }
    }
}
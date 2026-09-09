// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Data;
using Menus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly EfDbContext _dbContext;

        public RestaurantsController(Core core, EfDbContext dbContext)
        {
            _core = core;
            _dbContext = dbContext;
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
            ViewData["restaurantMeta"] = GetRestaurantMeta();

            return View(restaurants);
        }

        private Dictionary<string, (bool SeoFilled, DateTime? LastChecked, string Website)> GetRestaurantMeta()
        {
            var result = new Dictionary<string, (bool, DateTime?, string)>();
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                var wasClosed = connection.State != ConnectionState.Open;
                if (wasClosed) connection.Open();

                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"SELECT rs.ItemId, rs.Title, rs.Description, rs.Keywords, l.LastCheckedAt, ri.Website
                                                 FROM RestaurantSeo rs
                                                 JOIN RestaurantInfo ri ON ri.RestaurantId = rs.ItemId
                                                 LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = rs.ItemId";
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var restaurantId = reader.GetString(0);
                                var title = reader.IsDBNull(1) ? null : reader.GetString(1);
                                var description = reader.IsDBNull(2) ? null : reader.GetString(2);
                                var keywords = reader.IsDBNull(3) ? null : reader.GetString(3);
                                var lastChecked = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
                                var website = reader.IsDBNull(5) ? null : reader.GetString(5);

                                var seoFilled = IsLocalizableFilled(title) && IsLocalizableFilled(description) && !string.IsNullOrWhiteSpace(keywords);
                                result[restaurantId] = (seoFilled, lastChecked, website);
                            }
                        }
                    }
                }
                finally
                {
                    if (wasClosed) connection.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetRestaurantMeta FAILED: {ex}");
            }
            return result;
        }

        private static bool IsLocalizableFilled(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var parsed = JsonConvert.DeserializeObject<LocalizableString>(json);
                return parsed?.Values?.Any(v => !string.IsNullOrWhiteSpace(v.Value)) == true;
            }
            catch
            {
                return false;
            }
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

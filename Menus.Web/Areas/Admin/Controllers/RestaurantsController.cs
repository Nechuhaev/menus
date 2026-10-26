// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Data;
using Menus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Menus.Web.Areas.Admin.Controllers
{
    public class RestaurantAdminRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Website { get; set; }
        public bool SeoFilled { get; set; }
        public DateTime? LastChecked { get; set; }
        public string SyncNote { get; set; }
        public RestaurantStatus Status { get; set; }
        public DateTime CreateDate { get; set; }
        public bool Moderated { get; set; }
    }

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

                public async Task<IActionResult> Index(
            string nameFilter, string phoneFilter, string addressFilter, string siteFilter,
            string seoFilter, string freshnessFilter, string moderatedFilter, DateTime? createdDateFilter,
            RestaurantStatus? status, RestaurantCreatedBy? createdBy, int page = 0)
        {
            int pageSize = 20;
            var restaurants = GetFilteredRestaurants(
                nameFilter, phoneFilter, addressFilter, siteFilter,
                seoFilter, freshnessFilter, moderatedFilter, createdDateFilter,
                status, createdBy, pageSize, page);
            var totalCount = GetFilteredRestaurantsCount(
                nameFilter, phoneFilter, addressFilter, siteFilter,
                seoFilter, freshnessFilter, moderatedFilter, createdDateFilter,
                status, createdBy);

            ViewData["nameFilter"] = nameFilter;
            ViewData["phoneFilter"] = phoneFilter;
            ViewData["addressFilter"] = addressFilter;
            ViewData["siteFilter"] = siteFilter;
            ViewData["seoFilter"] = seoFilter;
            ViewData["freshnessFilter"] = freshnessFilter;
            ViewData["moderatedFilter"] = moderatedFilter;
            ViewData["createdDateFilter"] = createdDateFilter;
            ViewData["page"] = page;
            ViewData["status"] = status;
            ViewData["createdBy"] = createdBy;
            ViewData["pageSize"] = pageSize;
            ViewData["totalCount"] = totalCount;
            ViewData["totalPages"] = (int)Math.Ceiling(totalCount / (double)pageSize);

            return View(restaurants);
        }

        private List<string> BuildFilterConditions(
            System.Data.Common.DbCommand command,
            string nameFilter, string phoneFilter, string addressFilter, string siteFilter,
            string seoFilter, string freshnessFilter, string moderatedFilter, DateTime? createdDateFilter,
            RestaurantStatus? status, RestaurantCreatedBy? createdBy, out string joinCreatedBy)
        {
            var conditions = new List<string>();

            void AddParam(string name, object value)
            {
                var p = command.CreateParameter();
                p.ParameterName = name;
                p.Value = value;
                command.Parameters.Add(p);
            }

            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                conditions.Add("r.Name LIKE @nameFilter");
                AddParam("@nameFilter", $"%{nameFilter}%");
            }
            if (!string.IsNullOrWhiteSpace(phoneFilter))
            {
                conditions.Add("r.Phone LIKE @phoneFilter");
                AddParam("@phoneFilter", $"%{phoneFilter}%");
            }
            if (!string.IsNullOrWhiteSpace(addressFilter))
            {
                conditions.Add("r.Address LIKE @addressFilter");
                AddParam("@addressFilter", $"%{addressFilter}%");
            }
            if (!string.IsNullOrWhiteSpace(siteFilter))
            {
                conditions.Add("ri.Website LIKE @siteFilter");
                AddParam("@siteFilter", $"%{siteFilter}%");
            }
            if (seoFilter == "filled")
            {
                conditions.Add(@"(rs.Title REGEXP '""Value"": ""[^""]' AND rs.Description REGEXP '""Value"": ""[^""]' AND rs.Keywords IS NOT NULL AND rs.Keywords != '')");
            }
            else if (seoFilter == "empty")
            {
                conditions.Add(@"NOT (rs.Title REGEXP '""Value"": ""[^""]' AND rs.Description REGEXP '""Value"": ""[^""]' AND rs.Keywords IS NOT NULL AND rs.Keywords != '')");
            }
            if (freshnessFilter == "never")
            {
                conditions.Add("l.LastCheckedAt IS NULL");
            }
            else if (freshnessFilter == "stale")
            {
                conditions.Add("(l.LastCheckedAt IS NULL OR l.LastCheckedAt < DATE_SUB(NOW(), INTERVAL 30 DAY))");
            }
            else if (freshnessFilter == "recent")
            {
                conditions.Add("l.LastCheckedAt >= DATE_SUB(NOW(), INTERVAL 30 DAY)");
            }
            if (status != null)
            {
                conditions.Add("r.Status = @status");
                AddParam("@status", (int)status.Value);
            }
            if (createdDateFilter != null)
            {
                conditions.Add("DATE(r.CreateDate) = @createdDate");
                AddParam("@createdDate", createdDateFilter.Value.Date);
            }
            if (moderatedFilter == "1")
            {
                conditions.Add("r.Moderated = 1");
            }
            else if (moderatedFilter == "0")
            {
                conditions.Add("r.Moderated = 0");
            }

            joinCreatedBy = "";
            if (createdBy == RestaurantCreatedBy.Admin)
            {
                joinCreatedBy = "LEFT JOIN (SELECT COUNT(*) AS Cnt, RestaurantId FROM MenuCategories GROUP BY RestaurantId) cc ON cc.RestaurantId = r.Id";
                conditions.Add("cc.Cnt > 0");
            }
            else if (createdBy != null)
            {
                joinCreatedBy = "LEFT JOIN (SELECT COUNT(*) AS Cnt, RestaurantId FROM MenuCategories GROUP BY RestaurantId) cc ON cc.RestaurantId = r.Id";
                conditions.Add("cc.Cnt IS NULL");
            }

            return conditions;
        }

        private int GetFilteredRestaurantsCount(
            string nameFilter, string phoneFilter, string addressFilter, string siteFilter,
            string seoFilter, string freshnessFilter, string moderatedFilter, DateTime? createdDateFilter,
            RestaurantStatus? status, RestaurantCreatedBy? createdBy)
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                var wasClosed = connection.State != ConnectionState.Open;
                if (wasClosed) connection.Open();

                try
                {
                    var command = connection.CreateCommand();
                    var conditions = BuildFilterConditions(
                        command, nameFilter, phoneFilter, addressFilter, siteFilter,
                        seoFilter, freshnessFilter, moderatedFilter, createdDateFilter,
                        status, createdBy, out var joinCreatedBy);

                    var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

                    command.CommandText = $@"
                        SELECT COUNT(*)
                        FROM Restaurants r
                        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                        LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
                        {joinCreatedBy}
                        {whereClause}";

                    var result = command.ExecuteScalar();
                    return Convert.ToInt32(result);
                }
                finally
                {
                    if (wasClosed) connection.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetFilteredRestaurantsCount FAILED: {ex}");
                return 0;
            }
        }

        private List<RestaurantAdminRow> GetFilteredRestaurants(
            string nameFilter, string phoneFilter, string addressFilter, string siteFilter,
            string seoFilter, string freshnessFilter, string moderatedFilter, DateTime? createdDateFilter,
            RestaurantStatus? status, RestaurantCreatedBy? createdBy, int pageSize, int page)
        {
            var result = new List<RestaurantAdminRow>();
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                var wasClosed = connection.State != ConnectionState.Open;
                if (wasClosed) connection.Open();

                try
                {
                    var command = connection.CreateCommand();
                    var conditions = BuildFilterConditions(
                        command, nameFilter, phoneFilter, addressFilter, siteFilter,
                        seoFilter, freshnessFilter, moderatedFilter, createdDateFilter,
                        status, createdBy, out var joinCreatedBy);

                    var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

                    command.CommandText = $@"
                        SELECT r.Id, r.Name, r.Phone, r.Address, r.Status, r.CreateDate, r.Moderated,
                               ri.Website, rs.Title, rs.Description, rs.Keywords, l.LastCheckedAt, l.Notes
                        FROM Restaurants r
                        JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                        JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                        LEFT JOIN RestaurantSyncLog l ON l.RestaurantId = r.Id
                        {joinCreatedBy}
                        {whereClause}
                        ORDER BY r.CreateDate DESC
                        LIMIT {pageSize} OFFSET {pageSize * page}";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var title = reader.IsDBNull(8) ? null : reader.GetString(8);
                            var description = reader.IsDBNull(9) ? null : reader.GetString(9);
                            var keywords = reader.IsDBNull(10) ? null : reader.GetString(10);

                            result.Add(new RestaurantAdminRow
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Phone = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Address = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Status = (RestaurantStatus)reader.GetInt32(4),
                                CreateDate = reader.GetDateTime(5),
                                Moderated = reader.GetBoolean(6),
                                Website = reader.IsDBNull(7) ? null : reader.GetString(7),
                                SeoFilled = IsLocalizableFilled(title) && IsLocalizableFilled(description) && !string.IsNullOrWhiteSpace(keywords),
                                LastChecked = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11),
                                SyncNote = reader.IsDBNull(12) ? null : reader.GetString(12),
                            });
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
                Console.WriteLine($"GetFilteredRestaurants FAILED: {ex}");
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

        // «Видалити» в адмінці лише закриває ресторан (Status = Closed): дані й адреса лишаються в базі, повернути -
        // зміною статусу. Справжнє видалення прибрало 35 ресторанів разом із меню, а їхні сторінки в Google стали 404
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            await Close(id);
            return Ok();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> DeleteSeveral(IEnumerable<string> ids)
        {
            foreach (var id in ids)
            {
                await Close(id);
            }
            return Ok();
        }

        private async Task Close(string id)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            if (restaurant == null) return;
            restaurant.Status = RestaurantStatus.Closed;
            await _core.Restaurants.Update(restaurant);
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

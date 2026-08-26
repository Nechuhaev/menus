// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using Menus.Abstract;
using Menus.Abstract.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Domain.Restaurants
{
    public class CoreRestaurants
    {
        private IDbContext _db;
        private Core _core;
        public CoreRestaurants(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(IRestaurant restaurant, IRestaurantRole role)
        {
            //restaurant.UpdateDate = DateTime.Now;
            await _db.Restaurants.Add(restaurant);
            await _db.RestaurantRoles.Add(role);
        }

        public async Task Update(IRestaurant restaurant)
        {
            //restaurant.UpdateDate = DateTime.Now;
            await _db.Restaurants.Update(restaurant);
        }

        public async Task Delete(string id)
        {
            var restaurant = await GetRestaurantById(id);

            await _db.Restaurants.Remove(restaurant);
        }

        public async Task<IEnumerable<IRestaurant>> Search(string query, RestaurantStatus? status, RestaurantCreatedBy? createdBy, int pageSize = 20, int page = 0)
        {
            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(query))
            {
                parameters.Add("@query", $"%{query}%");
            }
            if (status != null)
            {
                parameters.Add("@status", (int)status.Value);
            }

            string queryText = $@"WHERE true 
                    {(!string.IsNullOrEmpty(query) ? "AND (" : "")}
                    {(!string.IsNullOrEmpty(query) ? "r.Name LIKE @query OR " : "")}
                    {(!string.IsNullOrEmpty(query) ? "r.Phone LIKE @query OR " : "")}
                    {(!string.IsNullOrEmpty(query) ? "r.Email LIKE @query" : "")}
                    {(!string.IsNullOrEmpty(query) ? ")" : "")}
                    {(status != null ? "AND r.Status = @status" : "")}
            ";

            if (createdBy == null)
            {
                string request = $@"SELECT r.*
                    FROM Restaurants r
                    {queryText}
                    ORDER BY r.CreateDate DESC LIMIT {pageSize} OFFSET {pageSize * page}";
                return (await _db.Restaurants.FromSql(request, parameters, false));
            } else if (createdBy == RestaurantCreatedBy.Admin)
            {
                string request = $@"SELECT r.*
                    FROM Restaurants r
                    LEFT JOIN(SELECT COUNT(*) AS Count, RestaurantId AS RestaurantId FROM MenuCategories mc GROUP BY RestaurantId) cc ON(r.Id = RestaurantId)
                    {queryText} AND cc.Count > 0
                    ORDER BY r.Name ASC LIMIT {pageSize} OFFSET {pageSize * page}";
                return (await _db.Restaurants.FromSql(request, parameters, false));
            }
            else
            {
                string request = $@"SELECT r.*
                    FROM Restaurants r
                    LEFT JOIN(SELECT COUNT(*) AS Count, RestaurantId AS RestaurantId FROM MenuCategories mc GROUP BY RestaurantId) cc ON(r.Id = RestaurantId)
                    {queryText} AND ISNULL(cc.Count)
                    ORDER BY r.CreateDate DESC LIMIT {pageSize} OFFSET {pageSize * page}";
                return (await _db.Restaurants.FromSql(request, parameters, false));
            }
        }

        public async Task<IEnumerable<ICompleteRestaurant>> FindNearest(string lat, string lon)
        {
            var parameters = new Dictionary<string, object> {
                { "@lat", Convert.ToDecimal(lat, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
                { "@lon", Convert.ToDecimal(lon, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
            };
            
            var nearestRestaurants = await _db.CompleteRestaurants.FromSql("CALL GetNearestRestaurants(@lat, @lon, 5, 120)", parameters);
            return nearestRestaurants;
        }

        public IQueryable<ICompleteRestaurant> GetCompleteRestaurants()
        {
            return _db.CompleteRestaurants.From("SELECT * FROM menus.RestaurantsWithInfoAndPositions", new Dictionary<string, object>());
        }

        public async Task<string> GetRestaurantOwnerId(string id)
        {
            var restaurantRole = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.RestaurantId == id && x.Role == RestaurantUserRole.Owner));

            return restaurantRole?.UserId;
        }

        public async Task<IEnumerable<IRestaurant>> GetRestaurantsForUser(string userId)
        {
            var restaurantRoles = await Task.Run(() => _db.RestaurantRoles.Where(x => x.UserId == userId).Select(x => x.RestaurantId).ToList());
            var restaurants = await Task.Run(() => _db.Restaurants.Where(x => restaurantRoles.Contains(x.Id)));

            return restaurants;
        }

        public async Task<IRestaurantRole> GetCurrentOwnerForRestaurant(string id)
        {
            var owner = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.RestaurantId == id && x.Role == RestaurantUserRole.Owner));

            return owner;
        }

        public async Task<IRestaurantRole> GetUserRoleForRestaurant(string id, string userId)
        {
            var owner = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.RestaurantId == id && x.UserId == userId));

            return owner;
        }

        public async Task CreateRestaurantRole(IRestaurantRole role)
        {
            await _db.RestaurantRoles.Add(role);
        }

        public async Task UpdateRestaurantRole(IRestaurantRole role)
        {
            await _db.RestaurantRoles.Update(role);
        }

        public async Task<IEnumerable<IRestaurant>> GetActiveRestaurants()
        {
            var restaurants = await Task.Run(() => _db.Restaurants.Where(x => x.Status == RestaurantStatus.Active));

            return restaurants;
        }

        public async Task<IEnumerable<IRestaurant>> GetActiveRestaurantsWithUrls(string city = "")
        {
            var restaurants = await Task.Run(() => _db.Restaurants.Where(x => x.Status == RestaurantStatus.Active));
            if (!string.IsNullOrEmpty(city)) restaurants = restaurants.Where(x => x.City == city);
            var result = new List<IRestaurant>();
            foreach (var restaurant in restaurants)
            {
                var seo = await Task.Run(() => _db.RestaurantSeo.FirstOrDefault(x => x.ItemId == restaurant.Id));
                var resultRestaurant = restaurant.Clone() as IRestaurant;
                if (seo != null)
                {
                    resultRestaurant.Id = seo.Url;
                }

                result.Add(resultRestaurant);
            }

            return result;
        }

        public async Task<IRestaurant> GetSelectedRestaurantForUser(string id, string userId)
        {
            IRestaurantRole restaurantRole;

            if (string.IsNullOrEmpty(id)) restaurantRole = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.UserId == userId));
            else restaurantRole = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.RestaurantId == id && x.UserId == userId));

            if (restaurantRole == null) return null;
            return await Task.Run(() => _db.Restaurants.First(x => x.Id == restaurantRole.RestaurantId));
        }

        public async Task<IEnumerable<IMenuCategory>> GetMenuCategoriesForRestaurant(string id)
        {
            return await Task.Run(() => _db.MenuCategories.Where(x => x.RestaurantId == id && !x.IsTopLevelCategory));
        }

        public async Task<IEnumerable<IMenuCategory>> GetAllMenuCategoriesForRestaurant(string id)
        {
            return await Task.Run(() => _db.MenuCategories.Where(x => x.RestaurantId == id));
        }

        public async Task<IEnumerable<IRestaurant>> GetRestaurantCreatedByAdmin()
        {
            string query = @"SELECT r.*
                            FROM Restaurants r
                            LEFT JOIN
                                (
	                                SELECT COUNT(*) AS Count, RestaurantId AS RestaurantId FROM 
                                    MenuCategories mc GROUP BY RestaurantId
                                ) cc 
                            ON (r.Id = cc.RestaurantId)
                            WHERE cc.Count > 0";


            var result = (await _db.Restaurants.FromSql(query, new Dictionary<string, object>(), false));

            return result;
        }

        public async Task<IEnumerable<IDishCategory>> GetDishCategoriesForMenuCategories(IEnumerable<string> menuCategoryIds)
        {
            return await Task.Run(() => _db.DishCategories.Where(x => menuCategoryIds.Contains(x.MenuCategoryId)));
        }

        public async Task CreateMenuCategory(IMenuCategory category)
        {
            await _db.MenuCategories.Add(category);
        }

        public async Task CreateMenuCategories(IEnumerable<IMenuCategory> categories)
        {
            await _db.MenuCategories.Add(categories);
        }

        public async Task UpdateMenuCategory(IMenuCategory category)
        {
            await _db.MenuCategories.Update(category);
        }

        public async Task UpdateMenuCategories(IEnumerable<IMenuCategory> category)
        {
            await _db.MenuCategories.Update(category);
        }

        public async Task DeleteMenuCategory(string id)
        {
            var category = await GetMenuCategoryById(id);
            await _db.MenuCategories.Remove(category);
        }

        public async Task<IMenuCategory> GetMenuCategoryById(string id)
        {
            return await Task.Run(() => _db.MenuCategories.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IRestaurant> GetRestaurantById(string id)
        {
            return await Task.Run(() => _db.Restaurants.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IEnumerable<IRestaurant>> GetRestaurantsByIds(IEnumerable<string> ids)
        {
            return await Task.Run(() => _db.Restaurants.Where(x => ids.Contains(x.Id)));
        }

        public async Task<IRestaurant> GetRestaurantByIdOrSeo(string id)
        {
            var id1 = id; // Solves "Access to modified closure"
            var seo = await Task.Run(() => _db.RestaurantSeo.FirstOrDefault(x => x.Url == id1));
            if (seo != null) id = seo.ItemId;
            return await Task.Run(() => _db.Restaurants.FirstOrDefault(x => x.Id == id));
        }

        public async Task<bool> IsUserAllowedToEdit(string id, string userId)
        {
            var role = await Task.Run(() => _db.RestaurantRoles.FirstOrDefault(x => x.RestaurantId == id && x.UserId == userId));
            return role != null;
        }

        public async Task<IRestaurantInfo> GetRestaurantInfo(string restaurantId)
        {
            return await Task.Run(() => _db.RestaurantInfo.FirstOrDefault(x => x.RestaurantId == restaurantId));
        }

        public async Task UpdateRestaurantInfo(IRestaurantInfo restaurantInfo)
        {
            var existingInfo = _db.RestaurantInfo.FirstOrDefault(x => x.Id == restaurantInfo.Id);
            if(existingInfo == null) await _db.RestaurantInfo.Add(restaurantInfo);
            else await _db.RestaurantInfo.Update(restaurantInfo);
        }

        public async Task CreateSeo(IRestaurantSeo seo)
        {
            //while (true)
            //{
            //    var existingSeoUrl = _db.RestaurantSeo.FirstOrDefault(x => x.Url == seo.Url);
            //    if (existingSeoUrl == null) break;

            //    seo.Url += "1";
            //}
            await _db.RestaurantSeo.Add(seo);
        }

        public async Task UpdateSeo(IRestaurantSeo seo)
        {
            //while (true)
            //{
            //    var existingSeoUrl = _db.RestaurantSeo.FirstOrDefault(x => x.Url == seo.Url);
            //    if (existingSeoUrl == null) break;

            //    if (existingSeoUrl.Id != seo.Id) seo.Url += "1";
            //}
            var existingSeoUrl = _db.RestaurantSeo.FirstOrDefault(x => x.Url == seo.Url && x.Id != seo.Id);
            if (existingSeoUrl != null) throw new InvalidOperationException("Same url already exist");
            await _db.RestaurantSeo.Update(seo);
        }

        public async Task<IRestaurantSeo> GetSeoByRestaurantId(string id)
        {
            return await Task.Run(() => _db.RestaurantSeo.FirstOrDefault(x => x.ItemId == id));
        }

        public async Task<IEnumerable<ICity>> GetCities()
        {
            return await Task.Run(() => _db.Cities);
        }

        public async Task<ICity> GetCityById(string id)
        {
            return await Task.Run(() => _db.Cities.FirstOrDefault(x => x.Id == id));
        }

        public async Task CreateMenuPhoto(IMenuPhoto menuPhoto)
        {
            await _db.MenuPhotos.Add(menuPhoto);
        }

        public async Task UpdateMenuPhoto(IMenuPhoto menuPhoto)
        {
            await _db.MenuPhotos.Update(menuPhoto);
        }

        public async Task<IEnumerable<IMenuPhoto>> GetMenuPhotosForRestaurant(string restaurantId)
        {
            return await Task.Run(() => _db.MenuPhotos.Where(x => x.RestaurantId == restaurantId).OrderBy(x => x.Index).ThenByDescending(x => x.Timestamp));
        }

        public async Task<IMenuPhoto> GetMenuPhotoById(string id)
        {
            return await Task.Run(() => _db.MenuPhotos.FirstOrDefault(x => x.Id == id));
        }

        public async Task DeleteMenuPhoto(string id)
        {
            var item = _db.MenuPhotos.FirstOrDefault(x => x.Id == id);
            await _db.MenuPhotos.Remove(item);
        }

        public async Task<IEnumerable<IRestaurantSeo>> FindRestaurantsAutocomplete(string search, string city)
        {
            if (string.IsNullOrWhiteSpace(search)) return new List<IRestaurantSeo>();

            search = search.Replace("\"", "");
            search = search.Replace("'", "");

            var parameters = new Dictionary<string, object> {
                { "@search", $"%{search}%" },
                { "@city", city }
            };

            var resultRestaurants = await _db.RestaurantSeo.FromSql($"SELECT s.* FROM RestaurantSeo s INNER JOIN Restaurants r ON s.ItemId = r.Id WHERE s.SearchTerms LIKE @search AND r.City = @city LIMIT 10", parameters, false);

            return resultRestaurants;
        }

        public IQueryable<ICompleteRestaurant> FindSimilarCuisineRestaurants(string cuisine, string city, string id)
        {
            var parameters = new Dictionary<string, object> {
                { "@cuisine", $"%{cuisine}%" },
                { "@id", id },
                { "@city", city }
            };

            var parametersWithoutCuisine = new Dictionary<string, object> {
                { "@id", id },
                { "@city", city }
            };

            IQueryable <ICompleteRestaurant> result = _db.CompleteRestaurants.From($"SELECT * FROM menus.RestaurantsWithInfoAndPositions WHERE Cuisine LIKE @cuisine AND City = @city AND Id <> @id", parameters);
            if(result.Count() == 0)
            {
                result = _db.CompleteRestaurants.From($"SELECT * FROM menus.RestaurantsWithInfoAndPositions WHERE City = @city AND Id <> @id", parametersWithoutCuisine);
            }

            return result;
        }
    }
}

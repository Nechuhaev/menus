// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Abstract.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Menus.Domain.Dishes
{
    public class CoreDishes
    {
        private IDbContext _db;
        private Core _core;
        public CoreDishes(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(IDish dish)
        {
            await _db.Dishes.Add(dish);
        }

        public async Task Create(IEnumerable<IDish> dishes)
        {
            await _db.Dishes.Add(dishes);
        }

        public async Task Update(IDish dish)
        {
            await _db.Dishes.Update(dish);
        }

        public async Task Update(IEnumerable<IDish> dishes)
        {
            await _db.Dishes.Update(dishes);
        }

        public async Task UpdateDishPhoto(IDishPhoto dishPhoto)
        {
            await _db.DishPhotos.Update(dishPhoto);

            var dish = await _core.Dishes.GetDishById(dishPhoto.DishId);
            var dishPhotos = (await _core.Dishes.GetApprovedDishPhotosForDish(dish.Id)).OrderBy(x => x.Index).ThenByDescending(x => x.Timestamp).Select(x => x.ImageUrl);
            dish.Photos = dishPhotos;

            await _core.Dishes.Update(dish);
        }

        public async Task Delete(string id)
        {
            var dish = await GetDishById(id);

            await _db.Dishes.Remove(dish);
        }

        public async Task Delete(List<string> ids, string restaurantId)
        {
            var dishes = _db.Dishes.Where(x => ids.Contains(x.Id) && x.RestaurantId == restaurantId);

            await _db.Dishes.Remove(dishes);
        }

        public async Task<IEnumerable<IDish>> GetAllDishes()
        {
            return await Task.Run(() => _db.Dishes);
        }

        public async Task<int> GetAllDishesCount()
        {
            return await Task.Run(() => _db.Dishes.Count());
        }

        public async Task<IEnumerable<ISearchDish>> FindDishesByName(string search, string city, int take = 999, int skip = 0, string orderBy = "RatingFloat")
        {
            var parameters = new Dictionary<string, object> {
                { "@search", $"%{search.ToLowerInvariant()}%" },
                { "@city", city },
                { "@orderBy", orderBy },
                { "@skip", skip },
                { "@skipTake", skip + take },
            };
            return await _db.SearchDishes.FromSql("SELECT * FROM SearchDishes WHERE Name LIKE @search AND RestaurantCity = @city ORDER BY @orderBy DESC LIMIT @skip, @skipTake", parameters);
        }

        public async Task<IEnumerable<ISearchDish>> GetBestDishes(string city, int take = 999, int skip = 0)
        {
            var parameters = new Dictionary<string, object> {
                { "@city", city },
                { "@orderBy", "ShowOnIndex" },
                { "@orderBy2", "RatingFloat" },
                { "@skip", skip },
                { "@skipTake", skip + take },
            };
            
            string query = @"
                        SELECT * FROM SearchDishes  
                        WHERE ShowOnIndex = (1) or (RatingFloat >= 4 and RatingsCount > 2 and LENGTH(PhotoLink) > 10)
                            AND RestaurantCity = @city 
                        ORDER BY @orderBy DESC, @orderBy2 DESC LIMIT @skip, @skipTake";
            return await _db.SearchDishes.FromSql(query, parameters);
            //return await _db.SearchDishes.FromSql($"SELECT * FROM SearchDishes sd WHERE sd.ShowOnIndex = (1) AND sd.RestaurantCity = '{city}' ORDER BY sd.{orderBy} DESC LIMIT {skip}, {skip + take}");
        }

        public async Task<IEnumerable<IDishPhotoExtended>> GetDishesPhotos(int? status = null, DateTime? from = null, DateTime? to = null, int take = 999, int skip = 0, string orderBy = "Date")
        {
            var parameters = new Dictionary<string, object> {
                { "@status", status },
                { "@orderBy", orderBy },
                { "@from", from },
                { "@to", to },
                { "@skip", skip },
                { "@skipTake", skip + take },
            };
            
            string query = "SELECT * FROM DishPhotoExtended ";
            if (status != null)
            {
                query += "WHERE Status = @status";
                if (from != null && to != null)
                {
                    query += " AND Date between @from and @to";
                }
            } else
            {
                if (from != null && to != null)
                {
                    query += "WHERE Date between @from and @to";
                }
            }
            query += " ORDER BY @orderBy DESC LIMIT @skip, @skipTake";
            return await _db.DishPhotoExtendeds.FromSql(query, parameters);
        }

        public async Task<IDishPhoto> GetDishPhotoEntity(string id)
        {
            return await Task.Run(() => _db.DishPhotos.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IEnumerable<IDishPhoto>> GetDishPhotosForDish(string id)
        {
            return await Task.Run(() => _db.DishPhotos.Where(x => x.DishId == id));
        }

        public async Task<IEnumerable<IDishPhoto>> GetApprovedDishPhotosForDish(string id)
        {
            return await Task.Run(() => _db.DishPhotos.Where(x => x.DishId == id && x.Status == DishPhotoStatus.Approved));
        }

        //public async Task<IEnumerable<string>> FindDishAutocomplete(string search)
        //{
        //    var resultDishes = await _db.Dishes.FromSql($"SELECT * FROM Dishes d WHERE d.Name LIKE '%{search.ToLowerInvariant()}%' LIMIT 10", false);
        //    var resultGrouped = resultDishes.GroupBy(x => x.Name.Find(search));
        //    var result = resultGrouped.Select(x => x.Key);

        //    return result;
        //}

        public async Task<IEnumerable<IAutocompleteDish>> FindDishAutocomplete(string search, string lat, string lon, string city)
        {
            var parameters = new Dictionary<string, object> {
                { "@search", search.ToLowerInvariant() },
                { "@lat", Convert.ToDecimal(lat, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
                { "@lon", Convert.ToDecimal(lon, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
                { "@city", city }
            };
            
            var dishes = await _db.AutocompleteDishes.FromSql("CALL GetSearchDishWithSeoAndRatings(@search, @lat, @lon, @city)", parameters);
            return dishes;
        }

        public async Task<IEnumerable<ICompleteDish>> GetCompleteDishes(string search, string lat, string lon, string city, CompleteDishesOrderBy orderBy, int skip, int take)
        {
            search = search.Replace("\"", "");
            search = search.Replace("'", "");

            var parameters = new Dictionary<string, object> {
                { "@search", search.ToLowerInvariant() },
                { "@lat", Convert.ToDecimal(lat, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
                { "@lon", Convert.ToDecimal(lon, new CultureInfo("en-US")).ToString("#.######", new CultureInfo("en-US")) },
                { "@city", city },
                { "@take", take },
                { "@skip", skip }
            };
            
            switch (orderBy)
            {
                case CompleteDishesOrderBy.Distance:
                    parameters.Add("@order", "distance asc");
                    break;
                case CompleteDishesOrderBy.Rating:
                    parameters.Add("@order", "rating desc");
                    break;
                case CompleteDishesOrderBy.RatingsCount:
                    parameters.Add("@order", "ratingsCount desc");
                    break;
            }

            var dishes = await _db.CompleteDishes.FromSql("CALL FindCompleteDishesWithCategories(@search, @lat, @lon, @city, @order, @take, @skip)", parameters);

            return dishes;
        }

        public async Task<IEnumerable<IDish>> GetDishesForRestaurant(string restaurantId)
        {
            return await Task.Run(() => _db.Dishes.Where(x => x.RestaurantId == restaurantId));
        }

        public async Task<IEnumerable<IDishWithSeoAndRatings>> GetDishesForRestaurantWithSeoAndRatings(string restaurantId, string userId)
        {
            var parameters = new Dictionary<string, object> {
                { "@restaurantId", restaurantId },
                { "@userId", userId }
            };

            return await Task.Run(() => _db.DishesWithSeoAndRatings.Get(parameters));
        }

        public async Task<IEnumerable<IDish>> GetDishesForRestaurantWithUrl(string restaurantId)
        {
            var dishes = await Task.Run(() => _db.Dishes.Where(x => x.RestaurantId == restaurantId));
            foreach (var dish in dishes)
            {
                var seo = await Task.Run(() => _db.DishSeo.FirstOrDefault(x => x.ItemId == dish.Id));
                if (seo != null) {
                    dish.Id = seo.Url;
                }
            }

            return dishes;
        }

        public async Task<IDishWithSeoAndRatings> GetBestDishForRestaurant(string restaurantId)
        {
            var rating = await Task.Run(() => _db.Ratings.Where(x => x.RestaurantId == restaurantId).OrderByDescending(x => x.Rate).FirstOrDefault());
            if (rating == null) return null;

            return await GetDishByIdOrSeoWithSeoAndRating(rating.DishId, restaurantId, null);
        }

        public async Task<IEnumerable<IDishCategory>> GetDishesForCategory(string categoryId)
        {
            return await Task.Run(() => _db.DishCategories.Where(x => x.MenuCategoryId == categoryId));
        }

        public async Task<IEnumerable<IDishCategory>> GetDishCategories(string dishId)
        {
            return await Task.Run(() => _db.DishCategories.Where(x => x.DishId == dishId));
        }

        public async Task AddDishToCategory(IDishCategory dishCategory)
        {
            dishCategory.Index = await Task.Run(() => _db.DishCategories.Count(x => x.MenuCategoryId == dishCategory.MenuCategoryId));
            await _db.DishCategories.Add(dishCategory);
        }

        public async Task CreateDishCategories(IEnumerable<IDishCategory> dishCategories)
        {
            await _db.DishCategories.Add(dishCategories);
        }

        public async Task UpdateMultipleDishCategories(IEnumerable<IDishCategory> dishCategories)
        {
            await _db.DishCategories.Update(dishCategories);
        }

        public async Task AddUserDishPhoto(IDishPhoto dishPhoto)
        {
            await _db.DishPhotos.Add(dishPhoto);

            var dish = await _core.Dishes.GetDishById(dishPhoto.DishId);
            var dishPhotos = (await _core.Dishes.GetApprovedDishPhotosForDish(dish.Id)).OrderBy(x => x.Index).ThenByDescending(x => x.Timestamp).Select(x => x.ImageUrl);
            dish.Photos = dishPhotos;

            await _core.Dishes.Update(dish);
        }

        public async Task<IEnumerable<IDishPhoto>> GetDishPhotos(string dishId)
        {
            return await Task.Run(() =>
            {
                return _db.DishPhotos.Where(x => x.DishId == dishId).ToList();
            });
        }

        public async Task<IDishPhoto> GetDishPhotoById(string dishPhotoId)
        {
            return await Task.Run(() =>
            {
                return _db.DishPhotos.FirstOrDefault(x => x.Id == dishPhotoId);
            });
        }

        public async Task<IDishPhoto> GetDishPhotoByImageUrl(string imageUrl)
        {
            return await Task.Run(() =>
            {
                return _db.DishPhotos.FirstOrDefault(x => x.ImageUrl == imageUrl);
            });
        }

        public async Task DeleteUserDishPhoto(IDishPhoto dishPhoto)
        {
            await _db.DishPhotos.Remove(dishPhoto);

            var dish = await _core.Dishes.GetDishById(dishPhoto.DishId);
            var dishPhotos = (await _core.Dishes.GetApprovedDishPhotosForDish(dish.Id)).OrderBy(x => x.Index).ThenByDescending(x => x.Timestamp).Select(x => x.ImageUrl);
            dish.Photos = dishPhotos;

            await _core.Dishes.Update(dish);
        }

        public async Task DeleteDishPhoto(string id)
        {
            var photo = _db.DishPhotos.First(x => x.Id == id);
            var file = await _core.Files.GetFileById(GetGuid(photo.ImageUrl));

            //await _db.Files.Remove(file);
            await _db.DishPhotos.Remove(photo);

            var dish = await _core.Dishes.GetDishById(photo.DishId);
            var dishPhotos = (await _core.Dishes.GetApprovedDishPhotosForDish(dish.Id)).OrderBy(x => x.Index).Select(x => x.ImageUrl);
            dish.Photos = dishPhotos;

            await _core.Dishes.Update(dish);
        }

        public async Task DeleteDishPhotosForDish(string dishId)
        {
            var photos = _db.DishPhotos.Where(x => x.DishId == dishId).ToList();
            var fileIds = photos.Select(y => GetGuid(y.ImageUrl)).ToList();
            var files = _db.Files.Where(x => fileIds.Contains(x.Id)).ToList();

            //await _db.Files.Remove(files);
            await _db.DishPhotos.Remove(photos);

            var dish = await _core.Dishes.GetDishById(dishId);
            dish.Photos = new List<string>();

            await _core.Dishes.Update(dish);
        }

        private static string GetGuid(string input)
        {
            if (input == null) return "";
            Regex regex = new Regex("[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}");
            Match match = regex.Match(input);
            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }

        public async Task RemoveDishFromCategories(IEnumerable<IDishCategory> dishCategories)
        {
            await _db.DishCategories.Remove(dishCategories);
        }

        public async Task RemoveDishFromCategory(IDishCategory dishCategory)
        {
            await _db.DishCategories.Remove(dishCategory);
        }

        public async Task<IDish> GetDishById(string id)
        {
            return await Task.Run(() => _db.Dishes.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IDish> GetDishByIdOrSeo(string id, string restaurantId)
        {
            var id1 = id; // Solves "Access to modified closure"
            var seo = await Task.Run(() => _db.DishSeo.FirstOrDefault(x => x.Url == id1 && x.RestaurantId == restaurantId));
            if (seo != null) id = seo.ItemId;
            return await Task.Run(() => _db.Dishes.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IDishWithSeoAndRatings> GetDishByIdOrSeoWithSeoAndRating(string id, string restaurantId, string userId)
        {
            var seo = await Task.Run(() => _db.DishSeo.FirstOrDefault(x => x.Url == id && x.RestaurantId == restaurantId));
            if (seo != null) id = seo.ItemId;

            var parameters = new Dictionary<string, object> {
                { "@dishId", id },
                { "@userId", userId }
            };

            return await Task.Run(() => _db.DishesWithSeoAndRatings.GetSingle(parameters));
        }

        public async Task<IEnumerable<IDishVariation>> GetDishVariations(string dishId)
        {
            return await Task.Run(() => _db.DishVariations.Where(x => x.DishId == dishId));
        }

        public async Task CreateDishVariations(IEnumerable<IDishVariation> variations)
        {
            await _db.DishVariations.Add(variations);
        }

        public async Task DeleteDishVariations(IEnumerable<IDishVariation> variations)
        {
            await _db.DishVariations.Remove(variations);
        }

        public async Task CreateSeo(IDishSeo seo)
        {
            //while (true)
            //{
            //    var existingSeoUrl = _db.DishSeo.FirstOrDefault(x => x.Url == seo.Url && x.RestaurantId == seo.RestaurantId);
            //    if (existingSeoUrl == null) break;

            //    if (existingSeoUrl.Id != seo.Id) seo.Url += "1";
            //}

            await _db.DishSeo.Add(seo);
        }

        public async Task CreateSeo(IEnumerable<IDishSeo> seo)
        {
            await _db.DishSeo.Add(seo);
        }

        public async Task UpdateSeo(IDishSeo seo, string possibleAddOnString = "")
        {
            //var addOnSubstrings =
            //    !string.IsNullOrEmpty(possibleAddOnString) ? possibleAddOnString.Split("-") : new string [0];
            //var currentAddOnSubstringIndex = 0;

            //while (true)
            //{
            //    var existingSeoUrl = _db.DishSeo.FirstOrDefault(x => x.Url == seo.Url && x.RestaurantId == seo.RestaurantId);
            //    if (existingSeoUrl == null) break;

            //    if (addOnSubstrings.Length > 0 && currentAddOnSubstringIndex < addOnSubstrings.Length)
            //    {
            //        seo.Url += $"-{addOnSubstrings[currentAddOnSubstringIndex]}";
            //        currentAddOnSubstringIndex++;
            //    }
            //    else seo.Url += "1";
            //}
            var existingSeoUrl = _db.DishSeo.FirstOrDefault(x => x.Url == seo.Url && x.RestaurantId == seo.RestaurantId && x.Id != seo.Id);
            if (existingSeoUrl != null) throw new InvalidOperationException("Url already exist");
            await _db.DishSeo.Update(seo);
        }

        public async Task UpdateSeoWithoutChecks(IDishSeo seo)
        {
            await _db.DishSeo.Update(seo);
        }

        public async Task<IEnumerable<IDishSeo>> GetSeoForRestaurant(string restaurantId)
        {
            return await Task.Run(() => _db.DishSeo.Where(x => x.RestaurantId == restaurantId));
        }

        public async Task<IDishSeo> GetSeoByDishId(string id)
        {
            return await Task.Run(() => _db.DishSeo.FirstOrDefault(x => x.ItemId == id));
        }

        public async Task<IEnumerable<IDishRatingWithUser>> GetRatingsWithUsersForDish(string id)
        {
            var parameters = new Dictionary<string, object> {
                { "@dishId", id }
            };

            return await Task.Run(() => _db.DishRatingWithUsers.Get(parameters));
        }
    }

    public enum CompleteDishesOrderBy
    {
        Distance,
        Rating,
        RatingsCount
    }
}

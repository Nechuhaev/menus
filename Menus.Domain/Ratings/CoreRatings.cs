// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Abstract.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Menus.Domain.Ratings
{
    public class CoreRatings
    {
        private IDbContext _db;
        private Core _core;
        public CoreRatings(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(IRating rating)
        {
            await _db.Ratings.Add(rating);
        }

        public async Task Update(IRating rating)
        {
            await _db.Ratings.Update(rating);
        }

        public async Task Delete(string id)
        {
            var rating = await GetRatingById(id);

            await _db.Ratings.Remove(rating);
        }

        public async Task<IRating> GetRatingById(string id)
        {
            return await Task.Run(() => _db.Ratings.FirstOrDefault(x => x.Id == id));
        }

        public async Task<IEnumerable<IRating>> GetAllRatings(RatingStatus? status)
        {
            IEnumerable<IRating> ratings = _db.Ratings;
            if (status != null)
            {
                ratings = ratings.Where(x => x.Status == status);
            }
            return await Task.Run(() => ratings.ToList());
        }

        public async Task<IRating> GetDishRatingForUser(string dishId, string userId)
        {
            return await Task.Run(() => _db.Ratings.FirstOrDefault(x => x.DishId == dishId && x.UserId == userId));
        }

        public async Task<List<IRating>> GetAllUserRatings(string userId)
        {
            return await Task.Run(() => _db.Ratings.Where(x => x.UserId == userId).ToList());
        }

        public async Task<IDishRating> GetDishRatingData(string restaurantId, string dishId, string userId, IDishRating result)
        {
            var dish = await _core.Dishes.GetDishByIdOrSeo(dishId, restaurantId);
            var userRating = await GetDishRatingForUser(dish.Id, string.IsNullOrEmpty(userId) ? "nouser" : userId);
            var generalRating = await GetRatingForDish(dish.Id);
            var generalRatingCount = await GetRatingCountForDish(dish.Id);

            result.GeneralRating = generalRating;
            result.GeneralRatingCount = generalRatingCount;
            result.UserRating = userRating == null ? 0 : userRating.Rate;

            return result;
        }

        public async Task<int> GetRatingForDish(string dishId)
        {
            return await Task.Run(() => _db.Ratings.Count(x => x.DishId == dishId) > 0 ? Convert.ToInt32(Math.Ceiling(_db.Ratings.Where(x => x.DishId == dishId).ToList().Average(x => x.Rate))) : 0);
        }

        public async Task<int> GetRatingCountForDish(string dishId)
        {
            return await Task.Run(() => _db.Ratings.Count(x => x.DishId == dishId));
        }

        public async Task<int> GetRatingCountForUser(string userId)
        {
            return await Task.Run(() => _db.Ratings.Count(x => x.UserId == userId));
        }

        public async Task<int> GetRatingCountForRestaurant(string restaurantId)
        {
            return await Task.Run(() => _db.Ratings.Count(x => x.RestaurantId == restaurantId));
        }

        public async Task<IEnumerable<IUserRating>> GetUserRatings(string id, RatingStatus? status = null)
        {
            string queriedStatus = status != null ? ((int)status).ToString() : null;
            var parameters = new Dictionary<string, object> {
                { "@userId", id },
                { "@status", queriedStatus }
            };

            return await Task.Run(() => _db.UserRatings.Get(parameters));
        }

        public async Task<IEnumerable<IUserRating>> GetAnonymousUserRatings(string id, RatingStatus? status = null)
        {
            string queriedStatus = status != null ? ((int)status).ToString() : null;
            var parameters = new Dictionary<string, object> {
                { "@userId", id },
                { "@status", queriedStatus }
            };

            return await Task.Run(() => _db.UserRatings.Get(parameters));
        }

        public async Task AddAnonymousRating(IAnonymousRating rating)
        {
            await _db.AnonymousRatings.Add(rating);
        }


        public async Task UpdateAnonymousRating(IAnonymousRating rating)
        {
            try
            {
                await _db.AnonymousRatings.Update(rating);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
            
        }

        public async Task<int> GetNumberOfAnonymousRatingByDishAndDevice(string dishId, string deviceId)
        {
            return await Task.Run(() => _db.AnonymousRatings.Count(x => x.DishId == dishId && x.DeviceId == deviceId));
        }

        public async Task<IAnonymousRating> GetAnonymousRatingByDishAndDevice(string dishId, string deviceId)
        {
            return await Task.Run(() => _db.AnonymousRatings.FirstOrDefault(x => x.DishId == dishId && x.DeviceId == deviceId));
        }

        public async Task<IAnonymousRatingResults> GetAnonymousRatingValidationResult(string deviceId, string ipAddress, string dishId, string restaurantId)
        {
            try
            {
                //var ratingResults = await _db.AnonymousRatingResults.FromSql($"CALL GetAnonymousRatingValidation('{deviceId}', '{ipAddress}', '{dishId}', '{restaurantId}')");
                var parameters = new Dictionary<string, object> {
                    { "@deviceId", deviceId },
                    { "@ipAddress", ipAddress },
                    { "@dishId", dishId },
                    { "@restaurantId", restaurantId }
                };

                var ratingResults = await Task.Run(() => _db.AnonymousRatingResults.GetSingle(parameters));
                return ratingResults;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }

            return null;
        }

        public async Task<IEnumerable<IAnonymousRating>> GetLatestAnonymousRatings()
        {
            var latestAnonymousRatings = await Task.Run(() => _db.AnonymousRatings.OrderByDescending(x => x.UpdateDate).Take(50).ToList());
            return latestAnonymousRatings;
        }

        public async Task<IEnumerable<IDish>> GetDishesByRatingsRatings(IEnumerable<IAnonymousRating> ratings)
        {
            var dishIds = ratings.Select(x => x.DishId);
            var dishes = await Task.Run(() => _db.Dishes.Where(x => dishIds.Contains(x.Id)).ToList());
            return dishes;
        }

        public async Task<IEnumerable<IRestaurant>> GetRestaurantsByDishes(IEnumerable<IDish> dishes)
        {
            var restaurantIds = dishes.Select(x => x.RestaurantId).Distinct();
            var restaurants = await Task.Run(() => _db.Restaurants.Where(x => restaurantIds.Contains(x.Id)).ToList());
            return restaurants;
        }
    }
}

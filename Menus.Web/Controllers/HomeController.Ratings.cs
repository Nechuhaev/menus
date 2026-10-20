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
        [Authorize]
        public async Task<IActionResult> RateDish(string restaurantId, string dishId, int rating, string text)
        {
            if (rating == 0) return Ok();
            var dish = await _core.Dishes.GetDishByIdOrSeo(dishId, restaurantId);
            var rate = await _core.Ratings.GetDishRatingForUser(dish.Id, User.Identity.Name);
            var createNew = false;
            if (rate == null) {
                createNew = true;
                rate = new Rating
                {
                    DishId = dish.Id,
                    RestaurantId = restaurantId,
                    Id = Guid.NewGuid().ToString(),
                    UserId = User.Identity.Name,
                    Text = text
                };
            }

            rate.Rate = rating;
            rate.Text = string.IsNullOrEmpty(text) ? rate.Text : text;
            rate.Status = RatingStatus.NotApproved;
            rate.UpdateDate = DateTime.Now;

            if (createNew) await _core.Ratings.Create(rate);
            else await _core.Ratings.Update(rate);

            var dishWithSeo = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, dish.RestaurantId, User.Identity.Name);

            return Ok(new { userRating = dishWithSeo.UserRating, ratingNum = dishWithSeo.Rating, ratingFloat = dishWithSeo.RatingFloat, rating = dishWithSeo.RatingFloat.ToString("F1"), ratingCount = dishWithSeo.RatingCount });
        }

        [AllowAnonymous]
        public async Task<IActionResult> RateDishAnonymous(string restaurantId, string dishId, int rating, string text)
        {
            var ipAddress = HomeController.GetIpAddress(Request);
            var uniqueCookie = _accessor.HttpContext.Request.Cookies[".Menus.DeviceId"];

            var validationResult = await _core.Ratings.GetAnonymousRatingValidationResult(uniqueCookie, ipAddress, dishId, restaurantId);

            bool canVoteAnonymously = CanVoteAnonymously(validationResult);

            if (rating == 0) return Ok();

            var dish = await _core.Dishes.GetDishByIdOrSeo(dishId, restaurantId);



            var prevRate = await GetAnonymousRatingByDishId(dishId);
            if (prevRate == null && canVoteAnonymously)
            {
                var rate = new AnonymousRating
                {
                    DishId = dish.Id,
                    Id = Guid.NewGuid().ToString(),
                    Rate = rating,
                    UpdateDate = DateTime.Now,
                    DeviceId = uniqueCookie,
                    UserIp = ipAddress,
                    RestaurantId = restaurantId
                };
                await _core.Ratings.AddAnonymousRating(rate);
            }
            else if (prevRate != null)
            {
                prevRate.Rate = rating;
                prevRate.UpdateDate = DateTime.Now;
                await _core.Ratings.UpdateAnonymousRating(prevRate);
            }
            

            var dishWithSeo = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, restaurantId, User.Identity.Name);

            return Ok(new { userRating = dishWithSeo.UserRating, ratingNum = dishWithSeo.Rating, ratingFloat = dishWithSeo.RatingFloat, rating = dishWithSeo.RatingFloat.ToString("F1"), ratingCount = dishWithSeo.RatingCount });
        }

        private async Task<IAnonymousRating> GetAnonymousRatingByDishId(string dishId)
        {
            var uniqueCookie = _accessor.HttpContext.Request.Cookies[".Menus.DeviceId"];
            var anonymousRating = await _core.Ratings.GetAnonymousRatingByDishAndDevice(dishId, uniqueCookie);

            return anonymousRating;
        }

        private bool CanVoteAnonymously(IAnonymousRatingResults ratingResults)
        {
            var rateDishEnable = Convert.ToBoolean(_config["RateDishAnonymous:enable"]);
            var maxVotesForDishFromOneIp = Convert.ToInt32(_config["RateDishAnonymous:maxVotesForDishFromOneIp"]);
            var maxVotesForRestaurantFromOneIp = Convert.ToInt32(_config["RateDishAnonymous:maxVotesForRestaurantFromOneIp"]);
            var maxVotesForRestaurantFromDevice = Convert.ToInt32(_config["RateDishAnonymous:maxVotesForRestaurantFromDevice"]);

            int votesForDishFromOneIp = ratingResults.VotesForDishFromOneIp;
            int votesForRestaurantFromOneIp = ratingResults.VotesForRestaurantFromOneIp;
            int votesForRestaurantFromOneDevice = ratingResults.VotesForRestaurantFromOneDevice;



            if (!rateDishEnable)
            {
                return false;
            }

            if (votesForDishFromOneIp > maxVotesForDishFromOneIp)
            {
                return false;
            }

            if (votesForRestaurantFromOneIp > maxVotesForRestaurantFromOneIp)
            {
                return false;
            }

            if (votesForRestaurantFromOneDevice > maxVotesForRestaurantFromDevice)
            {
                return false;
            }

            return true;
        }

        public async Task<IActionResult> MobileRate(string dishId, string restaurantId, string returnUrl)
        {
            ViewData["Robots"] = "NOINDEX,NOFOLLOW";

            var restaurant = await _core.Restaurants.GetRestaurantById(restaurantId);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurantId);
            var restaurantSeo = await _core.Restaurants.GetSeoByRestaurantId(restaurantId);
            var dish = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, restaurantId, User.Identity.Name);

            if (restaurant == null || restaurantInfo == null || restaurantSeo == null || dish == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var review = await _core.Ratings.GetDishRatingForUser(dishId, User.Identity.Name);
            ViewData["restaurant"] = restaurant;
            ViewData["dish"] = dish;
            ViewData["review"] = review;
            ViewData["restaurantSeo"] = restaurantSeo;
            ViewData["restaurantInfo"] = restaurantInfo;
            ViewData["returnUrl"] = returnUrl;
            return View();
        }

        public IActionResult RatingRecommendations()
        {
            return View();
        }
    }
}

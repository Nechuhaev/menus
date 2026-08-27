// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "admin, manager")]
    public class RatingsController : Controller
    {
        private readonly Core _core;

        public RatingsController(Core core)
        {
            _core = core;
        }

        // GET: Ratings
        public async Task<IActionResult> Index(int? status = null, int page = 0)
        {
            ViewData["status"] = status;
            ViewData["page"] = page;
            RatingStatus? selectedStatus = null;
            if (status != null)
            {
                selectedStatus = (RatingStatus)status;
            }

            var anonymousRatings = await GetAnonymousRatings();
            ViewData["anonymousRatings"] = anonymousRatings;
            var Ratings = (await _core.Ratings.GetUserRatings(null, selectedStatus ?? null)).Skip(page * 10).Take(10).ToList();
            return View(Ratings);
        }

        private async Task<List<UserRating>> GetAnonymousRatings() {
            var anonymousRatings = (await _core.Ratings.GetLatestAnonymousRatings()).ToList();
            var dishes = (await _core.Ratings.GetDishesByRatingsRatings(anonymousRatings)).ToList();
            var restaurants = (await _core.Ratings.GetRestaurantsByDishes(dishes)).ToList();
            List<UserRating> ratings = new List<UserRating>();

                        foreach (IAnonymousRating anonymousRating in anonymousRatings)
            {
                UserRating ur = new UserRating();
                var dish = dishes.FirstOrDefault(x => x.Id == anonymousRating.DishId);
                if (dish == null)
                {
                    // Orphaned rating: the dish it references no longer exists. Skip it instead of crashing the page.
                    continue;
                }
                var restaurant = restaurants.FirstOrDefault(x => x.Id == dish.RestaurantId);
                if (restaurant == null)
                {
                    continue;
                }

                ur.RestaurantId = anonymousRating.RestaurantId;
                ur.Rate = anonymousRating.Rate;
                ur.UserFirstName = anonymousRating.UserIp;
                ur.UpdateDate = anonymousRating.UpdateDate;
                ur.DishName = dish.Name.Get();
                ur.RestaurantName = restaurant.Name;

                ratings.Add(ur);
            }

            return ratings;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangeRatingStatus(string id, RatingStatus status)
        {
            var rating = await _core.Ratings.GetRatingById(id);
            if (rating != null)
            {
                rating.Status = status;
                rating.UpdateDate = DateTime.Now;
                await _core.Ratings.Update(rating);
                return Ok();
            }
            return BadRequest();
        }
    }
}

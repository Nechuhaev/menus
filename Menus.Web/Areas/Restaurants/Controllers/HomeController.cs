// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Areas.Restaurants.Controllers
{
    [Area("Restaurants")]
    [Authorize(Roles = "admin, manager, restaurant, restaurant-manager")]
    public class HomeController : Controller
    {
        private readonly Core _core;
        private readonly UserService _usrService;

        public HomeController(Core core, UserService userService)
        {
            _core = core;
            _usrService = userService;
        }


        public IActionResult Index()
        {
            return RedirectToAction("Index", "Manage");
        }

        public IActionResult Password()
        {
            return View();
        }

        public IActionResult Faq()
        {
            return View();
        }

        public IActionResult Article()
        {
            return View();
        }

        public IActionResult Profile()
        {
            return View();
        }

        public IActionResult DishesList()
        {
            return View();
        }

        // GET: Ratings
        public async Task<IActionResult> Ratings(int? status = null, int page = 0)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            ViewData["status"] = status;
            ViewData["page"] = page;
            RatingStatus? selectedStatus = null;
            if (status != null)
            {
                selectedStatus = (RatingStatus)status;
            }
            var Ratings = (await _core.Ratings.GetUserRatings(null, selectedStatus ?? null)).Where(x => x.RestaurantId == selectedRestaurant.Id).Skip(page * 10).Take(10).ToList();
            return View(Ratings);
        }

        [HttpGet]
        public async Task<IActionResult> RatingThread(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var rating = await _core.Ratings.GetRatingById(id);

            if (rating.RestaurantId != selectedRestaurant.Id) return NotFound();

            return View(rating);
        }

        [HttpPost]
        public async Task<IActionResult> RatingThread(string id, string response)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var rating = await _core.Ratings.GetRatingById(id);

            if (rating.RestaurantId != selectedRestaurant.Id) return NotFound();

            var newRating = new Rating {
                DishId = rating.DishId,
                Id = Guid.NewGuid().ToString(),
                RestaurantId = rating.RestaurantId,
                Text = response,
                UserId = User.Identity.Name,
                UpdateDate = DateTime.Now,
                Status = RatingStatus.Approved,
                ParentId = rating.Id,
                Rate = rating.Rate
            };

            await _core.Ratings.Create(newRating);

            return RedirectToAction("Ratings");
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
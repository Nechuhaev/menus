// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Routing;

namespace Menus.Web.Services
{
    public class UserRatingStats
    {
        public int RatingsAmount { get; set; }
        public int FeedbacksAmount { get; set; }
    }
    public class UserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private ApplicationUser _user;
        private readonly Core _core;

        public UserService(UserManager<ApplicationUser> userManager, Core core)
        {
            _userManager = userManager;
            _user = null;
            _core = core;
        }

        public async Task<ApplicationUser> GetUserById(string id)
        {   if (_user != null && _user.Id ==  id) return _user;

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) user = new ApplicationUser();

            _user = user;
            return user; 
        }

        public async Task<UserRatingStats> GetUserStats(string id)
        {
            UserRatingStats result = new UserRatingStats
            {
                RatingsAmount = 0,
                FeedbacksAmount = 0
            };
            if (id != null)
            {
                var userRatings = await _core.Ratings.GetAllUserRatings(id);
                if (userRatings.Count() > 0)
                {
                    result.RatingsAmount = userRatings.Where(x => x.Text != null && x.Text != "").Count();
                    result.FeedbacksAmount = userRatings.Count();
                } 
            }
            return result;
        }

        public bool IsMobileUser(HttpRequest request)
        {
            //return true;

            var queryParam = request.Query["device"].FirstOrDefault();
            if (!string.IsNullOrEmpty(queryParam))
            {
                switch (queryParam)
                {
                    case "desktop":
                        return false;
                    case "mobile":
                        return true;
                }
            }

            var userAgent = request.Headers["User-Agent"].FirstOrDefault();
            if (string.IsNullOrEmpty(userAgent)) userAgent = "";
            var macosPlatforms = new List<string> { "Macintosh", "MacIntel", "MacPPC", "Mac68K" };
            var windowsPlatforms = new List<string> { "Win32", "Win64", "Windows", "WinCE" };
            var iosPlatforms = new List<string> { "iPhone", "iPad", "iPod" };
            var device = "";

            if (macosPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "desktop";
            }
            else if (iosPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "mobile";
            }
            else if (windowsPlatforms.Any(x => userAgent.Contains(x)))
            {
                device = "desktop";
            }
            else if (userAgent.Contains("Android"))
            {
                device = "mobile";
            }
            else if (userAgent.Contains("Linux"))
            {
                device = "desktop";
            }

            return device == "mobile";
        }

        public bool IsIphone(HttpRequest request)
        {
            var userAgent = request.Headers["User-Agent"].FirstOrDefault();

            if (string.IsNullOrEmpty(userAgent))
            {
                userAgent = "";
            }

            var iosPlatforms = new List<string> { "iPhone" };
            bool result = iosPlatforms.Any(x => userAgent.Contains(x));

            return result;
        }

        public bool IsIosWebView(HttpRequest request)
        {
            var userAgent = request.Headers["User-Agent"].FirstOrDefault();
            if (string.IsNullOrEmpty(userAgent)) userAgent = "";
            var iosPlatforms = new List<string> { "iPhone", "iPad", "iPod" };

            if (iosPlatforms.Any(x => userAgent.Contains(x)) && !userAgent.Contains("Version"))
            {
                return true;
            }

            return false;
        }

        public async Task<IRestaurant> GetSelecedRestaurant(ClaimsPrincipal user, HttpRequest request)
        {
            var selectedRestaurantId = request.HttpContext?.GetRouteValue("restaurantId")?.ToString() ?? request.Cookies[".Menus.SelectedRestaurant"];
            if (string.IsNullOrEmpty(selectedRestaurantId)) return null;
            var selectedRestaurant = (user.IsInRole("admin") || user.IsInRole("manager") ?
                await _core.Restaurants.GetRestaurantById(selectedRestaurantId) :
                await _core.Restaurants.GetSelectedRestaurantForUser(selectedRestaurantId, user.Identity.Name));

            return selectedRestaurant;
        }
    }
}

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
    public partial class HomeController : Controller
    {
        private readonly Core _core;
        private readonly UserService _userService;
        private readonly EmailService _email;
        private readonly IConfiguration _config;
        private readonly IHostingEnvironment _env;
        private readonly UrlGenerateService _urls;
        private readonly IDbContext _db;
        private readonly AbTestService _abTests;
        private readonly IHttpContextAccessor _accessor;
        private readonly RestaurantRegistrationService _registration;

        public HomeController(
            Core core, 
            UserService userService, 
            EmailService email, 
            IConfiguration config, 
            IHostingEnvironment env, 
            UrlGenerateService urls, 
            IDbContext db,
            AbTestService abTests,
            IHttpContextAccessor accessor,
            RestaurantRegistrationService registration)
        {
            _core = core;
            _userService = userService;
            _email = email;
            _config = config;
            _env = env;
            _urls = urls;
            _db = db;
            _abTests = abTests;
            _accessor = accessor;
            _registration = registration;
        }

        public async Task<IActionResult> Index(string culture, string email, string token, string ReturnUrl)
        {
            //Response.Cookies.Append(
            //    CookieRequestCultureProvider.DefaultCookieName,
            //    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("ru")),
            //    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
            //);

            if (string.IsNullOrWhiteSpace(culture))
            {
                culture = Request.Path.ToString().Substring(1, 3);
            }

            SetCultureCookie();

            if ((culture != "ru" && culture != "uk" && culture != "en") ||
               (_config["DisableLanguages:uk"].ToLower() != "false" && culture == "uk") ||
               (_config["DisableLanguages:en"].ToLower() != "false" && culture == "en"))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            if (!string.IsNullOrEmpty(Request.Cookies[".Menus.RedirectUrl"]))
            {
                byte[] data = Convert.FromBase64String(Request.Cookies[".Menus.RedirectUrl"]);
                string decodedString = Encoding.UTF8.GetString(data);
                var redirectUrl = WebUtility.UrlDecode(decodedString);
                Response.Cookies.Delete(".Menus.RedirectUrl");
                return Redirect(redirectUrl);
            }

            if (Request.Path.Value == "/" && !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(token)) return RedirectPermanent($"/{culture}/?email={email}&token={token}");
            if (Request.Path.Value == "/" && !string.IsNullOrEmpty(ReturnUrl)) return RedirectPermanent($"/{culture}/?ReturnUrl={ReturnUrl}");
            if (Request.Path.Value == "/") return RedirectPermanent($"/{culture}");

            var cities = (await _core.Restaurants.GetCities()).ToList();

            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            //var restaurants = (await _core.Restaurants.GetActiveRestaurantsWithUrls()).ToList();
            //var cityRestaurants = await _core.Restaurants.GetActiveRestaurantsWithUrls(selectedCity);
            //TODO: optimize featured/latest restaurants
            //ViewData["restaurants"] = JsonConvert.SerializeObject(cityRestaurants.Select(x => new { name = $"{x.Name} ({x.Address})", id = x.Id }).ToList());

            var latestRestaurantsString = Request.Cookies[".Menus.LatestRestaurants"];
            var latestRestaurants = !string.IsNullOrEmpty(latestRestaurantsString) ? JsonConvert.DeserializeObject<List<string>>(latestRestaurantsString) : new List<string>();
            var latestViewedRestaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => latestRestaurants.Contains(x.Url)).ToList();
            ViewData["latestViewedRestaurants"] = latestViewedRestaurants;
            ViewData["featuredRestaurants"] = _core.Restaurants.GetCompleteRestaurants().Where(x => x.IsFeatured && x.City == selectedCity).OrderBy(x => x.Name).Take(4).ToList();

            var latestAddedRestaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants()
                //.Where(x => !string.IsNullOrEmpty(x.Photos) && x.Photos.Length > 4)
                .Where(x => x.City == selectedCity && x.ShowOnIndex)
                .OrderByDescending(x => x.CreateDate)
                .Take(4)
                .ToList());

            ViewData["nearestRestaurants"] = latestAddedRestaurants;
            //ViewData["nearestRestaurants"] = (await _core.Restaurants.FindNearest(lat, lon)).Take(4).ToList();
            ViewData["latestBlogArticles"] = (await _core.Pages.GetBlog())
                .Where(x => x.Status == PageStatus.Published)
                .OrderByDescending(x => x.Timestamp)
                .Take(4)
                .ToList();
            ViewData["cities"] = JsonConvert.SerializeObject(cities.Select(x => new { name = x.Name.Get(culture), id = x.Id, terms = "" }).OrderBy(x => x.name));
            ViewData["selectedCity"] = cities.First(x => x.Id == selectedCity);

            ViewData["bestDishes"] = (await _core.Dishes.GetBestDishes(selectedCity, 4)).ToList();

            return View();
        }

        public async Task<IActionResult> GetAbTest(string name)
        {
            var abTest = _abTests.GetTestByName(name);
            if (abTest != null)
            {
                var result = await _abTests.FireParameterForTest(abTest);
                return Json(result);
            }
            else
            {
                return null;
            }
        }

        private string GetSelectedCityIdAndSetSelectedCityCookie()
        {
            var selectedCity = Request.Cookies[".Menus.SelectedCity"];
            var selectedCityName = Request.Cookies[".Menus.SelectedCityName"];
            if (string.IsNullOrEmpty(selectedCity))
            {
                Response.Cookies.Append(
                    ".Menus.SelectedCity",
                    "e2655e24-92dc-4179-abf6-c3908278a4de",
                    new CookieOptions {Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true}
                );

                selectedCity = "e2655e24-92dc-4179-abf6-c3908278a4de";
            }

            if (string.IsNullOrEmpty(selectedCityName))
            {
                Response.Cookies.Append(
                    ".Menus.SelectedCityName",
                    "kyiv",
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
                );

                selectedCityName = "kyiv";
            }

            ViewData["selectedCityName"] = selectedCityName;

            return selectedCity;
        }

        [ResponseCache(Duration = 7776000, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IActionResult> File(string id)
        {
            id = GetGuid(id);

            var file = await _core.Files.GetFileById(id);
            if (file == null) return NotFound();

            Response.ContentLength = file.Size;
            Response.ContentType = file.Mime;
            Response.Body.Write(file.Data);

            return new EmptyResult();
        }

        public async Task<IActionResult> Restaurant(string culture, string id, string city)
        {
            string cityCodeLanguage = "en";

            if ((culture != "ru" && culture != "uk" && culture != "en") ||
                (_config["DisableLanguages:uk"].ToLower() != "false" && culture == "uk") ||
                (_config["DisableLanguages:en"].ToLower() != "false" && culture == "en"))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            SetCultureCookie();

            var cities = (await _core.Restaurants.GetCities()).ToList();
            if (!cities.Select(x => x.Name.Get(cityCodeLanguage).ToLower()).Contains(city))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            if (restaurant == null)
            {
                var seos = (await _core.Restaurants.FindRestaurantsAutocomplete(id?.Trim(), selectedCity)).ToList();
                var seosIds = seos.Select(x => x.ItemId);
                var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seosIds.Contains(x.Id)).OrderBy(x => x.Name).Take(4).ToList();

                if(restaurants.Count > 0)
                {
                    ViewData["possibleRestaurants"] = restaurants;
                } else
                {
                    var featuredRestaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => x.IsFeatured && x.City == selectedCity).OrderBy(x => x.Name).Take(4).ToList();
                    ViewData["possibleRestaurants"] = featuredRestaurants;
                }
                

                Response.StatusCode = 404;
                return View("NotFoundRestaurant", id);
            };
            var seo = await _core.Restaurants.GetSeoByRestaurantId(restaurant.Id);

            if (!string.IsNullOrEmpty(GetGuid(id))) return RedirectToAction("Restaurant", new {id = seo.Url});
            
            ViewData["selectedCity"] = selectedCity;
            var restaurantCityObj = cities.FirstOrDefault(x => x.Id == restaurant.City);
            if (restaurantCityObj == null)
            {
                Response.StatusCode = 404;
                return View("NotFoundRestaurant", id);
            }
            if (restaurantCityObj.Name.Get("en").ToLower() != city)
            {
                return RedirectToAction("Restaurant", new { id, city = restaurantCityObj.Name.Get("en").ToLower() });
            }

            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurant.Id);
            ViewData["restaurantId"] = id;
            ViewData["seo"] = seo ?? new RestaurantSeo();
            UpdateLatestRestaurants(id, HttpContext);

            var menu = await GetMenuForRestaurant(restaurant);

            var photos = string.IsNullOrEmpty(restaurantInfo.Photos) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(restaurantInfo.Photos);
            var (openHours, schedulePerDay) = OpenHoursHelper.Parse(restaurantInfo.OpenHours);
            var menuPhotos = (await _core.Restaurants.GetMenuPhotosForRestaurant(restaurant.Id)).ToList();
            var role = await _core.Restaurants.GetUserRoleForRestaurant(restaurantInfo.RestaurantId,
                User.Identity.Name);

            var data = new RestaurantViewModel {
                Restaurant = restaurant,
                RestaurantInfo = restaurantInfo,
                UserRole = role,
                Menu = menu,
                Photos = photos,
                OpenHours = openHours,
                SchedulePerDay = schedulePerDay,
                MenuPhotos = menuPhotos
            };

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadMenuPhoto";
            ViewData["valueName"] = "restaurantId";
            ViewData["value"] = data.Restaurant.Id;

            Response.Headers.Remove("Last-Modified");
            Response.Headers.Add("Last-Modified", restaurant.UpdateDate.ToString("R"));

            return View(data);
        }

        public IActionResult StatusCodeError(string id)
        {
            //if (id == "404")
            //{
            //    return View("NotFound");
            //}
            //return RedirectToAction("Error");

            var path = ControllerContext.HttpContext.Request.Path.ToString();
            if (!path.Contains("/ru/") && !path.Contains("/en/") && !path.Contains("/uk/"))
            {
                var rqf = ControllerContext.HttpContext.Features.Get<IRequestCultureFeature>();
                var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;
                return RedirectToAction("StatusCodeError", new { id, culture });
            }

            Response.StatusCode = 404;
            return View("NotFound");
        }

        private async Task<DishesListViewModel> GetMenuForRestaurant(IRestaurant restaurant)
        {
            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(restaurant.Id)).ToList();
            var dishes =
                (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restaurant.Id, User.Identity.Name)).ToList();
            var dishCategories = (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id)))
                .OrderBy(x => x.Index).ToList();

            ViewData["topDishes"] = dishes.Where(x => x.Photos.Any() && x.RatingFloat > 2).OrderByDescending(x => x.RatingFloat).Take(5).ToList();

            var menu = new DishesListViewModel {Categories = new List<DishesListViewModelCategory>()};

            foreach (var dishCategory in dishCategories)
            {
                var modelCategory = menu.Categories.FirstOrDefault(x => x.Id == dishCategory.MenuCategoryId);
                if (modelCategory == null)
                {
                    var category = categories.First(x => x.Id == dishCategory.MenuCategoryId);
                    modelCategory = new DishesListViewModelCategory
                    {
                        Active = category.Active,
                        Id = category.Id,
                        Index = category.Index,
                        RestaurantId = category.RestaurantId
                    };
                    var iModelCategory = (IMenuCategory) modelCategory;
                    iModelCategory.Name = category.Name;

                    menu.Categories.Add(modelCategory);
                }

                var dishList = modelCategory.Dishes;
                if (dishList == null)
                {
                    dishList = new List<IDishWithSeoAndRatings>();
                    modelCategory.Dishes = dishList;
                }
                dishList.Add(dishes.First(x => x.Id == dishCategory.DishId));
            }

            menu.Categories = menu.Categories.OrderBy(x => x.Index).ToList();

            var includedDishIds = dishCategories.GroupBy(x => x.DishId).Select(x => x.Key);
            var allDishIds = dishes.Select(x => x.Id);
            var excludedDishes = allDishIds.Except(includedDishIds);

            var excludedCategory = new DishesListViewModelCategory
            {
                Active = true,
                Id = "",
                Index = 99999,
                RestaurantId = restaurant.Id
            };
            var excludedCategoryName = new LocalizableString { Values = new List<LocalizableStringValue>() };
            excludedCategoryName.Add(new LocalizableStringValue { Locale = "ru", Value = "Без категории" });
            excludedCategoryName.Add(new LocalizableStringValue { Locale = "en", Value = "Empty category" });
            excludedCategoryName.Add(new LocalizableStringValue { Locale = "uk", Value = "Без категорії" });
            ((IMenuCategory)excludedCategory).Name = excludedCategoryName;

            excludedCategory.Dishes =
                dishes.Where(x => excludedDishes.Contains(x.Id)).OrderBy(x => x.Name.Get()).ToList();
            if (excludedCategory.Dishes.Any()) menu.Categories.Add(excludedCategory);

            return menu;
        }

        public async Task<IActionResult> Dish(string culture, string id, string dishId, string city, int page = 0)
        {
            if ((culture != "ru" && culture != "uk" && culture != "en") ||
                (_config["DisableLanguages:uk"].ToLower() != "false" && culture == "uk") ||
                (_config["DisableLanguages:en"].ToLower() != "false" && culture == "en"))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            SetCultureCookie();

            var cities = (await _core.Restaurants.GetCities()).ToList();
            if (!cities.Select(x => x.Name.Get("en").ToLower()).Contains(city))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            if (restaurant == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            ViewData["selectedCity"] = selectedCity;
            var restaurantCityObj = cities.FirstOrDefault(x => x.Id == restaurant.City);
            if (restaurantCityObj == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            if (restaurantCityObj.Name.Get("en").ToLower() != city)
            {
                return RedirectToAction("Dish", new { id, dishId, city = restaurantCityObj.Name.Get("en").ToLower() });
            }
            ViewData["restaurantCity"] = restaurantCityObj;
            
            UpdateLatestRestaurants(id, HttpContext);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurant.Id);
            var dish = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, restaurant.Id, User.Identity.Name);
            if (dish == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            //var reviews = await _core.Ratings.GetRatingForDish();
            if (!_userService.IsMobileUser(Request))
            {
                var menu = await GetMenuForRestaurant(restaurant);
                ViewData["menu"] = menu;
            }

            var ratings = await _core.Dishes.GetRatingsWithUsersForDish(dish.Id);
            ratings = page == -1 ? ratings.Where(x => x.UserId == User.Identity.Name).ToList() : ratings;

            var dishes =
                (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restaurant.Id, User.Identity.Name)).OrderByDescending(x => x.RatingFloat).ToList();

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(restaurant.Id)).OrderBy(x => x.Index).ToList();

            var dishCategories = (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id)))
                .OrderBy(x => x.Index).ToList();

            if(dishCategories.Count > 0)
            {
                var currentDishCategory = dishCategories.FirstOrDefault(x => x.DishId == dish.Id);

                if (currentDishCategory != null)
                {
                    var sameCategoryDishes = dishCategories.Where(x => x.Id != currentDishCategory.Id && x.MenuCategoryId == currentDishCategory.MenuCategoryId).Select(x => x.DishId);
                    ViewData["similarDishes"] = dishes.Where(x => sameCategoryDishes.Contains(x.Id)).OrderByDescending(x => x.RatingFloat).Take(5).ToList();
                }
                else
                {
                    ViewData["similarDishes"] = dishes.Where(x => x.RatingFloat > 0 && x.Id != dish.Id).Take(5).ToList();
                }
            } else
            {
                ViewData["similarDishes"] = dishes.Where(x => x.RatingFloat > 0 && x.Id != dish.Id).Take(5).ToList();
            }
            

            ViewData["restaurantId"] = id;
            ViewData["restaurant"] = restaurant;
            ViewData["restaurantInfo"] = restaurantInfo;
            ViewData["ratings"] = ratings;
            ViewData["page"] = page;
            ViewData["images"] = (await _core.Dishes.GetDishPhotos(dish.Id)).OrderByDescending(x => x.Timestamp);

            if (User.Identity.IsAuthenticated)
            {
                var review = await _core.Ratings.GetDishRatingForUser(dish.Id, User.Identity.Name);
                ViewData["review"] = review;
            } else
            {
                var prevRate = await GetAnonymousRatingByDishId(dish.Id);
                if(prevRate != null)
                {
                    dish.UserRating = prevRate.Rate;
                }
            }
            

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadDishPhoto";
            ViewData["valueName"] = "dishId";
            ViewData["value"] = dish.Id;
            ViewData["AllowRateDishAnonymous"] = _config["RateDishAnonymous:enable"];

            Response.Headers.Remove("Last-Modified");
            Response.Headers.Add("Last-Modified", restaurant.UpdateDate.ToString("R"));

            return page == 0 ? View(dish) : View("_DishRatingsAjax", dish);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        // Сторінка для необроблених винятків: код 500, а не 404, щоб пошукові системи
        // не викидали сторінку з індексу через тимчасову помилку
        public IActionResult Error()
        {
            Response.StatusCode = 500;
            return View("NotFound");
        }

        private async Task LogEvent(string name, object data)
        {
            var logItem = new LogItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = User.Identity.Name,
                DeviceId = Request.Cookies[".Menus.DeviceId"] ?? "system",
                Event = name,
                EventData = JsonConvert.SerializeObject(data),
                Timestamp = DateTime.Now
            };
            await _core.LogItems.Create(logItem);
        }

        private static string GetGuid(string input) {
            if (input == null) return "";
            Regex regex = new Regex("[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}");
            Match match = regex.Match(input);
            if (match.Success)
            {
                return match.Value;
            }

            return "";
        }

        private void SetCultureCookie()
        {
            var cultureCookie = ControllerContext.HttpContext.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName];
            var culture = ControllerContext.HttpContext.GetRouteValue("culture")?.ToString();
            if (!string.IsNullOrEmpty(culture) && (culture == "ru" || culture == "uk" || culture == "en"))
            {
                if (string.IsNullOrEmpty(cultureCookie) ||
                    culture != CookieRequestCultureProvider.ParseCookieValue(cultureCookie).Cultures.First().Value)
                {
                    ControllerContext.HttpContext.Response.Cookies.Append(
                        CookieRequestCultureProvider.DefaultCookieName,
                        CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(new CultureInfo(culture))),
                        new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(10), IsEssential = true }
                    );
                }
            }
        }

        private static void UpdateLatestRestaurants(string restaurantId, HttpContext context)
        {
            var latestRestaurantsString = context.Request.Cookies[".Menus.LatestRestaurants"];
            var latestRestaurants = !string.IsNullOrEmpty(latestRestaurantsString) ? JsonConvert.DeserializeObject<List<string>>(latestRestaurantsString) : new List<string>();

            if (latestRestaurants.FirstOrDefault() == restaurantId) return;

            if (latestRestaurants.Count == 4) latestRestaurants.RemoveAt(3);

            latestRestaurants.Insert(0, restaurantId);

            context.Response.Cookies.Append(".Menus.LatestRestaurants", JsonConvert.SerializeObject(latestRestaurants), new CookieOptions { HttpOnly = true, IsEssential = true, Expires = DateTime.Now.AddYears(1) });
        }

        private string GetIpAddress()
        {
            var ipAddress = Request.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();//_accessor.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString()
            return ipAddress;
        }

        public static string GetIpAddress(HttpRequest request)
        {
            var ipAddress = string.Empty;
            var emptyValues = default(Microsoft.Extensions.Primitives.StringValues);
            Microsoft.Extensions.Primitives.StringValues header = 
                request.Headers.FirstOrDefault(h => h.Key == "CF-Connecting-IP").Value;

            if (header == emptyValues)
            {
                header = request.Headers.FirstOrDefault(h => h.Key == "X-Forwarded-For").Value;
            }
                

            if (header != emptyValues)
                ipAddress = header.First();
            else
                ipAddress = request.HttpContext.Connection.RemoteIpAddress.ToString();

            return ipAddress;
        }
    }
}

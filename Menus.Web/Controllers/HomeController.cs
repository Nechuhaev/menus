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
    public class HomeController : Controller
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

        public HomeController(
            Core core, 
            UserService userService, 
            EmailService email, 
            IConfiguration config, 
            IHostingEnvironment env, 
            UrlGenerateService urls, 
            IDbContext db,
            AbTestService abTests,
            IHttpContextAccessor accessor)
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

        public async Task<IActionResult> BestDishes(int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            var dishes = (await _core.Dishes.GetBestDishes(selectedCity, 12, page * 12)).ToList();

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            return page == 0 ? View(dishes) : View("_BestDishesAjax", dishes);
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

        public async Task<IActionResult> SelectCity(string cityId)
        {
            Response.Cookies.Append(
                ".Menus.SelectedCity",
                cityId,
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
            );

            var cities = (await _core.Restaurants.GetCities()).ToList();

            var city = cities.First(x => x.Id == cityId);

            Response.Cookies.Append(
                ".Menus.SelectedCityName",
                city.Name.Get("en").ToLower(),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
            );

            return Ok();
        }

        public IActionResult SearchDish(string dish_q, string sort)
        {
            return RedirectToAction("SearchResult", new { q = dish_q, sort });
        }

        public async Task<IActionResult> SearchResult(string q, string sort, int page = 0)
        {
            if (string.IsNullOrEmpty(q))
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            ViewData["featuredRestaurants"] = _core.Restaurants.GetCompleteRestaurants().Where(x => x.City == selectedCity).Take(5).ToList();
            CompleteDishesOrderBy rSort = CompleteDishesOrderBy.Rating;
            switch (sort)
            {
                case "rating":
                    rSort = CompleteDishesOrderBy.Rating;
                    break;
                case "rating-count":
                    rSort = CompleteDishesOrderBy.RatingsCount;
                    break;
                case "distance":
                    rSort = CompleteDishesOrderBy.Distance;
                    break;
            }
            var results = await _core.Dishes.GetCompleteDishes(q, lat, lon, selectedCity, rSort, page * 28, 28);
            results = ProcessDishResult(results, q);

            if (!results.Any())
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }

            return page == 0 ? View(results) : View("_SearchResultAjax", results);
        }

        [Authorize]
        public async Task<IActionResult> MobilePhoto(string dishId)
        {
            var dish = await _core.Dishes.GetDishById(dishId);
            var dishWithSeo = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, dish.RestaurantId, User.Identity.Name);
            var restaurant = await _core.Restaurants.GetRestaurantById(dish.RestaurantId);
            var restaurantSeo = await _core.Restaurants.GetSeoByRestaurantId(restaurant.Id);

            ViewData["dish"] = dishWithSeo;
            ViewData["restaurant"] = restaurant;
            ViewData["restaurantSeo"] = restaurantSeo;

            return View();
        }

        public async Task<IActionResult> DishAutocomplete(string q, string sortOrder)
        {
           CompleteDishesOrderBy dishSortOrder = CompleteDishesOrderBy.Rating;

            if (string.IsNullOrEmpty(q))
            {
                return BadRequest("q is empty");
            }

            switch (sortOrder)
            {
                case "rating":
                    dishSortOrder = CompleteDishesOrderBy.Rating;
                    break;
                case "rating-count":
                    dishSortOrder = CompleteDishesOrderBy.RatingsCount;
                    break;
                case "distance":
                    dishSortOrder = CompleteDishesOrderBy.Distance;
                    break;
            }

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            var result = (await _core.Dishes.GetCompleteDishes(q, lat, lon, selectedCity, dishSortOrder, 0, 10));
            result = ProcessDishResult(result, q);

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            var resultModel = result.Where(x => x.Name.Get().IndexOf(q, StringComparison.InvariantCultureIgnoreCase) >= 0)
                .Select(x => new
                {
                    name = x.Name.Get(),
                    id = x.Name.Get(),
                    terms = x.SearchTerms,
                    location = $"{x.RestaurantName} - {x.RestaurantDisplayAddress.Get(culture)}",
                    distance = (x.RestaurantDistance?.ToString("F1") ?? "--") + " km",
                    rating = x.RatingFloat.ToString("F1"),
                    ratingPercent = x.Rating * 20
                }).ToList();

            return Ok(resultModel);
        }

        public IEnumerable<ICompleteDish> ProcessDishResult(IEnumerable<ICompleteDish> result, string search)
        {
            foreach (ICompleteDish dish in result)
            {
                if (!dish.Name.GetAll().ToLower().Contains(search.ToLower()))
                {
                    List<LocalizableStringValue> newValues = new List<LocalizableStringValue>();

                    foreach (LocalizableStringValue locStrig in dish.Name.Values)
                    {
                        string locale = locStrig.Locale;
                        string categoryName = dish.CategoryName.Get(locale);
                        string dishName = dish.Name.Get(locale);
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            LocalizableStringValue value = new LocalizableStringValue
                            {
                                Locale = locale,
                                Value = $"{categoryName}: {dish.Name.Get(locale)}"
                            };
                            newValues.Add(value);
                        }
                        else
                        {
                            newValues.Add(locStrig);
                        }
                    }

                    dish.Name.Replace(newValues);
                }
            }

            return result;
        }

        public async Task<IActionResult> RestaurantsAutocomplete(string q)
        {
            if (string.IsNullOrEmpty(q)) return BadRequest("q is empty");

            //q = MySql.Data.MySqlClient.MySqlHelper.EscapeString(q);

            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            var seos = (await _core.Restaurants.FindRestaurantsAutocomplete(q, selectedCity)).ToList();
            var seosIds = seos.Select(x => x.ItemId);
            var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seosIds.Contains(x.Id)).ToList();
            var result = seos.Where(x => restaurants.Any(y => y.Id == x.ItemId)).Select(seo =>
            {
                var restaurant = restaurants.FirstOrDefault(x => x.Id == seo.ItemId);

                string distance = null;
                double distanceDouble = 0;

                if (restaurant.GpsLatitude != null)
                {
                    var udLat = Convert.ToDouble(lat, new CultureInfo("en-US"));
                    var udLon = Convert.ToDouble(lon, new CultureInfo("en-US"));

                    var rdLat = Convert.ToDouble(restaurant.GpsLatitude, new CultureInfo("en-US"));
                    var rdLon = Convert.ToDouble(restaurant.GpsLongitude, new CultureInfo("en-US"));

                    var userCoord = new GeoCoordinate(udLat, udLon);
                    var restaurantCoord = new GeoCoordinate(rdLat, rdLon);

                    distanceDouble = userCoord.GetDistanceTo(restaurantCoord) / 1000;
                    distance = (distanceDouble).ToString("F1") + " km";
                }

                //var bestDish = await _core.Dishes.GetBestDishForRestaurant(restaurant.Id);
                //bestDish = bestDish ?? new DishWithSeoAndRatings();

                return new
                {
                    name = restaurant.Name,
                    location = restaurant.DisplayAddress.Get(culture),
                    id = seo.Url,
                    terms = seo.SearchTerms,
                    distance,
                    distanceDouble
                    //rating = bestDish.RatingFloat.ToString("F1"),
                    //ratingPercent = bestDish.Rating * 20
                };
            }).ToList();

            return Ok(result.OrderBy(x => x.distanceDouble));
        }

        public async Task<IActionResult> GetCityRestaurants(string cityId)
        {
            var cityRestaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => x.City == cityId).ToList());

            var rqf = Request.HttpContext.Features.Get<IRequestCultureFeature>();
            // Culture contains the information of the requested culture
            var culture = rqf.RequestCulture.Culture.TwoLetterISOLanguageName;

            return Ok(cityRestaurants.Select(x => new { name = $"{x.Name} ({x.DisplayAddress.Get(culture)})", id = x.Url }).ToList());
        }

        public async Task<IActionResult> GetCities(string culture)
        {
            var cities = await _core.Restaurants.GetCities();

            return Ok(cities.OrderBy(x => x.Name.Get(culture)).Select(x => new { name = $"{x.Name.Get(culture)}", id = x.Id, terms = "" }).ToList());
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

        public IActionResult FindRestaurant(string q, string q_id)
        {
            return RedirectToAction("Restaurant", new { id = string.IsNullOrEmpty(q_id) ? q : q_id });
        }

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

        [Authorize]
        public async Task<IActionResult> SendTypo(string email, string text, string url)
        {
            var userId = User.Identity.Name;
            var typo = new TypoRequest
            {
                Id = Guid.NewGuid().ToString(),
                CreateDate = DateTime.Now,
                Text = text,
                Url = url,
                Email = email,
                UserId = string.IsNullOrEmpty(userId) ? "1a950ec5-7fd3-4d82-adca-d27fda6025b5" : userId
            };

            url = url.Replace($"{Request.Scheme}://{Request.Host}", "");

            await _core.Typos.Create(typo);

            return LocalRedirect(url);
        }

        [Authorize]
        public IActionResult MobileTypo(string url)
        {
            ViewData["url"] = url;
            return View();
        }

        public async Task<IActionResult> MobileRate(string dishId, string restaurantId, string returnUrl)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(restaurantId);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurantId);
            var restaurantSeo = await _core.Restaurants.GetSeoByRestaurantId(restaurantId);
            var dish = await _core.Dishes.GetDishByIdOrSeoWithSeoAndRating(dishId, restaurantId, User.Identity.Name);
            var review = await _core.Ratings.GetDishRatingForUser(dishId, User.Identity.Name);
            ViewData["restaurant"] = restaurant;
            ViewData["dish"] = dish;
            ViewData["review"] = review;
            ViewData["restaurantSeo"] = restaurantSeo;
            ViewData["restaurantInfo"] = restaurantInfo;
            ViewData["returnUrl"] = returnUrl;
            return View();
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
            var restaurantCityObj = cities.First(x => x.Id == restaurant.City);
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
            var openHours = JsonConvert.DeserializeObject<JObject>(restaurantInfo.OpenHours);
            var schedulePerDay = openHours["perDay"].Value<bool>();
            var openHoursData = openHours["data"].Value<JArray>();
            var currentDay = (int)DateTime.Now.DayOfWeek;
            currentDay = currentDay == 0 ? 6 : currentDay - 1;
            var openedFrom = openHoursData[schedulePerDay ? currentDay : 0]["from"].Value<string>();
            var openedTo = openHoursData[schedulePerDay ? currentDay : 0]["to"].Value<string>();
            var menuPhotos = (await _core.Restaurants.GetMenuPhotosForRestaurant(restaurant.Id)).ToList();
            var role = await _core.Restaurants.GetUserRoleForRestaurant(restaurantInfo.RestaurantId,
                User.Identity.Name);

            var data = new RestaurantViewModel {
                Restaurant = restaurant,
                RestaurantInfo = restaurantInfo,
                UserRole = role,
                Menu = menu,
                Photos = photos,
                OpenHours = schedulePerDay ? openHoursData.ToString() : $"{openedFrom} - {openedTo}",
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

        public IActionResult UseProfiler(string pwd)
        {
            if (pwd == "B8ttoFRTQ9azs5SFICs3")
            {
                Response.Cookies.Append(
                    ".Menus.UseProfiler",
                    "true",
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
                );
            }

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> ContentPage(string id)
        {
            var page = await _core.Pages.GetPublishedByUrl(id, PageCategory.Content);
            return page == null ? View("NotFound") : View(page);
        }

                        public async Task<IActionResult> NewsPage(string id)
        {
            var page = await _core.Pages.GetPublishedByUrl(id, PageCategory.News);

            if(string.IsNullOrEmpty(id))
            {
                return View("Blog", (await _core.Pages.GetBlog()).Where(x => x.Status == PageStatus.Published));
            }

            if (page != null)
            {
                page.Views += 1;
                await _core.Pages.Update(page);
            }

            ViewData["otherBlogs"] = (await _core.Pages.GetBlog())
                .Where(x => x.Id != page.Id && x.Status == PageStatus.Published)
                .OrderByDescending(x => x.Timestamp)
                .Take(6)
                .ToList();
            return page == null ? View("NotFound") : View("BlogPage", page);
        }

        public async Task<IActionResult> Blog()
        {
            return View((await _core.Pages.GetBlog()).Where(x => x.Status == PageStatus.Published));
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


        [HttpGet]
        [Authorize]
        public IActionResult RegisterRestaurant(string name)
        {
            ViewData["name"] = name;
            return View();
        }

        public IActionResult RatingRecommendations()
        {
            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> EditRestaurant(string id)
        {
            var restaurantRole = await _core.Restaurants.GetUserRoleForRestaurant(id, User.Identity.Name);
            if (restaurantRole == null)
                return RedirectToAction("Index");

            var restaurant = await _core.Restaurants.GetRestaurantById(id);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(id);
            restaurant.Email = restaurantInfo.Cuisine.Get();
            return View(restaurant);
        }

        public async Task<IActionResult> Newest(string sort, int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            //var latestRestaurants = (await  _core.Restaurants.GetActiveRestaurants()).Where(x => x.City == selectedCity).OrderByDescending(x => x.CreateDate).Skip(page * 12).Take(12);
            //var latestRestaurantIds = latestRestaurants.Select(x => x.Id).ToList();

            //IEnumerable<ICompleteRestaurant> restaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => latestRestaurantIds.Contains(x.Id)).ToList());
            var restarauntsCreatedByAdmin = (await Task.Run(() => _core.Restaurants.GetRestaurantCreatedByAdmin())).Select(x => x.Id).ToList();


            var latestAddedRestaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants()
                //.Where(x => !string.IsNullOrEmpty(x.Photos) && x.Photos.Length > 4)
                .Where(x => x.City == selectedCity && restarauntsCreatedByAdmin.Contains(x.Id))
                .OrderByDescending(x => x.ShowOnIndex)
                .ThenByDescending(x => x.CreateDate)
                .Skip(page * 12)
                .Take(12)
                .ToList());

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            return page == 0 ? View(latestAddedRestaurants) : View("_FeaturedAndNearestAjax", latestAddedRestaurants);
        }

        public async Task<IActionResult> Featured(string sort, int page = 0)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            IEnumerable<ICompleteRestaurant> restaurants = await Task.Run(() => _core.Restaurants.GetCompleteRestaurants().Where(x => x.IsFeatured && x.City == selectedCity).ToList());

            switch (sort)
            {
                case "rating-count":
                    restaurants = restaurants.OrderByDescending(x => x.RatingsCount);
                    break;

                case "rating":
                    restaurants = restaurants.Select(x =>
                    {
                        var bestDish = _core.Dishes.GetBestDishForRestaurant(x.Id).Result;
                        bestDish = bestDish ?? new DishWithSeoAndRatings();

                        return new
                        {
                            restaurant = x,
                            rating = bestDish.RatingFloat
                        };
                    }).OrderByDescending(x => x.rating).Select(x => x.restaurant);
                    break;
            }

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            restaurants = restaurants.Skip(page * 12).Take(12);

            return page == 0 ? View(restaurants) : View("_FeaturedAndNearestAjax", restaurants);
        }

        public async Task<IActionResult> Nearest(string sort, int page = 0)
        {
            var lat = Request.Cookies[".Menus.UserLatitude"];
            var lon = Request.Cookies[".Menus.UserLongitude"];
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();

            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon))
            {
                var cities = (await _core.Restaurants.GetCities()).ToList();

                var city = cities.First(x => x.Id == selectedCity);
                lat = city.Latitude;
                lon = city.Longitude;
            }

            IEnumerable<ICompleteRestaurant> restaurants = (await _core.Restaurants.FindNearest(lat, lon)).ToList();

            switch (sort)
            {
                case "rating-count":
                    restaurants = restaurants.OrderByDescending(x => x.RatingsCount);
                    break;

                case "rating":
                    restaurants = restaurants.Select(x =>
                    {
                        var bestDish = _core.Dishes.GetBestDishForRestaurant(x.Id).Result;
                        bestDish = bestDish ?? new DishWithSeoAndRatings();

                        return new
                        {
                            restaurant = x,
                            rating = bestDish.RatingFloat
                        };
                    }).OrderByDescending(x => x.rating).Select(x => x.restaurant);
                    break;
            }

            ViewData["googleMapsKey"] = _config["Google:MapsKey"];

            restaurants = restaurants.Skip(page * 12).Take(12);

            return page == 0 ? View(restaurants) : View("_FeaturedAndNearestAjax", restaurants);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditRestaurant(Restaurant model, string culture)
        {
            culture = string.IsNullOrEmpty(culture) ? "ru" : culture;
            var cuisine = model.Email;

            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Empty name");

            var restaurantRole = await _core.Restaurants.GetUserRoleForRestaurant(model.Id, User.Identity.Name);
            if (restaurantRole == null)
                return RedirectToAction("Index");

            var restaurant = await _core.Restaurants.GetRestaurantById(model.Id);
            restaurant.Name = model.Name;
            restaurant.City = model.City;
            restaurant.Address = model.Address;
            restaurant.Phone = model.Phone;
            await _core.Restaurants.Update(restaurant);

            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(model.Id);
            restaurantInfo.Cuisine = string.IsNullOrEmpty(cuisine) ? new LocalizableString() : new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = cuisine, Locale = culture } } };
            restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = culture } } };
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);

            var seo = await _core.Restaurants.GetSeoByRestaurantId(model.Id);
            seo.Url = _urls.GetNewUrlForRestaurant(seo, restaurant);
            await _core.Restaurants.UpdateSeo(seo);

            return RedirectToAction("Restaurant", new { id = model.Id });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RegisterRestaurant(Restaurant model, string culture)
        {
            culture = string.IsNullOrEmpty(culture) ? "ru" : culture;
            var cuisine = model.Email;
            model.Email = "";

            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Empty name");

            var autoApproveUserRestaurants = _config["AutoApproveUserRestaurants"] == "true";

            model.Id = Guid.NewGuid().ToString();
            model.Status = autoApproveUserRestaurants ? RestaurantStatus.Active : RestaurantStatus.Pending;
            model.CreateDate = DateTime.Now;

            await _core.Restaurants.Create(model, new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                Role = RestaurantUserRole.Owner,
                UserId = User.Identity.Name
            });

            await _core.Restaurants.CreateRestaurantRole(new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                UserId = User.Identity.Name,
                Role = RestaurantUserRole.Creator
            });

            var restaurantInfo = new RestaurantInfo() as IRestaurantInfo;
            restaurantInfo.Id = Guid.NewGuid().ToString();
            restaurantInfo.Currency = "₴";
            restaurantInfo.Cuisine = string.IsNullOrEmpty(cuisine) ? new LocalizableString() : new LocalizableString{Values = new List<LocalizableStringValue>{ new LocalizableStringValue{ Value = cuisine, Locale = culture } }};
            restaurantInfo.FullInfo = new LocalizableString();
            restaurantInfo.ShortInfo = new LocalizableString();
            restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = culture } } };
            restaurantInfo.RestaurantId = model.Id;
            restaurantInfo.OpenHours = "{\"perDay\":false,\"data\":[{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true}]}";
            restaurantInfo.UserAllowedAddDishes = true;
            restaurantInfo.UserAllowedAddMenu = true;
            restaurantInfo.UserAllowedOpenMenu = true;
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);
            

            var seo = new RestaurantSeo() as IRestaurantSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.Title = new LocalizableString();
            seo.Header = new LocalizableString();
            seo.Description = new LocalizableString();
            seo.ItemId = model.Id;
            seo.Url = _urls.GetNewUrlForRestaurant(seo, model);
            seo.SearchTerms = model.Name + " " + seo.Url;
            await _core.Restaurants.CreateSeo(seo);

            return RedirectToAction("Restaurant", new { id = model.Id });
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

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UploadMenuPhoto(string base64, string restaurantId)
        {
            var restaurant = await _core.Restaurants.GetRestaurantById(restaurantId);
            if (restaurant == null) return BadRequest("Restaurant not found");

            var info = await _core.Restaurants.GetRestaurantInfo(restaurantId);
            if(!info.UserAllowedAddMenu) return BadRequest("Not allowed to add menus");

            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = Helpers.ImageHelper.GetMimeType(base64),
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            var menuPhoto = (IMenuPhoto) new MenuPhoto();
            menuPhoto.Id = Guid.NewGuid().ToString();
            menuPhoto.Photos = new List<string>{ file.Id + Helpers.ImageHelper.GetExtension(base64) };
            menuPhoto.RestaurantId = restaurantId;
            menuPhoto.UserId = User.Identity.Name;
            menuPhoto.Timestamp = DateTime.Now;
            menuPhoto.Index = 999;

            await _core.Restaurants.CreateMenuPhoto(menuPhoto);

            return Ok(new { id = menuPhoto.Id, name = file.Id + Helpers.ImageHelper.GetExtension(base64), approved = true });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteMenuPhoto(string id)
        {
            var menuPhoto = await _core.Restaurants.GetMenuPhotoById(id);
            if(menuPhoto == null) return BadRequest("Menu not found");
            if(menuPhoto.UserId != User.Identity.Name) return BadRequest("Not allowed to remove this menu");

            await _core.Files.Delete(GetGuid(menuPhoto.Photos.First()));
            await _core.Restaurants.DeleteMenuPhoto(id);

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> UpdateAllDishPhotos()
        {
            var dishes = (await _core.Dishes.GetAllDishes()).ToList();
            var timestamp = DateTime.Now;
            foreach (var dish in dishes)
            {
                foreach (var imageUrl in dish.Photos)
                {
                    var photo = await _core.Dishes.GetDishPhotoByImageUrl(imageUrl);
                    if (photo == null)
                    {
                        await _core.Dishes.AddUserDishPhoto(new DishPhoto
                        {
                            Id = Guid.NewGuid().ToString(),
                            ImageUrl = imageUrl,
                            Index = 999,
                            UserId = "1a950ec5-7fd3-4d82-adca-d27fda6025b5",
                            Status = DishPhotoStatus.Approved,
                            DishId = dish.Id,
                            Timestamp = timestamp
                        });
                    }
                }
            }

            return Ok("Done!");
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteDishPhoto(string id)
        {
            var dishPhoto = await _core.Dishes.GetDishPhotoById(id);
            if (dishPhoto == null) return BadRequest("Photo not found");
            if (dishPhoto.UserId != User.Identity.Name) return BadRequest("Not allowed to remove this photo");

            await _core.Files.Delete(GetGuid(dishPhoto.ImageUrl));
            await _core.Dishes.DeleteUserDishPhoto(dishPhoto);

            return Ok();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UploadDishPhoto(string base64, string dishId)
        {
            var autoApproveUserPhotos = _config["AutoApproveUserPhotos"] == "true";

            var dish = await _core.Dishes.GetDishById(dishId);

            if (dish == null) return BadRequest("Dish not found");

            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = Helpers.ImageHelper.GetMimeType(base64),
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            var dishPhoto = new DishPhoto
            {
                Id = Guid.NewGuid().ToString(),
                Index = 999,
                Status = autoApproveUserPhotos ? DishPhotoStatus.Approved : DishPhotoStatus.New,
                UserId = User.Identity.Name,
                DishId = dishId,
                ImageUrl = file.Id + Helpers.ImageHelper.GetExtension(base64),
                Timestamp = DateTime.Now
            };

            await _core.Dishes.AddUserDishPhoto(dishPhoto);

            return Ok(new { id = dishPhoto.Id, name = file.Id + Helpers.ImageHelper.GetExtension(base64), approved = autoApproveUserPhotos });
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
            var restaurantCityObj = cities.First(x => x.Id == restaurant.City);
            if (restaurantCityObj == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            if (restaurantCityObj.Name.Get("en").ToLower() != city)
            {
                return RedirectToAction("Dish", new { id, dishId, city = restaurantCityObj.Name.Get("en").ToLower() });
            }
            
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
        
        [Authorize]
        public async Task<IActionResult> CreateDish(string id)
        {
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            ViewData["selectedCity"] = selectedCity;

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            if (restaurant == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            UpdateLatestRestaurants(id, HttpContext);
            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(restaurant.Id);

            //var reviews = await _core.Ratings.GetRatingForDish();
            if (!_userService.IsMobileUser(Request))
            {
                var menu = await GetMenuForRestaurant(restaurant);
                ViewData["menu"] = menu;
            }

            ViewData["restaurantId"] = id;
            ViewData["restaurant"] = restaurant;
            ViewData["restaurantInfo"] = restaurantInfo;

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadDishPhoto";
            ViewData["valueName"] = "dishId";

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDish(UserCreateDishViewModel model)
        {
            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(model.RestaurantId);
            if (restaurant == null)
            {
                Response.StatusCode = 404;
                return View("NotFound");
            }
            if (string.IsNullOrEmpty(model.Name)) return BadRequest("Name is empty");

            if (model.Unit == null) model.Unit = " ";

            var dish = (IDish) new Dish();
            dish.Name = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Locale = model.Locale, Value = model.Name } } };
            dish.Description = new LocalizableString();
            dish.Id = Guid.NewGuid().ToString();
            dish.Ingredients = new LocalizableString();
            dish.IsPriceMultiple = false;
            dish.Photos = new List<string>();
            dish.Price = Convert.ToDecimal(model.Price);
            dish.RestaurantId = model.RestaurantId;
            dish.Status = DishStatus.Active;
            dish.Weight = model.Weight.ToString();
            dish.WeightType = model.Unit;
            dish.ShortDescription = new LocalizableString();

            await _core.Dishes.Create(dish);

            var price = new DishVariation
            {
                Id = Guid.NewGuid().ToString(),
                DishId = dish.Id,
                Price = dish.Price,
                Weight = model.Weight,
                Type = model.Unit
            };
            await _core.Dishes.CreateDishVariations(new List<IDishVariation> {price});

            var rating = new Rating
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.RestaurantId,
                Status = RatingStatus.Approved,
                Text = "",
                DishId = dish.Id,
                UserId = User.Identity.Name,
                Rate = Convert.ToInt32(model.Rating),
                UpdateDate = DateTime.Now
            };
            if (rating.Rate > 0)
            {
                await _core.Ratings.Create(rating);
                dish.RatingFloat = rating.Rate;
                dish.RatingsCount = 1;
                await _core.Dishes.Update(dish);
            }

            var seo = new DishSeo() as IDishSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.ItemId = dish.Id;
            seo.RestaurantId = dish.RestaurantId;
            seo.Url = await _urls.GetNewUrlForDish(seo, dish);
            seo.SearchTerms = dish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(dish.Name.Get());
            await _core.Dishes.CreateSeo(seo);

            if(!string.IsNullOrEmpty(model.Photo)) await UploadDishPhoto(model.Photo, dish.Id);

            await LogEvent("CreateDish", new { id = dish.Id, name = dish.Name.Get() });

            return RedirectToAction("Restaurant", new { id = restaurant.Id });
        }

        public async Task<IActionResult> FindNearestRestaurants(string lat, string lon)
        {
            GetSelectedCityIdAndSetSelectedCityCookie();
            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lon)) return Ok(new List<string>());
            var nearbyRestaurants = await _core.Restaurants.FindNearest(lat, lon);

            await LogEvent("UserPosition", new { latitude = lat, longitude = lon });

            return View("_NearestRestaurants", nearbyRestaurants.Take(10).ToList());
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return StatusCodeError("404");
        }

        public async Task<IActionResult> GetMenuUserInfo(string menuPhotoId)
        {
            var menuPhoto = await _core.Restaurants.GetMenuPhotoById(menuPhotoId);
            if (menuPhoto == null) return NotFound();
            var user = await _core.Users.GetUserById(menuPhoto.UserId);
            var ratingsCount = await _core.Ratings.GetRatingCountForUser(user.Id);

            ViewData["name"] = $"{user.FirstName} {user.LastName}";
            ViewData["photo"] = user.ImageUrl;
            ViewData["ratingsCount"] = ratingsCount;

            return View("_MenuUserInfo");
        }

        [HttpPost]
        public async Task<IActionResult> SendFormdata(IFormCollection form)
        {
            var mailText = new StringBuilder();
            mailText.AppendLine($"New form submit from {Request.Headers["Referer"]}<br/><br/>");
            foreach(var field in form)
            {
                mailText.AppendLine($"{field.Key}: {field.Value}<br/>");
            }

            _email.SendEmail(_config["formsEmailReceiver"], "Form submit", mailText.ToString());

            await LogEvent("FormSubmit", new
            {
                url = Request.Headers["Referer"],
                form = form.Select(x => new { key = x.Key, value = x.Value.FirstOrDefault() })
            });

            if (
                form.ContainsKey("Package") &&
                form.ContainsKey("Restaurant") &&
                form.ContainsKey("Email") &&
                form.ContainsKey("Phone")
            )
            {
                await _db.CustomerRequests.Add(new CustomerRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    UpdateDate = DateTime.Now,
                    CreateDate = DateTime.Now,
                    Status = CustomerRequestStatus.New,
                    Email = form["Email"],
                    RestaurantName = form["Restaurant"],
                    Package = form["Package"],
                    Phone = form["Phone"]
                });
            }

            Response.Headers.Add("Access-Control-Allow-Origin", "*");

            return Ok();
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

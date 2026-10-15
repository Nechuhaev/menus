// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Menus.Web.Helpers;
using Menus.Web.Models;
using Menus.Domain;
using System.Text.RegularExpressions;
using Menus.Web.Models.ViewModels;
using Menus.Abstract.Models;
using Menus.Web.Services;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using DishStatus = Menus.Abstract.Models.DishStatus;
using DishVariation = Menus.Web.Models.DishVariation;
using LogItem = Menus.Web.Models.LogItem;
using RestaurantInfo = Menus.Web.Models.RestaurantInfo;
using GeoCoordinatePortable;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using Menus.Abstract;
using Microsoft.AspNetCore.Hosting;


namespace Menus.Web.Controllers
{
    public class IframeController : Controller
    {
        private readonly Core _core;
        private readonly IConfiguration _config;
        private readonly LocaleService _localeSrvc;

        public IframeController(Core core, IConfiguration config, LocaleService localeSrvc)
        {
            _core = core;
            _config = config;
            _localeSrvc = localeSrvc;
        }

        public async Task<IActionResult> Restaurant(string culture, string id, string city)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            if (restaurant == null)
            {
                var seos = (await _core.Restaurants.FindRestaurantsAutocomplete(id.Trim(), selectedCity)).ToList();
                var seosIds = seos.Select(x => x.ItemId);
                var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seosIds.Contains(x.Id)).Take(4).ToList();

                ViewData["possibleRestaurants"] = restaurants;

                Response.StatusCode = 404;
                return View("NotFoundRestaurant", id);
            };
            var seo = await _core.Restaurants.GetSeoByRestaurantId(restaurant.Id);

            if (!string.IsNullOrEmpty(GetGuid(id))) return RedirectToAction("Restaurant", new { id = seo.Url });

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
            var (openHours, schedulePerDay) = OpenHoursHelper.Parse(restaurantInfo.OpenHours);
            var menuPhotos = (await _core.Restaurants.GetMenuPhotosForRestaurant(restaurant.Id)).ToList();
            var role = await _core.Restaurants.GetUserRoleForRestaurant(restaurantInfo.RestaurantId,
                User.Identity.Name);

            var data = new RestaurantViewModel
            {
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

            IframeSettingsModel iframeSettings = new IframeSettingsModel();

            if (!string.IsNullOrEmpty(seo.CustomIframeSettings) && !string.IsNullOrEmpty(seo.CustomIframeSettings.Trim()))
            {
                iframeSettings = JsonConvert.DeserializeObject<IframeSettingsModel>(seo.CustomIframeSettings);
            } else
            {
                iframeSettings = new IframeSettingsModel
                {
                    ShowHeader = !string.IsNullOrEmpty(Request.Query["showHeader"]) ? Convert.ToBoolean(Request.Query["showHeader"]) : false,
                    ShowInfo = !string.IsNullOrEmpty(Request.Query["showInfo"]) ? Convert.ToBoolean(Request.Query["showInfo"]) : false,
                    ShowBreadcrump = !string.IsNullOrEmpty(Request.Query["showBreadcrump"]) ? Convert.ToBoolean(Request.Query["showBreadcrump"]) : false,
                    ShowTop = !string.IsNullOrEmpty(Request.Query["showTop"]) ? Convert.ToBoolean(Request.Query["showTop"]) : false,
                    ShowRating = !string.IsNullOrEmpty(Request.Query["showRating"]) ? Convert.ToBoolean(Request.Query["showRating"]) : false,
                    ShowError = !string.IsNullOrEmpty(Request.Query["showError"]) ? Convert.ToBoolean(Request.Query["showError"]) : false,
                    ShowSocial = !string.IsNullOrEmpty(Request.Query["showSocial"]) ? Convert.ToBoolean(Request.Query["showSocial"]) : false,
                    ShowFavourites = !string.IsNullOrEmpty(Request.Query["showFavourites"]) ? Convert.ToBoolean(Request.Query["showFavourites"]) : false,
                    ShowSimilar = !string.IsNullOrEmpty(Request.Query["showSimilar"]) ? Convert.ToBoolean(Request.Query["showSimilar"]) : false,
                    ShowFooter = !string.IsNullOrEmpty(Request.Query["showFooter"]) ? Convert.ToBoolean(Request.Query["showFooter"]) : false,
                    ShowCategories = !string.IsNullOrEmpty(Request.Query["showCategories"]) ? Convert.ToBoolean(Request.Query["showCategories"]) : false,
                    ShowFooterDescription = !string.IsNullOrEmpty(Request.Query["showFooterDescription"]) ? Convert.ToBoolean(Request.Query["showFooterDescription"]) : false,
                    ShowLogo = !string.IsNullOrEmpty(Request.Query["showLogo"]) ? Convert.ToBoolean(Request.Query["showLogo"]) : false,
                    ShowLanguages = !string.IsNullOrEmpty(Request.Query["showLanguages"]) ? Convert.ToBoolean(Request.Query["showLanguages"]) : false,
                    ShowPoweredBy = !string.IsNullOrEmpty(Request.Query["showPoweredBy"]) ? Convert.ToBoolean(Request.Query["showPoweredBy"]) : false,
                    ShowExternalUrl = !string.IsNullOrEmpty(Request.Query["showExternalUrl"]) ? Convert.ToBoolean(Request.Query["showExternalUrl"]) : false,
                    ShowDishImages = !string.IsNullOrEmpty(Request.Query["showDishImages"]) ? Convert.ToBoolean(Request.Query["showDishImages"]) : false,
                    ShowDishImagesPreview = !string.IsNullOrEmpty(Request.Query["showDishImagesPreview"]) ? Convert.ToBoolean(Request.Query["showDishImagesPreview"]) : false,
                    ShowDishImagesIcons = !string.IsNullOrEmpty(Request.Query["showDishImagesIcons"]) ? Convert.ToBoolean(Request.Query["showDishImagesIcons"]) : false,
                    ShowTheardLevelCategories = !string.IsNullOrEmpty(Request.Query["showTheardLevelCategories"]) ? Convert.ToBoolean(Request.Query["showTheardLevelCategories"]) : false,
                    ShowTags = !string.IsNullOrEmpty(Request.Query["showTags"]) ? Convert.ToBoolean(Request.Query["showTags"]) : false,
                    ShowOnlyCategories = !string.IsNullOrEmpty(Request.Query["showOnlyCategories"]) ? Convert.ToBoolean(Request.Query["showOnlyCategories"]) : false,
                    ShowPhone = !string.IsNullOrEmpty(Request.Query["showPhone"]) ? Convert.ToBoolean(Request.Query["showPhone"]) : false,
                    ShowFirstCategoryOnStart = !string.IsNullOrEmpty(Request.Query["showFirstCategoryOnStart"]) ? Convert.ToBoolean(Request.Query["showFirstCategoryOnStart"]) : false,
                    ShowOnlyIframeLogo = !string.IsNullOrEmpty(Request.Query["showOnlyIframeLogo"]) ? Convert.ToBoolean(Request.Query["showOnlyIframeLogo"]) : false
                };
            }

            var languages = _localeSrvc.GetLocales();
            if (!string.IsNullOrEmpty(seo.CustomLanguages))
            {
                IDictionary<string, string> selectedLanguages = JsonConvert.DeserializeObject<IDictionary<string, string>>(seo.CustomLanguages);
                languages = languages.Where(x => selectedLanguages.ContainsKey(x.Id) && Convert.ToBoolean(selectedLanguages[x.Id]));
            }

            iframeSettings.Languages = languages;

            ViewData["iframeSettings"] = iframeSettings;

            return View(data);
        }

        public async Task<IActionResult> Clean(string culture, string id, string city)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();

            var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(id);
            var selectedCity = GetSelectedCityIdAndSetSelectedCityCookie();
            if (restaurant == null)
            {
                var seos = (await _core.Restaurants.FindRestaurantsAutocomplete(id.Trim(), selectedCity)).ToList();
                var seosIds = seos.Select(x => x.ItemId);
                var restaurants = _core.Restaurants.GetCompleteRestaurants().Where(x => seosIds.Contains(x.Id)).Take(4).ToList();

                ViewData["possibleRestaurants"] = restaurants;

                Response.StatusCode = 404;
                return View("NotFoundRestaurant", id);
            };
            var seo = await _core.Restaurants.GetSeoByRestaurantId(restaurant.Id);

            if (!string.IsNullOrEmpty(GetGuid(id))) return RedirectToAction("Restaurant", new { id = seo.Url });

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

            var menu = await GetMenuForRestaurantWithTierThree(restaurant);

            var photos = string.IsNullOrEmpty(restaurantInfo.Photos) ? new List<string>() : JsonConvert.DeserializeObject<List<string>>(restaurantInfo.Photos);
            var (openHours, schedulePerDay) = OpenHoursHelper.Parse(restaurantInfo.OpenHours);
            var menuPhotos = (await _core.Restaurants.GetMenuPhotosForRestaurant(restaurant.Id)).ToList();
            var role = await _core.Restaurants.GetUserRoleForRestaurant(restaurantInfo.RestaurantId,
                User.Identity.Name);

            var data = new RestaurantViewModel
            {
                Restaurant = restaurant,
                RestaurantInfo = restaurantInfo,
                UserRole = role,
                Menu = menu,
                Photos = photos,
                OpenHours = openHours,
                SchedulePerDay = schedulePerDay,
                MenuPhotos = menuPhotos
            };

            IframeSettingsModel iframeSettings = new IframeSettingsModel();

            if (!string.IsNullOrEmpty(seo.CustomIframeSettings) && !string.IsNullOrEmpty(seo.CustomIframeSettings.Trim()))
            {
                iframeSettings = JsonConvert.DeserializeObject<IframeSettingsModel>(seo.CustomIframeSettings);
            }
            else
            {
                iframeSettings = new IframeSettingsModel
                {
                    ShowHeader = !string.IsNullOrEmpty(Request.Query["showHeader"]) ? Convert.ToBoolean(Request.Query["showHeader"]) : false,
                    ShowInfo = !string.IsNullOrEmpty(Request.Query["showInfo"]) ? Convert.ToBoolean(Request.Query["showInfo"]) : false,
                    ShowBreadcrump = !string.IsNullOrEmpty(Request.Query["showBreadcrump"]) ? Convert.ToBoolean(Request.Query["showBreadcrump"]) : false,
                    ShowTop = !string.IsNullOrEmpty(Request.Query["showTop"]) ? Convert.ToBoolean(Request.Query["showTop"]) : false,
                    ShowRating = !string.IsNullOrEmpty(Request.Query["showRating"]) ? Convert.ToBoolean(Request.Query["showRating"]) : false,
                    ShowError = !string.IsNullOrEmpty(Request.Query["showError"]) ? Convert.ToBoolean(Request.Query["showError"]) : false,
                    ShowSocial = !string.IsNullOrEmpty(Request.Query["showSocial"]) ? Convert.ToBoolean(Request.Query["showSocial"]) : false,
                    ShowFavourites = !string.IsNullOrEmpty(Request.Query["showFavourites"]) ? Convert.ToBoolean(Request.Query["showFavourites"]) : false,
                    ShowSimilar = !string.IsNullOrEmpty(Request.Query["showSimilar"]) ? Convert.ToBoolean(Request.Query["showSimilar"]) : false,
                    ShowFooter = !string.IsNullOrEmpty(Request.Query["showFooter"]) ? Convert.ToBoolean(Request.Query["showFooter"]) : false,
                    ShowCategories = !string.IsNullOrEmpty(Request.Query["showCategories"]) ? Convert.ToBoolean(Request.Query["showCategories"]) : false,
                    ShowFooterDescription = !string.IsNullOrEmpty(Request.Query["showFooterDescription"]) ? Convert.ToBoolean(Request.Query["showFooterDescription"]) : false,
                    ShowLogo = !string.IsNullOrEmpty(Request.Query["showLogo"]) ? Convert.ToBoolean(Request.Query["showLogo"]) : false,
                    ShowLanguages = !string.IsNullOrEmpty(Request.Query["showLanguages"]) ? Convert.ToBoolean(Request.Query["showLanguages"]) : false,
                    ShowPoweredBy = !string.IsNullOrEmpty(Request.Query["showPoweredBy"]) ? Convert.ToBoolean(Request.Query["showPoweredBy"]) : false,
                    ShowExternalUrl = !string.IsNullOrEmpty(Request.Query["showExternalUrl"]) ? Convert.ToBoolean(Request.Query["showExternalUrl"]) : false,
                    ShowDishImages = !string.IsNullOrEmpty(Request.Query["showDishImages"]) ? Convert.ToBoolean(Request.Query["showDishImages"]) : false,
                    ShowDishImagesPreview = !string.IsNullOrEmpty(Request.Query["showDishImagesPreview"]) ? Convert.ToBoolean(Request.Query["showDishImagesPreview"]) : false,
                    ShowDishImagesIcons = !string.IsNullOrEmpty(Request.Query["showDishImagesIcons"]) ? Convert.ToBoolean(Request.Query["showDishImagesIcons"]) : false,
                    ShowTheardLevelCategories = !string.IsNullOrEmpty(Request.Query["showTheardLevelCategories"]) ? Convert.ToBoolean(Request.Query["showTheardLevelCategories"]) : false,
                    ShowTags = !string.IsNullOrEmpty(Request.Query["showTags"]) ? Convert.ToBoolean(Request.Query["showTags"]) : false,
                    ShowOnlyCategories = !string.IsNullOrEmpty(Request.Query["showOnlyCategories"]) ? Convert.ToBoolean(Request.Query["showOnlyCategories"]) : false,
                    ShowPhone = !string.IsNullOrEmpty(Request.Query["showPhone"]) ? Convert.ToBoolean(Request.Query["showPhone"]) : false,
                    ShowFirstCategoryOnStart = !string.IsNullOrEmpty(Request.Query["showFirstCategoryOnStart"]) ? Convert.ToBoolean(Request.Query["showFirstCategoryOnStart"]) : false,
                    ShowOnlyIframeLogo = !string.IsNullOrEmpty(Request.Query["showOnlyIframeLogo"]) ? Convert.ToBoolean(Request.Query["showOnlyIframeLogo"]) : false                    
                };
            }

            var languages = _localeSrvc.GetLocales();
            if (!string.IsNullOrEmpty(seo.CustomLanguages))
            {
                IDictionary<string, string> selectedLanguages = JsonConvert.DeserializeObject<IDictionary<string, string>>(seo.CustomLanguages);
                languages = languages.Where(x => selectedLanguages.ContainsKey(x.Id) && Convert.ToBoolean(selectedLanguages[x.Id]));
            }

            iframeSettings.Languages = languages;

            ViewData["iframeSettings"] = iframeSettings;

            return View(data);
        }

        public async Task<IActionResult> Cache(string culture, string id, string city)
        {
            var url = Request.Path.ToString().Replace("/cache", "");

            using (var client = new HttpClient())
            {
                await client.PutAsync($"{_core.Settings.IframeCacheServiceUrl}{url}", new StringContent(""));
            }

            return Ok();
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
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true }
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

        private static void UpdateLatestRestaurants(string restaurantId, HttpContext context)
        {
            var latestRestaurantsString = context.Request.Cookies[".Menus.LatestRestaurants"];
            var latestRestaurants = !string.IsNullOrEmpty(latestRestaurantsString) ? JsonConvert.DeserializeObject<List<string>>(latestRestaurantsString) : new List<string>();

            if (latestRestaurants.FirstOrDefault() == restaurantId) return;

            if (latestRestaurants.Count == 4) latestRestaurants.RemoveAt(3);

            latestRestaurants.Insert(0, restaurantId);

            context.Response.Cookies.Append(".Menus.LatestRestaurants", JsonConvert.SerializeObject(latestRestaurants), new CookieOptions { HttpOnly = true, IsEssential = true, Expires = DateTime.Now.AddYears(1) });
        }

        private async Task<DishesListViewModel> GetMenuForRestaurant(IRestaurant restaurant)
        {
            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(restaurant.Id)).ToList();
            var dishes =
                (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restaurant.Id, User.Identity.Name)).ToList();
            var dishCategories = (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id)))
                .OrderBy(x => x.Index).ToList();

            ViewData["topDishes"] = dishes.Where(x => x.Photos.Count() > 0 && x.RatingFloat > 2).OrderByDescending(x => x.RatingFloat).Take(5).ToList();

            var menu = new DishesListViewModel { Categories = new List<DishesListViewModelCategory>() };

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
                    var iModelCategory = (IMenuCategory)modelCategory;
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

        private async Task<DishesListViewModel> GetMenuForRestaurantWithTierThree(IRestaurant restaurant)
        {
            var categories = (await _core.Restaurants.GetAllMenuCategoriesForRestaurant(restaurant.Id)).ToList();
            var dishes =
                (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(restaurant.Id, User.Identity.Name)).ToList();
            var dishCategories = (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id)))
                .OrderBy(x => x.Index).ToList();

            var tierThreeCategories = categories.Where(cat => cat.IsTopLevelCategory).OrderBy(x => x.Index).ToList();

            ViewData["topDishes"] = dishes.Where(x => x.Photos.Count() > 0 && x.RatingFloat > 2).OrderByDescending(x => x.RatingFloat).Take(5).ToList();

            var menu = new DishesListViewModel { Categories = new List<DishesListViewModelCategory>() };

            if (tierThreeCategories.Count > 0)
            {
                foreach (var ttCategory in tierThreeCategories)
                {
                    var parentModelCategory = new DishesListViewModelCategory
                    {
                        Active = ttCategory.Active,
                        Id = ttCategory.Id,
                        Index = ttCategory.Index,
                        RestaurantId = ttCategory.RestaurantId,
                        IsTopLevelCategory = true
                    };

                    var iParentModelCategory = (IMenuCategory)parentModelCategory;
                    iParentModelCategory.Name = ttCategory.Name;
                    iParentModelCategory.Description = ttCategory.Description;
                    parentModelCategory.Dishes = new List<IDishWithSeoAndRatings>();

                    menu.Categories.Add(parentModelCategory);

                    var childCategories = ttCategory.ChildCategories.OrderBy(x => x.Index).ToList();
                    foreach (var childCategory in childCategories)
                    {
                        DishesListViewModelCategory modelCategory = new DishesListViewModelCategory
                        {
                            Active = childCategory.Active,
                            Id = childCategory.Id,
                            Index = childCategory.Index,
                            RestaurantId = childCategory.RestaurantId
                        };
                        var iModelCategory = (IMenuCategory)modelCategory;
                        iModelCategory.Name = childCategory.Name;
                        iModelCategory.CurrentParentCategoryId = ttCategory.Id;
                        menu.Categories.Add(modelCategory);

                        var dishList = modelCategory.Dishes;
                        if (dishList == null)
                        {
                            dishList = new List<IDishWithSeoAndRatings>();
                            modelCategory.Dishes = dishList;
                        }
                        var dishIds = dishCategories.Where(x => x.MenuCategoryId == modelCategory.Id).OrderBy(x => x.Index).Select(x => x.DishId).ToList();
                        dishList.AddRange(dishes.Where(x => dishIds.Contains(x.Id)).OrderBy(y => dishIds.IndexOf(y.Id)));
                    }
                }
            } else
            {
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
                        var iModelCategory = (IMenuCategory)modelCategory;
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

                    menu.Categories = menu.Categories.OrderBy(x => x.Index).ToList();
                }
            }

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
    }
}

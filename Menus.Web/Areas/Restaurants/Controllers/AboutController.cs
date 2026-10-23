// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util.Store;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Helpers;
using Menus.Web.Models;
using Menus.Web.Models.ViewModels;
using Menus.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Menus.Web.Areas.Restaurants.Controllers
{
    [Area("Restaurants")]
    [Authorize(Roles = "admin, manager, restaurant, restaurant-manager")]
    public class AboutController : Controller
    {
        private readonly Core _core;
        private readonly IDbContext _db;
        private readonly UserService _usrService;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly EmailService _email;
        private readonly UrlGenerateService _urls;
        private readonly IConfiguration _configuration;

        public AboutController(
            Core core, 
            UserService userService, 
            IDbContext db, 
            IHostingEnvironment hostingEnvironment, 
            EmailService email, 
            UrlGenerateService urls,
            IConfiguration configuration
            )
        {
            _core = core;
            _usrService = userService;
            _db = db;
            _hostingEnvironment = hostingEnvironment;
            _email = email;
            _urls = urls;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            if(User.IsInRole("restaurant") && (selectedRestaurant.Status == RestaurantStatus.Pending || selectedRestaurant.Status == RestaurantStatus.Blocked))
            {
                return RedirectToAction("Index", "Home");
            }

            var ownerId = await _core.Restaurants.GetRestaurantOwnerId(selectedRestaurant.Id);

            var restaurantInfo = await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id);
            if (restaurantInfo == null)
            {
                restaurantInfo = new RestaurantInfo();
                restaurantInfo.RestaurantId = selectedRestaurant.Id;
                restaurantInfo.ShortInfo = new LocalizableString();
                restaurantInfo.FullInfo = new LocalizableString();
                restaurantInfo.Cuisine = new LocalizableString();
            }

            ViewData["owner"] = await _core.Users.GetUserById(ownerId ?? "1a950ec5-7fd3-4d82-adca-d27fda6025b5");
            ViewData["menuUrl"] = await HttpContext.RequestServices.GetRequiredService<RestaurantMenuLinkService>().Get(selectedRestaurant.Id);

            return View(restaurantInfo);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        [Produces("application/json")]
        public async Task<IActionResult> GetPossibleOwners(string query)
        {
            IEnumerable<IUser> users;
            if (string.IsNullOrEmpty(query))
                users = (await _core.Users.GetAllUsers()).Where(x =>
                    x.Role == UserRole.Admin || x.Role == UserRole.Manager || x.Role == UserRole.Restaurant);
            else
                users = (await _core.Users.GetAllUsers())
                    .Where(x => ((x.FirstName ?? "").Contains(query) || (x.LastName ?? "").Contains(query) || (x.Email ?? "").Contains(query) || x.Id.Contains(query))
                                && (x.Role == UserRole.Admin || x.Role == UserRole.Manager || x.Role == UserRole.Restaurant))
                    .Take(10);

            return Ok(users.ToList().Select(x => new { value = $"{x.FirstName ?? ""} {x.LastName ?? ""} | {x.Email.ToLowerInvariant()} | {x.Id}", id = x.Id, tokens = new List<string>{ x.FirstName, x.LastName, x.Email } }));
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> ChangeOwner(string restaurantId, string userString)
        {
            var userId = GetGuid(userString);

            if (string.IsNullOrEmpty(userId)) return Ok();

            var user = await _core.Users.GetUserById(userId);
            if (user.Role != UserRole.Admin && user.Role != UserRole.Manager &&
                user.Role != UserRole.Restaurant) return Ok();

            var restaurantOwner = await _core.Restaurants.GetCurrentOwnerForRestaurant(restaurantId);
            if(restaurantOwner == null)
            {
                restaurantOwner = new RestaurantRole {
                    Id = Guid.NewGuid().ToString(),
                    RestaurantId = restaurantId,
                    Role = RestaurantUserRole.Owner,
                    UserId = userId
                };
                await _core.Restaurants.CreateRestaurantRole(restaurantOwner);
            }
            else
            {
                restaurantOwner.UserId = userId;
                await _core.Restaurants.UpdateRestaurantRole(restaurantOwner);
            }

            return Ok();
        }


        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateSpreadSheetWithRestaurantData(string spreadsheetId)
        {

            //return Ok(new { price = price.ToString("F2") });
            try
            {
                var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

                if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                    !User.IsInRole("admin") &&
                    !User.IsInRole("manager"))
                {
                    return RedirectToAction("Index", "Home");
                }

                var restaurantInfo = await _core.Restaurants.GetRestaurantById(selectedRestaurant.Id);
                var seo = await _core.Restaurants.GetSeoByRestaurantId(selectedRestaurant.Id);

                var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
                var dishCategories = (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id))).ToList();
                var dishes = (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(selectedRestaurant.Id, "")).ToList();
                var model = new DishesListViewModel { Categories = new List<DishesListViewModelCategory>() };
                foreach (var dishCategory in dishCategories)
                {
                    var modelCategory = model.Categories.FirstOrDefault(x => x.Id == dishCategory.MenuCategoryId);
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

                        model.Categories.Add(modelCategory);
                    }

                    var dishList = modelCategory.Dishes;
                    if (dishList == null)
                    {
                        dishList = new List<IDishWithSeoAndRatings>();
                        modelCategory.Dishes = dishList;
                    }

                    dishList.Add(dishes.First(x => x.Id == dishCategory.DishId));
                }

                GoogleSpreadSheetHelper gHelper = new GoogleSpreadSheetHelper();
                string sheetId = !string.IsNullOrEmpty(spreadsheetId) ? spreadsheetId : _configuration["Google:DefaultDocumentId"];
                gHelper.UpdateSpreadSheetWithRestaurantData(sheetId, restaurantInfo, seo, model);

                return Ok(new { status = "Success", messages = new List<string>() { string.Format("Загружено: {0} блюд", dishes.Count) } });
            } catch(Exception ex)
            {
                return Ok(new { status = "Error", messages = new List<string>() { ex.Message } });
            }
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateFromSpreadSheetWithRestaurantData(string spreadsheetId)
        {
            List<string> dishLog = new List<string>();
            try
            {
                string ruLocal = "ru";
                string ukLocal = "uk";

                List<IDishVariation> existingPrices = new List<IDishVariation>();
                List<DishVariation> newPrices = new List<DishVariation>();

                var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

                if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                    !User.IsInRole("admin") &&
                    !User.IsInRole("manager"))
                {
                    return RedirectToAction("Index", "Home");
                }

                GoogleSpreadSheetHelper gHelper = new GoogleSpreadSheetHelper();
                string sheetId = !string.IsNullOrEmpty(spreadsheetId) ? spreadsheetId : _configuration["Google:DefaultDocumentId"];
                IList<IList<Object>> values = gHelper.UpdateFromSpreadSheetWithRestaurantData(sheetId);
                //Removing header
                values.RemoveAt(0);
                var dishes = (await _core.Dishes.GetDishesForRestaurant(selectedRestaurant.Id)).ToList();
                var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
                var currentCategoryName = string.Empty;

                foreach (IList<Object> dishValues in values)
                {
                    existingPrices = new List<IDishVariation>();
                    newPrices = new List<DishVariation>();
                    if (dishValues.Count == 3)
                    {
                        currentCategoryName = dishValues[2].ToString();
                    }


                    if (dishValues.Count == 15 || dishValues.Count == 14)
                    {

                        var currentCategory = categories.FirstOrDefault(x => x.Name.Get(ruLocal) == currentCategoryName);
                        string priceString = !string.IsNullOrEmpty(dishValues[13].ToString()) ? dishValues[13].ToString() : "0";

                        decimal price = Convert.ToDecimal(priceString);
                        string weightType = dishValues[12].ToString();
                        string weight = dishValues[11].ToString();

                        string ingredientsRu = dishValues[10].ToString();
                        string ingredientsUk = dishValues[9].ToString();

                        string descriptionRu = dishValues[8].ToString();
                        string descriptionUk = dishValues[7].ToString();

                        string shortDescriptionRu = dishValues[6].ToString();
                        string shortDescriptionUk = dishValues[5].ToString();

                        string nameRu = dishValues[4].ToString();
                        string nameUk = dishValues[3].ToString();

                        if (dishValues.Count == 15)
                        {
                            string dishId = dishValues[14].ToString();
                            var originalDish = dishes.FirstOrDefault(x => x.Id == dishId);

                            string originalDishWeight = originalDish.Weight;
                            if (originalDish.Weight.Split(" ", StringSplitOptions.RemoveEmptyEntries).Length > 1)
                            {
                                originalDishWeight = originalDish.Weight.Split(" ", StringSplitOptions.RemoveEmptyEntries)[0];
                            }

                            if (originalDish != null)
                            {
                                bool hasDiffrences =
                                    (originalDish.Name.Get(ruLocal) != nameRu) ||
                                    (originalDish.Name.Get(ukLocal) != nameUk) ||
                                    (originalDish.ShortDescription.Get(nameRu) != shortDescriptionRu) ||
                                    (originalDish.ShortDescription.Get(ukLocal) != shortDescriptionUk) ||
                                    (originalDish.Description.Get(nameRu) != descriptionRu) ||
                                    (originalDish.Description.Get(ukLocal) != descriptionUk) ||
                                    (originalDish.Ingredients.Get(nameRu) != ingredientsRu) ||
                                    (originalDish.Ingredients.Get(ukLocal) != ingredientsUk) ||
                                    (originalDishWeight != weight) ||
                                    (originalDish.Price != price);

                                if (hasDiffrences)
                                {
                                    List<ILocalizableStringValue> newNames = new List<ILocalizableStringValue>();
                                    LocalizableStringValue newRuName = new LocalizableStringValue();
                                    newRuName.Locale = ruLocal;
                                    newRuName.Value = nameRu;
                                    LocalizableStringValue newUkName = new LocalizableStringValue();
                                    newUkName.Locale = ukLocal;
                                    newUkName.Value = nameUk;
                                    newNames.Add(newRuName);
                                    newNames.Add(newUkName);

                                    List<ILocalizableStringValue> newShortDescriptions = new List<ILocalizableStringValue>();
                                    LocalizableStringValue newRuShortDescription = new LocalizableStringValue();
                                    newRuShortDescription.Locale = ruLocal;
                                    newRuShortDescription.Value = shortDescriptionRu;
                                    LocalizableStringValue newUkShortDescription = new LocalizableStringValue();
                                    newUkShortDescription.Locale = ukLocal;
                                    newUkShortDescription.Value = shortDescriptionUk;
                                    newShortDescriptions.Add(newRuShortDescription);
                                    newShortDescriptions.Add(newUkShortDescription);

                                    List<ILocalizableStringValue> newDescriptions = new List<ILocalizableStringValue>();
                                    LocalizableStringValue newRuDescription = new LocalizableStringValue();
                                    newRuDescription.Locale = ruLocal;
                                    newRuDescription.Value = descriptionRu;
                                    LocalizableStringValue newUkDescription = new LocalizableStringValue();
                                    newUkDescription.Locale = ukLocal;
                                    newUkDescription.Value = descriptionUk;
                                    newDescriptions.Add(newRuDescription);
                                    newDescriptions.Add(newUkDescription);

                                    List<ILocalizableStringValue> newIngridients = new List<ILocalizableStringValue>();
                                    LocalizableStringValue newRuIngridient = new LocalizableStringValue();
                                    newRuIngridient.Locale = ruLocal;
                                    newRuIngridient.Value = ingredientsRu;
                                    LocalizableStringValue newUkIngridient = new LocalizableStringValue();
                                    newUkIngridient.Locale = ukLocal;
                                    newUkIngridient.Value = ingredientsUk;
                                    newIngridients.Add(newRuIngridient);
                                    newIngridients.Add(newUkIngridient);


                                    var description = new LocalizableString() as ILocalizableString;
                                    description.Replace(newDescriptions.Select(x => x as ILocalizableStringValue));
                                    originalDish.Description = description;
                                    var ingredients = new LocalizableString() as ILocalizableString;
                                    ingredients.Replace(newIngridients.Select(x => x as ILocalizableStringValue));
                                    originalDish.Ingredients = ingredients;
                                    var name = new LocalizableString() as ILocalizableString;
                                    name.Replace(newNames.Select(x => x as ILocalizableStringValue));
                                    originalDish.Name = name;
                                    var shortDescription = new LocalizableString() as ILocalizableString;
                                    shortDescription.Replace(newShortDescriptions.Select(x => x as ILocalizableStringValue));
                                    originalDish.ShortDescription = shortDescription;



                                    bool hasPriceDifferences =
                                        (originalDish.Weight != weight) ||
                                        (originalDish.Price != price);

                                    if (hasPriceDifferences)
                                    {
                                        int weightResult = 0;
                                        var newPrice = new DishVariation();
                                        bool tryResult = int.TryParse(weight, out weightResult);

                                        var existingPrice = (await _core.Dishes.GetDishVariations(dishId)).OrderBy(x => x.Price).ToList().FirstOrDefault();
                                        if (existingPrice != null)
                                        {
                                            existingPrices.Add(existingPrice);
                                            newPrice = new DishVariation()
                                            {
                                                Id = Guid.NewGuid().ToString(),
                                                DishId = existingPrice.DishId,
                                                Price = price,
                                                Weight = weightResult,
                                                Type = !string.IsNullOrEmpty(weightType) ? weightType : "g"
                                            };
                                            newPrices.Add(newPrice);
                                        }
                                        else
                                        {
                                            newPrice = new DishVariation()
                                            {
                                                Id = Guid.NewGuid().ToString(),
                                                DishId = dishId,
                                                Price = price,
                                                Weight = weightResult,
                                                Type = !string.IsNullOrEmpty(weightType) ? weightType : "g"
                                            };
                                            newPrices.Add(newPrice);
                                        }

                                        originalDish.Price = newPrice.Price;
                                        originalDish.Weight = newPrice.Weight.ToString();
                                        originalDish.WeightType = newPrice.Type;
                                    }

                                    await _core.Dishes.Update(originalDish);
                                    if (hasPriceDifferences)
                                    {
                                        await _core.Dishes.CreateDishVariations(newPrices);
                                        await _core.Dishes.DeleteDishVariations(existingPrices);
                                    }
                                    dishLog.Add(string.Format("Блюдо обновлено: {0}", originalDish.Name.Get(ruLocal)));
                                }
                            }
                        }
                        if (dishValues.Count == 14 && !string.IsNullOrEmpty(nameUk))
                        {
                            Dish dish = new Dish();
                            dish.Id = Guid.NewGuid().ToString();
                            dish.PhotoLink = "[]";
                            dish.CreateDate = DateTime.Now;
                            dish.RestaurantId = selectedRestaurant.Id;

                            List<ILocalizableStringValue> newNames = new List<ILocalizableStringValue>();
                            LocalizableStringValue newRuName = new LocalizableStringValue();
                            newRuName.Locale = ruLocal;
                            newRuName.Value = nameRu;
                            LocalizableStringValue newUkName = new LocalizableStringValue();
                            newUkName.Locale = ukLocal;
                            newUkName.Value = nameUk;
                            newNames.Add(newRuName);
                            newNames.Add(newUkName);

                            List<ILocalizableStringValue> newShortDescriptions = new List<ILocalizableStringValue>();
                            LocalizableStringValue newRuShortDescription = new LocalizableStringValue();
                            newRuShortDescription.Locale = ruLocal;
                            newRuShortDescription.Value = shortDescriptionRu;
                            LocalizableStringValue newUkShortDescription = new LocalizableStringValue();
                            newUkShortDescription.Locale = ukLocal;
                            newUkShortDescription.Value = shortDescriptionUk;
                            newShortDescriptions.Add(newRuShortDescription);
                            newShortDescriptions.Add(newUkShortDescription);

                            List<ILocalizableStringValue> newDescriptions = new List<ILocalizableStringValue>();
                            LocalizableStringValue newRuDescription = new LocalizableStringValue();
                            newRuDescription.Locale = ruLocal;
                            newRuDescription.Value = descriptionRu;
                            LocalizableStringValue newUkDescription = new LocalizableStringValue();
                            newUkDescription.Locale = ukLocal;
                            newUkDescription.Value = descriptionUk;
                            newDescriptions.Add(newRuDescription);
                            newDescriptions.Add(newUkDescription);

                            List<ILocalizableStringValue> newIngridients = new List<ILocalizableStringValue>();
                            LocalizableStringValue newRuIngridient = new LocalizableStringValue();
                            newRuIngridient.Locale = ruLocal;
                            newRuIngridient.Value = ingredientsRu;
                            LocalizableStringValue newUkIngridient = new LocalizableStringValue();
                            newUkIngridient.Locale = ukLocal;
                            newUkIngridient.Value = ingredientsUk;
                            newIngridients.Add(newRuIngridient);
                            newIngridients.Add(newUkIngridient);


                            var description = new LocalizableString() as ILocalizableString;
                            description.Replace(newDescriptions.Select(x => x as ILocalizableStringValue));
                            ((IDish)(dish)).Description = description;
                            var ingredients = new LocalizableString() as ILocalizableString;
                            ingredients.Replace(newIngridients.Select(x => x as ILocalizableStringValue));
                            ((IDish)(dish)).Ingredients = ingredients;
                            var name = new LocalizableString() as ILocalizableString;
                            name.Replace(newNames.Select(x => x as ILocalizableStringValue));
                            ((IDish)(dish)).Name = name;
                            var shortDescription = new LocalizableString() as ILocalizableString;
                            shortDescription.Replace(newShortDescriptions.Select(x => x as ILocalizableStringValue));
                            ((IDish)(dish)).ShortDescription = shortDescription;

                            int weightResult = 0;
                            bool tryResult = int.TryParse(weight, out weightResult);
                            var newPrice = new DishVariation()
                            {
                                Id = Guid.NewGuid().ToString(),
                                DishId = dish.Id,
                                Price = price,
                                Weight = weightResult,
                                Type = !string.IsNullOrEmpty(weightType) ? weightType : "g"
                            };
                            newPrices.Add(newPrice);

                            var dishCategory = new DishCategory
                            {
                                Id = Guid.NewGuid().ToString(),
                                DishId = dish.Id,
                                MenuCategoryId = currentCategory.Id
                            };

                            dish.Price = newPrice.Price;
                            dish.Weight = newPrice.Weight.ToString();
                            dish.WeightType = newPrice.Type;
                            dish.IsPriceMultiple = false;

                            var seo = new DishSeo() as IDishSeo;
                            seo.Id = Guid.NewGuid().ToString();
                            seo.ItemId = dish.Id;
                            seo.RestaurantId = selectedRestaurant.Id;
                            seo.Url = await _urls.GetNewUrlForDish(seo, dish);
                            seo.SearchTerms = ((IDish)dish).Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(((IDish)dish).Name.Get());

                            await _core.Dishes.Create(dish);
                            await _core.Dishes.CreateDishVariations(newPrices);
                            await _core.Dishes.AddDishToCategory(dishCategory);
                            await _core.Dishes.CreateSeo(seo);
                            dishLog.Add(string.Format("Блюдо добавлено: {0}", ((IDish)dish).Name.Get(ruLocal)));

                        }

                    }

                }

                return Ok(new { status = "Success", messages = dishLog });
            }
            catch (Exception ex)
            {
                dishLog.Add(string.Format("Ошибка: {0}", ex.Message));
                return Ok(new { status = "Error", messages = dishLog });
            }
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SendActivationEmail()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var restaurantOwner = await _core.Restaurants.GetCurrentOwnerForRestaurant(selectedRestaurant.Id);
            var user = await _core.Users.GetUserById(restaurantOwner.UserId);

            var mailText = System.IO.File.ReadAllText(_hostingEnvironment.ContentRootPath + "/EmailTemplates/confirmaccess.html");
            var confirmationUrl = $"https://{Request.Host}";
            mailText = mailText.Replace("{{tipMyMenuUrl}}", confirmationUrl);
            mailText = mailText.Replace("{{firstName}}", user.FirstName);
            mailText = mailText.Replace("{{lastName}}", user.LastName);
            mailText = mailText.Replace("{{restaurantName}}", selectedRestaurant.Name);

            _email.SendEmail(user.Email, "Access Confirmation", mailText);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(UpdateRestaurantinfoModel model)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            if (model.ShortInfo == null) model.ShortInfo = new List<LocalizableStringValue>();
            if (model.FullInfo == null) model.FullInfo = new List<LocalizableStringValue>();
            if (model.Cuisine == null) model.Cuisine = new List<LocalizableStringValue>();
            if (model.DisplayAddress == null) model.DisplayAddress = new List<LocalizableStringValue>();

            var info = model.GetRestaurantInfo();
            var existingInfo = await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id);

            if (existingInfo == null)
            {
                existingInfo = new RestaurantInfo();
                existingInfo.Id = Guid.NewGuid().ToString();
                existingInfo.RestaurantId = selectedRestaurant.Id;
                model.Id = existingInfo.Id;
            }
            if (existingInfo.Id != model.Id) {
                return RedirectToAction("Index", "Home");
            }

            var iInfo = (IRestaurantInfo) info;
            var shortInfo = new LocalizableString() as ILocalizableString;
            shortInfo.Replace(model.ShortInfo.Select(x => x as ILocalizableStringValue));
            iInfo.ShortInfo = shortInfo;
            var fullInfo = new LocalizableString() as ILocalizableString;
            fullInfo.Replace(model.FullInfo.Select(x => x as ILocalizableStringValue));
            iInfo.FullInfo = fullInfo;
            var cuisine = new LocalizableString() as ILocalizableString;
            cuisine.Replace(model.Cuisine.Select(x => x as ILocalizableStringValue));
            iInfo.Cuisine = cuisine;
            var displayAddress = new LocalizableString() as ILocalizableString;
            displayAddress.Replace(model.DisplayAddress.Select(x => x as ILocalizableStringValue));
            iInfo.DisplayAddress = displayAddress;

            var newImages = string.IsNullOrEmpty(model.Photos) ? new List<string>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(model.Photos);
            var oldImages = string.IsNullOrEmpty(existingInfo.Photos) ? new List<string>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(existingInfo.Photos);
            var photosToRemove = oldImages.Except(newImages);

            //foreach (var oldPhoto in photosToRemove)
            //{
            //    var id = GetGuid(oldPhoto);
            //    await _core.Files.Delete(id);
            //}

            existingInfo.Contacts = model.Contacts;
            existingInfo.DisplayAddress = iInfo.DisplayAddress;
            existingInfo.Cuisine = iInfo.Cuisine;
            existingInfo.FullInfo = iInfo.FullInfo;
            existingInfo.GpsLatitude = model.GpsLatitude;
            existingInfo.GpsLongitude = model.GpsLongitude;
            existingInfo.OpenHours = model.OpenHours;
            existingInfo.Photos = model.Photos;
            existingInfo.Logo = model.Logo;
            existingInfo.ShortInfo = iInfo.ShortInfo;
            existingInfo.Website = model.Website;
            existingInfo.Currency = model.Currency;
            existingInfo.UserAllowedAddMenu = model.UserAllowedAddMenu;
            existingInfo.UserAllowedAddDishes = model.UserAllowedAddDishes;
            existingInfo.UserAllowedOpenMenu = model.UserAllowedOpenMenu;

            if (User.IsInRole("admin") || User.IsInRole("manager"))
            {
                existingInfo.IsFeatured = model.IsFeatured;
                existingInfo.ShowOnIndex = model.ShowOnIndex;
            }

            await _core.Restaurants.UpdateRestaurantInfo(existingInfo);
            await HttpContext.RequestServices.GetRequiredService<RestaurantMenuLinkService>().Set(selectedRestaurant.Id, model.MenuUrl);

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UploadRestaurantLogo(string base64, string id)
        {

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

            return Ok(new { id = file.Id, name = file.Id + Helpers.ImageHelper.GetExtension(base64), approved = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]   
        public async Task<IActionResult> UploadBase64Photo(string base64)
        {
            var mime = Regex.Match(base64, "data:(.+?);").Groups[1].Value;
            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);

            var file = new DbFile {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = mime,
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            return Ok(new { id = file.Id, name = file.Id + (mime == "image/png" ? ".png" : ".jpg") });
        }

        [HttpGet]
        public async Task<IActionResult> Seo()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var seo = await _core.Restaurants.GetSeoByRestaurantId(selectedRestaurant.Id);
            if (seo == null) seo = new RestaurantSeo() { ItemId = selectedRestaurant.Id };
            return View(seo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Seo(RestaurantSeo model)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            if (model.ItemId != selectedRestaurant.Id)
            {
                return RedirectToAction("Index", "Home");
            }

            var seo = await _core.Restaurants.GetSeoByRestaurantId(selectedRestaurant.Id);
            var createSeo = false;
            if (seo != null)
            {
                seo.Url = model.Url;
                seo.Description = (model as ISeo).Description;
                seo.Header = (model as ISeo).Header;
                seo.Title = (model as ISeo).Title;
                seo.Keywords = model.Keywords;
                seo.SeoText = model.SeoText;
                seo.SearchTerms = model.SearchTerms;
                seo.CustomCss = model.CustomCss;
                seo.CustomIframeSettings = model.CustomIframeSettings;
                seo.CustomScript = model.CustomScript;
                seo.CustomLanguages = model.CustomLanguages;
                (seo as RestaurantSeo).Tags = model.Tags;
            }
            else
            {
                seo = model;
                seo.Id = Guid.NewGuid().ToString();
                createSeo = true;
            }

            if (createSeo) await _core.Restaurants.CreateSeo(seo);
            else
            {
                try
                {
                    await _core.Restaurants.UpdateSeo(seo);
                }
                catch(Exception)
                {
                    ViewData["urlExist"] = true;
                    return View(seo);
                }
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> RegisrationInfo()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            return View(selectedRestaurant);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisrationInfo(RegisterRestaurantModel model)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            if (string.IsNullOrEmpty(model.Name) ||
               string.IsNullOrEmpty(model.Phone) ||
               string.IsNullOrEmpty(model.Address) ||
               string.IsNullOrEmpty(model.City)/* ||
               string.IsNullOrEmpty(model.Email)*/)
            {
                return BadRequest("Empty fields");
            }

            if (!RegisterFilesAreSupportedTypes(model)) return BadRequest("Unsupported files type");

            if (model.LicenseFile != null) {
                var oldId = GetGuid(selectedRestaurant.LicenseLink);
                await _core.Files.Delete(oldId);

                var licenseFileStream = model.LicenseFile.OpenReadStream();
                var licenseFileMemoryStream = new MemoryStream();
                licenseFileStream.CopyTo(licenseFileMemoryStream);
                var licenseFile = new DbFile
                {
                    Id = Guid.NewGuid().ToString(),
                    Mime = model.LicenseFile.ContentType,
                    Size = Convert.ToInt32(model.LicenseFile.Length),
                    Data = licenseFileMemoryStream.ToArray()
                };
                licenseFileMemoryStream.Close();
                licenseFileStream.Close();

                await _core.Files.Create(licenseFile);
                selectedRestaurant.LicenseLink = licenseFile.Id + GetFileExtension(licenseFile.Mime);
            }

            if (model.RegistrationFile != null)
            {
                var oldId = GetGuid(selectedRestaurant.RegistrationLink);
                await _core.Files.Delete(oldId);

                var registrationFileStream = model.RegistrationFile.OpenReadStream();
                var registrationFileMemoryStream = new MemoryStream();
                registrationFileStream.CopyTo(registrationFileMemoryStream);
                var registrationFile = new DbFile
                {
                    Id = Guid.NewGuid().ToString(),
                    Mime = model.RegistrationFile.ContentType,
                    Size = Convert.ToInt32(model.RegistrationFile.Length),
                    Data = registrationFileMemoryStream.ToArray()
                };
                registrationFileMemoryStream.Close();
                registrationFileStream.Close();

                await _core.Files.Create(registrationFile);
                selectedRestaurant.RegistrationLink = registrationFile.Id + GetFileExtension(registrationFile.Mime);
            }

            selectedRestaurant.Status = User.IsInRole("admin") || User.IsInRole("manager") ? model.Status : RestaurantStatus.Pending;
            selectedRestaurant.Name = model.Name;
            selectedRestaurant.Phone = model.Phone;
            selectedRestaurant.Address = model.Address;
            selectedRestaurant.City = model.City;
            selectedRestaurant.Email = model.Email;
            selectedRestaurant.IsModeratable = model.IsModeratable;
            selectedRestaurant.Moderated = model.Moderated;
            await _core.Restaurants.Update(selectedRestaurant);

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> PromoList()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var restaurantPromos = await _db.Promos
                .Where(x => x.RestaurantId == selectedRestaurant.Id)
                .OrderBy(x => x.StartDate)
                .ThenBy(x => x.EndDate)
                .ToListAsync();

            return View(restaurantPromos);
        }

        [HttpGet]
        public async Task<IActionResult> CreatePromo()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            return View("PromoSingle", new Promo());
        }

        [HttpGet]
        public async Task<IActionResult> EditPromo(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var promo = await _db.Promos.FirstOrDefaultAsync(x =>
                x.Id == id && x.RestaurantId == selectedRestaurant.Id);

            if (promo == null) return RedirectToAction("Index", "Home");

            return View("PromoSingle", promo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePromo(Promo model)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            if (string.IsNullOrEmpty(model.Id))
            {
                model.Id = Guid.NewGuid().ToString();
                await _db.Promos.Add(model);
            }
            else await _db.Promos.Update(model);

            return RedirectToAction("PromoList");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePromo(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var promo = await _db.Promos.FirstOrDefaultAsync(x =>
                x.Id == id && x.RestaurantId == selectedRestaurant.Id);

            if (promo == null) return BadRequest(new { error = "Promo not found" });

            await _db.Promos.Remove(promo);

            return Ok();
        }

        private static bool RegisterFilesAreSupportedTypes(RegisterRestaurantModel model)
        {
            return (model.LicenseFile == null || model.LicenseFile.ContentType == "image/png" || model.LicenseFile.ContentType == "image/jpeg" || model.LicenseFile.ContentType == "application/pdf") &&
                (model.RegistrationFile == null || model.RegistrationFile.ContentType == "image/png" || model.RegistrationFile.ContentType == "image/jpeg" || model.RegistrationFile.ContentType == "application/pdf");
        }

        private static string GetFileExtension(string mime)
        {
            switch (mime)
            {
                case "image/png":
                    return ".png";
                case "image/jpeg":
                    return ".jpg";
                case "application/pdf":
                    return ".pdf";
                default:
                    return ".png";
            }
        }

        private static string GetGuid(string input)
        {
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
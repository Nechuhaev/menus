// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Menus.Web.Models.ViewModels;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Menus.Web.Areas.Restaurants.Controllers
{
    [Area("Restaurants")]
    [Authorize(Roles = "admin, manager, restaurant, restaurant-manager")]
    public class ManageController : Controller
    {
        private readonly Core _core;
        private readonly UrlGenerateService _urls;
        private readonly UserService _usrService;
        private readonly IDbContext _db;

        public ManageController(Core core, UserService userService, UrlGenerateService urls, IDbContext db)
        {
            _core = core;
            _usrService = userService;
            _urls = urls;
            _db = db;
        }


        public async Task<IActionResult> Index()
        {
            Response.Cookies.Delete(".Menus.SelectedRestaurant");
            if (User.IsInRole("admin") || User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Restaurants", new { area = "Admin" });
            }
                

            var result = await _core.Restaurants.GetRestaurantsForUser(User.Identity.Name);
            return View(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToogleLanguagePrefill()
        {
            var disableLanguagePrefill = Request.Cookies[".Menus.DisableLanguagePrefill"];

            if (string.IsNullOrEmpty(disableLanguagePrefill))
            {
                Response.Cookies.Append(
                    ".Menus.DisableLanguagePrefill",
                    "true",
                    new CookieOptions {Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true}
                );
            }
            else
            {
                Response.Cookies.Delete(".Menus.DisableLanguagePrefill");
            }

            return Ok();
        }

        public async Task<IActionResult> SelectRestaurant(string id, string redirect)
        {
            var restaurant = await _core.Restaurants.GetSelectedRestaurantForUser(id, User.Identity.Name);
            if (restaurant == null) return RedirectToAction("Index");

            Response.Cookies.Append(".Menus.SelectedRestaurant", id,
                new Microsoft.AspNetCore.Http.CookieOptions {HttpOnly = true, IsEssential = true});

            return !string.IsNullOrEmpty(redirect) ? RedirectToAction(redirect) : RedirectToAction("Index", "About");
        }

        public async Task<IActionResult> Menu()
        {
            if (User.IsInRole("restaurant") || User.IsInRole("restaurant-manager"))
            {
                ViewData["restaurants"] = await _core.Restaurants.GetRestaurantsForUser(User.Identity.Name);
            }

            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            var menuCategories = await _core.Restaurants.GetAllMenuCategoriesForRestaurant(selectedRestaurant.Id);

            if (menuCategories.Any())
            {
                return View(menuCategories);
            }
            else return View("MenuEmpty");
        }

        [HttpGet]
        public async Task<IActionResult> CreateDish()
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> CreateDishIframe(string frameId)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);
            ViewData["frameId"] = frameId;
            ViewData["variations"] = "[]";
            ViewData["selectedMenuCategories"] = new List<string>();
            ViewData["selectedMenuCategoriesJson"] = "[]";
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;
            ViewData["mode"] = "new";

            var model = new Dish() as IDish;
            model.Description = new LocalizableString();
            model.Ingredients = new LocalizableString();
            model.Name = new LocalizableString();
            model.ShortDescription = new LocalizableString();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDishIframe(CreateDishModel model, string frameId, string mode)
        {
            var actionResult = await CreateDishUnified(model);

            string newDishId = null;

            if (mode == "copy")
            {
                //newDishId = await CloneDish(model.Id);
                newDishId = model.Id;
            }

            return actionResult ??
                   RedirectToAction("EditDishIframe", new {id = model.Id, frameId, mode, newId = newDishId});
        }

        private async Task<string> CloneDish(string dishId)
        {
            var newDish = (await _core.Dishes.GetDishById(dishId)).Clone() as IDish;
            var prices = (await _core.Dishes.GetDishVariations(dishId)).ToList();
            newDish.Id = Guid.NewGuid().ToString();

            var newDishPrices = prices.Select(x =>
            {
                var n = (IDishVariation) x.Clone();
                n.Id = Guid.NewGuid().ToString();
                n.DishId = newDish.Id;
                return n;
            });

            newDish.IsPriceMultiple = prices.Count > 1;
            newDish.Weight = prices.FirstOrDefault()?.Weight.ToString() ?? "0";
            newDish.WeightType = prices.FirstOrDefault()?.Type ?? "g";
            newDish.Price = prices.FirstOrDefault()?.Price ?? 0;
            await _core.Dishes.Create(newDish);
            await _core.Dishes.CreateDishVariations(newDishPrices);

            var dishCategories = await _core.Dishes.GetDishCategories(dishId);

            foreach (var category in dishCategories)
            {
                category.Id = Guid.NewGuid().ToString();
                category.Index = (await _core.Dishes.GetDishesForCategory(category.MenuCategoryId)).Count() + 1;
                category.DishId = newDish.Id;

                await _core.Dishes.AddDishToCategory(category);
            }

            var seo = new DishSeo() as IDishSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.ItemId = newDish.Id;
            seo.RestaurantId = newDish.RestaurantId;
            seo.Url = await _urls.GetNewUrlForDish(seo, newDish);
            seo.SearchTerms = newDish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(newDish.Name.Get());
            await _core.Dishes.CreateSeo(seo);

            await LogEvent("CopyDish", new { id = dishId, newId = newDish.Id });

            return newDish.Id;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDish(CreateDishModel model)
        {
            var actionResult = await CreateDishUnified(model);

            return actionResult ?? RedirectToAction("DishesList");
        }

        private async Task<IActionResult> CreateDishUnified(CreateDishModel model)
        {
            model.Id = Guid.NewGuid().ToString();
            model.PhotoLink = "[]";

            InitializeDishSecondaryFields(model);

            var dish = model.GetDish();

            if (model.Name == null) return BadRequest("Empty name");

            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var prices = JsonConvert.DeserializeObject<List<DishVariation>>(model.Prices);
            var newPrices = prices.Where(x => string.IsNullOrEmpty(x.Id)).Select(x =>
            {
                x.Id = Guid.NewGuid().ToString();
                x.DishId = dish.Id;
                return x;
            });

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var categoriesIdList = !string.IsNullOrEmpty(model.Categories)
                ? Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(model.Categories)
                : new List<string>();

            //if (model.PhotoFile != null && model.PhotoFile.Length > 0)
            //{
            //    var photoFileStream = model.PhotoFile.OpenReadStream();
            //    var photoFileMemoryStream = new MemoryStream();
            //    photoFileStream.CopyTo(photoFileMemoryStream);
            //    var photoFile = new DbFile
            //    {
            //        Id = Guid.NewGuid().ToString(),
            //        Mime = model.PhotoFile.ContentType,
            //        Size = Convert.ToInt32(model.PhotoFile.Length),
            //        Data = photoFileMemoryStream.ToArray()
            //    };
            //    photoFileMemoryStream.Close();
            //    photoFileStream.Close();
            //    await _core.Files.Create(photoFile);

            //    dish.PhotoLink = photoFile.Id + GetFileExtension(photoFile.Mime);
            //}

            FillDishLocalizableFields(model, dish);

            dish.RestaurantId = selectedRestaurant.Id;
            dish.Tags = model.Tags;
            dish.IsPriceMultiple = prices.Count > 1;
            dish.Weight = prices.FirstOrDefault()?.Weight.ToString() ?? "0";
            dish.WeightType = prices.FirstOrDefault()?.Type ?? "g";
            dish.Price = prices.FirstOrDefault()?.Price ?? 0;
            await _core.Dishes.Create(dish);
            await _core.Dishes.CreateDishVariations(newPrices);

            foreach (var categoryId in categoriesIdList)
            {
                var dishCategory = new DishCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    DishId = dish.Id,
                    MenuCategoryId = categoryId
                };

                await _core.Dishes.AddDishToCategory(dishCategory);
            }

            var seo = new DishSeo() as IDishSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.ItemId = dish.Id;
            seo.RestaurantId = selectedRestaurant.Id;
            seo.Url = await _urls.GetNewUrlForDish(seo, dish);
            seo.SearchTerms = ((IDish)dish).Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(((IDish)dish).Name.Get());
            await _core.Dishes.CreateSeo(seo);

            selectedRestaurant.UpdateDate = DateTime.Now;
            await _core.Restaurants.Update(selectedRestaurant);

            await LogEvent("CreateDish", new { id = dish.Id, name = ((IDish)dish).Name.Get() });
            return null;
        }

        private static void InitializeDishSecondaryFields(CreateDishModel model)
        {
            if (model.Description == null) model.Description = new List<LocalizableStringValue>();
            if (model.Ingredients == null) model.Ingredients = new List<LocalizableStringValue>();
            if (model.ShortDescription == null) model.ShortDescription = new List<LocalizableStringValue>();
        }

        private static void FillDishLocalizableFields(CreateDishModel model, IDish dish)
        {
            var description = new LocalizableString() as ILocalizableString;
            description.Replace(model.Description.Select(x => x as ILocalizableStringValue));
            dish.Description = description;
            var ingredients = new LocalizableString() as ILocalizableString;
            ingredients.Replace(model.Ingredients.Select(x => x as ILocalizableStringValue));
            dish.Ingredients = ingredients;
            var name = new LocalizableString() as ILocalizableString;
            name.Replace(model.Name.Select(x => x as ILocalizableStringValue));
            dish.Name = name;
            var shortDescription = new LocalizableString() as ILocalizableString;
            shortDescription.Replace(model.ShortDescription.Select(x => x as ILocalizableStringValue));
            dish.ShortDescription = shortDescription;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDishAndReturn(CreateDishModel model)
        {
            var actionResult = await CreateDishUnified(model);

            return actionResult ?? RedirectToAction("EditDish", new {id = model.Id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDishAndCopy(CreateDishModel model)
        {
            var actionResult = await CreateDishUnified(model);

            var newDishId = await CloneDish(model.Id);

            return actionResult ?? RedirectToAction("EditDish", new {id = newDishId});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeDishPrice(decimal price, string dishId)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest();
            }

            var dish = await _core.Dishes.GetDishById(dishId);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return BadRequest();
            }

            var dishVariations = await _core.Dishes.GetDishVariations(dish.Id);
            dish.Price = price;

            var variation = dishVariations.OrderBy(x => x.Price).First();
            var newVariation = variation.Clone() as IDishVariation;
            newVariation.Price = price;

            await _core.Dishes.Update(dish);
            await _core.Dishes.DeleteDishVariations(new List<IDishVariation> {variation});
            await _core.Dishes.CreateDishVariations(new List<IDishVariation> {newVariation});

            return Ok(new { price = price.ToString("F2") });
        }

        [HttpGet]
        public async Task<IActionResult> EditDish(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var dish = await _core.Dishes.GetDishById(id);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);
            ViewData["variations"] = JsonConvert.SerializeObject((await _core.Dishes.GetDishVariations(dish.Id))
                .Select(x => new
                {
                    id = x.Id, weight = x.Weight, type = x.Type, price = x.Price, weightFull = x.Weight + " " + x.Type
                }).ToList());

            var selectedMenuCategories =
                (await _core.Dishes.GetDishCategories(id)).Select(x => x.MenuCategoryId).ToList();
            ViewData["selectedMenuCategories"] = selectedMenuCategories;
            ViewData["selectedMenuCategoriesJson"] =
                Newtonsoft.Json.JsonConvert.SerializeObject(selectedMenuCategories);
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadDishPhoto";
            ViewData["valueName"] = "dishId";
            ViewData["value"] = dish.Id;

            return View(dish);
        }

        [HttpGet]
        public async Task<IActionResult> CopyDishSingle(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var dish = await _core.Dishes.GetDishById(id);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);
            ViewData["variations"] = JsonConvert.SerializeObject((await _core.Dishes.GetDishVariations(dish.Id))
                .Select(x => new
                {
                    id = "",
                    weight = x.Weight,
                    type = x.Type,
                    price = x.Price,
                    weightFull = x.Weight + " " + x.Type
                }).ToList());

            var selectedMenuCategories =
                (await _core.Dishes.GetDishCategories(id)).Select(x => x.MenuCategoryId).ToList();
            ViewData["selectedMenuCategories"] = selectedMenuCategories;
            ViewData["selectedMenuCategoriesJson"] =
                Newtonsoft.Json.JsonConvert.SerializeObject(selectedMenuCategories);
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;

            ViewData["controller"] = "Home";
            ViewData["action"] = "UploadDishPhoto";
            ViewData["valueName"] = "dishId";
            ViewData["value"] = dish.Id;

            return View(dish);
        }

        [HttpGet]
        public async Task<IActionResult> EditDishIframe(string id, string frameId, string mode, string newId)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var dish = await _core.Dishes.GetDishById(id);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);
            ViewData["variations"] = JsonConvert.SerializeObject((await _core.Dishes.GetDishVariations(dish.Id))
                .Select(x => new
                {
                    id = x.Id, weight = x.Weight, type = x.Type, price = x.Price, weightFull = x.Weight + " " + x.Type
                }).ToList());

            var selectedMenuCategories =
                (await _core.Dishes.GetDishCategories(id)).Select(x => x.MenuCategoryId).ToList();
            ViewData["selectedMenuCategories"] = selectedMenuCategories;
            ViewData["selectedMenuCategoriesJson"] =
                Newtonsoft.Json.JsonConvert.SerializeObject(selectedMenuCategories);
            ViewData["frameId"] = frameId;
            ViewData["mode"] = mode;
            ViewData["newId"] = newId;
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;

            return View(dish);
        }

        [HttpGet]
        public async Task<IActionResult> CreateCopyDishIframe(string id, string frameId, string mode)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var dish = await _core.Dishes.GetDishById(id);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Menu");
            }

            ViewData["menuCategories"] = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);
            ViewData["variations"] = JsonConvert.SerializeObject((await _core.Dishes.GetDishVariations(dish.Id))
                .Select(x => new
                {
                    id = "",
                    weight = x.Weight,
                    type = x.Type,
                    price = x.Price,
                    weightFull = x.Weight + " " + x.Type
                }).ToList());

            var selectedMenuCategories =
                (await _core.Dishes.GetDishCategories(id)).Select(x => x.MenuCategoryId).ToList();
            ViewData["selectedMenuCategories"] = selectedMenuCategories;
            ViewData["selectedMenuCategoriesJson"] =
                Newtonsoft.Json.JsonConvert.SerializeObject(selectedMenuCategories);
            ViewData["frameId"] = frameId;
            ViewData["mode"] = mode;
            ViewData["newId"] = id;
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;

            return View("CreateDishIframe", dish);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDishIframe(CreateDishModel model, string frameId, string mode)
        {
            var actionResult = await EditDishUnified(model);

            string newDishId = null;

            if (mode == "copy")
            {
                //newDishId = await CloneDish(model.Id);
                newDishId = model.Id;
            }

            return actionResult ??
                   RedirectToAction("EditDishIframe", new {id = model.Id, frameId, mode, newId = newDishId});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDish(CreateDishModel model)
        {
            var actionResult = await EditDishUnified(model);

            return actionResult ?? RedirectToAction("DishesList");
        }

        private async Task<IActionResult> EditDishUnified(CreateDishModel model)
        {
            //if (string.IsNullOrEmpty(model.PhotoLink)) model.PhotoLink = "[]";

            InitializeDishSecondaryFields(model);

            var dish = model.GetDish();
            var existingDish = await _core.Dishes.GetDishById(model.Id);
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (model.Name == null) return BadRequest("Empty fields");

            var prices = JsonConvert.DeserializeObject<List<DishVariation>>(model.Prices);
            var existingPrices = await _core.Dishes.GetDishVariations(dish.Id);
            var newPrices = prices.Where(x => string.IsNullOrEmpty(x.Id)).Select(x =>
            {
                x.Id = Guid.NewGuid().ToString();
                x.DishId = dish.Id;
                return x;
            });
            var deletedPriceIds = existingPrices.Select(x => x.Id).Except(prices.Select(x => x.Id));
            var deletePrices = existingPrices.Where(x => deletedPriceIds.Contains(x.Id));

            if (!(await _core.Restaurants.IsUserAllowedToEdit(existingDish.RestaurantId, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            var categoriesIdList = JsonConvert.DeserializeObject<List<string>>(model.Categories);

            //if (string.IsNullOrEmpty(model.PhotoLink) && !string.IsNullOrEmpty(existingDish.PhotoLink))
            //{
            //    dish.PhotoLink = "";
            //}

            //if (model.PhotoFile != null && model.PhotoFile.Length > 0)
            //{
            //    var photoFileStream = model.PhotoFile.OpenReadStream();
            //    var photoFileMemoryStream = new MemoryStream();
            //    photoFileStream.CopyTo(photoFileMemoryStream);
            //    var photoFile = new DbFile
            //    {
            //        Id = Guid.NewGuid().ToString(),
            //        Mime = model.PhotoFile.ContentType,
            //        Size = Convert.ToInt32(model.PhotoFile.Length),
            //        Data = photoFileMemoryStream.ToArray()
            //    };
            //    photoFileMemoryStream.Close();
            //    photoFileStream.Close();
            //    await _core.Files.Create(photoFile);

            //    dish.PhotoLink = photoFile.Id + GetFileExtension(photoFile.Mime);
            //}

            FillDishLocalizableFields(model, dish);
            var iDish = (IDish) dish;

            existingDish.Name = iDish.Name;
            existingDish.ShortDescription = iDish.ShortDescription;
            existingDish.Ingredients = iDish.Ingredients;
            existingDish.Description = iDish.Description;
            existingDish.ShowOnIndex = iDish.ShowOnIndex;
            //existingDish.PhotoLink = iDish.PhotoLink;

            existingDish.IsPriceMultiple = prices.Count > 1;
            (existingDish as Dish).Tags = model.Tags;
            existingDish.Weight = prices.FirstOrDefault()?.Weight.ToString() ?? "0";
            existingDish.WeightType = prices.FirstOrDefault()?.Type ?? "g";
            existingDish.Price = prices.FirstOrDefault()?.Price ?? 0;
            await _core.Dishes.Update(existingDish);
            await _core.Dishes.CreateDishVariations(newPrices);
            await _core.Dishes.DeleteDishVariations(deletePrices);

            var dishSeo = await _core.Dishes.GetSeoByDishId(dish.Id);
            if (dishSeo == null)
            {
                dishSeo = new DishSeo();
                dishSeo.Id = Guid.NewGuid().ToString();
                dishSeo.Url = await _urls.GetNewUrlForDish(dishSeo, iDish);
                dishSeo.ItemId = iDish.Id;
                dishSeo.RestaurantId = existingDish.RestaurantId;

                dishSeo.SearchTerms = existingDish.Name.GetAll() + " " + UrlGenerateService.TranslitWithCaseSave(existingDish.Name.Get());

                await _core.Dishes.CreateSeo(dishSeo);
            }

            var dishCategories = await _core.Dishes.GetDishCategories(existingDish.Id);
            var dishCategoriesIds = dishCategories.Select(x => x.MenuCategoryId).ToList();
            var removedIds = dishCategoriesIds.Except(categoriesIdList);
            var removedCategories = dishCategories.Where(x => removedIds.Contains(x.MenuCategoryId));
            await _core.Dishes.RemoveDishFromCategories(removedCategories);

            var createdCategories = categoriesIdList.Except(dishCategoriesIds);

            foreach (var categoryId in createdCategories)
            {
                var dishCategory = new DishCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    DishId = dish.Id,
                    MenuCategoryId = categoryId
                };
                var currentDishesCount = (await _core.Dishes.GetDishesForCategory(categoryId)).Count();
                dishCategory.Index = currentDishesCount;

                await _core.Dishes.AddDishToCategory(dishCategory);
            }

            selectedRestaurant.UpdateDate = DateTime.Now;
            await _core.Restaurants.Update(selectedRestaurant);

            await LogEvent("EditDish", new { id = dish.Id, name = iDish.Name.Get() });

            return null;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDishAndReturn(CreateDishModel model)
        {
            var actionResult = await EditDishUnified(model);

            return actionResult ?? RedirectToAction("EditDish", new {id = model.Id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditDishAndCopy(CreateDishModel model)
        {
            var actionResult = await EditDishUnified(model);

            //string newDishId = await CloneDish(model.Id);

            return actionResult ?? RedirectToAction("CopyDishSingle", new {id = model.Id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyDish(string id)
        {
            var existingDish = await _core.Dishes.GetDishById(id);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(existingDish.RestaurantId, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest();
            }

            var newDish = existingDish.Clone() as IDish;
            newDish.Id = Guid.NewGuid().ToString();

            //var newDishSeo = (IDishSeo)(await _core.Dishes.GetSeoByDishId(existingDish.Id)).Clone();
            //if (newDishSeo != null) {
            //    newDishSeo.Id = Guid.NewGuid().ToString();
            //    newDishSeo.ItemId = newDish.Id;
            //    if (!string.IsNullOrEmpty(newDishSeo.Url)) newDishSeo.Url = newDishSeo.Url + "-copy";
            //}

            var dishCategories = await _core.Dishes.GetDishCategories(existingDish.Id);

            var prices = (await _core.Dishes.GetDishVariations(existingDish.Id)).ToList();
            var newPrices = prices.Select(x =>
            {
                var n = (IDishVariation) x.Clone();
                n.Id = Guid.NewGuid().ToString();
                n.DishId = newDish.Id;
                return n;
            });

            newDish.IsPriceMultiple = prices.Count > 1;
            newDish.Weight = prices.FirstOrDefault()?.Weight.ToString() ?? "0";
            newDish.WeightType = prices.FirstOrDefault()?.Type ?? "g";
            newDish.Price = prices.FirstOrDefault()?.Price ?? 0;
            await _core.Dishes.Create(newDish);
            //if(newDishSeo != null) await _core.Dishes.CreateSeo(newDishSeo);

            foreach (var category in dishCategories)
            {
                category.Id = Guid.NewGuid().ToString();
                category.Index = (await _core.Dishes.GetDishesForCategory(category.MenuCategoryId)).Count() + 1;
                category.DishId = newDish.Id;

                await _core.Dishes.AddDishToCategory(category);
            }

            await _core.Dishes.CreateDishVariations(newPrices);
            
            await LogEvent("CopyDish", new { id = id, newId = newDish.Id });

            return Ok(new {dishId = newDish.Id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyCategory(string id)
        {
            var category = await _core.Restaurants.GetMenuCategoryById(id);
            category = category.Clone() as IMenuCategory;

            if (!(await _core.Restaurants.IsUserAllowedToEdit(category.RestaurantId, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest();
            }

            var dishCategories = (await _core.Dishes.GetDishesForCategory(category.Id)).ToList();

            category.Id = Guid.NewGuid().ToString();
            category.Index = (await _core.Restaurants.GetMenuCategoriesForRestaurant(category.RestaurantId)).Count() +
                             1;

            var resultDishCategories = new List<IDishCategory>();

            foreach (var dishCategory in dishCategories)
            {
                var newDishCategory = dishCategory.Clone() as IDishCategory;
                newDishCategory.Id = Guid.NewGuid().ToString();
                newDishCategory.MenuCategoryId = category.Id;
                resultDishCategories.Add(newDishCategory);
            }

            await _core.Restaurants.CreateMenuCategory(category);

            await _core.Dishes.CreateDishCategories(resultDishCategories);

            return Ok();
        }

        [HttpGet]
        public IActionResult CreateMenuCategory() //-V3013
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> EditMenuCategory(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var category = await _core.Restaurants.GetMenuCategoryById(id);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMenuCategory(string id, List<LocalizableStringValue> Name, List<LocalizableStringValue> Description)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var category = await _core.Restaurants.GetMenuCategoryById(id);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            foreach (var name in Name)
            {
                name.Value = name.Value.ToLower();
            }

            var categoryName = new LocalizableString() as ILocalizableString;
            categoryName.Replace(Name.Select(x => x as ILocalizableStringValue));

            var categoryDescription = new LocalizableString() as ILocalizableString;
            categoryDescription.Replace(Description.Select(x => x as ILocalizableStringValue));

            category.Name = categoryName;
            category.Description = categoryDescription;
            await _core.Restaurants.UpdateMenuCategory(category);

            return RedirectToAction("Menu");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMenuCategory(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var category = await _core.Restaurants.GetMenuCategoryById(id);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            category.Active = !category.Active;
            await _core.Restaurants.UpdateMenuCategory(category);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyLang(string from, string to)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
            var dishes = (await _core.Dishes.GetDishesForRestaurant(selectedRestaurant.Id)).ToList();

            foreach (var category in categories)
            {
                category.Name.CopyLocale(from, to);
                category.Name = category.Name;
            }

            foreach (var dish in dishes)
            {
                dish.Description.CopyLocale(from, to);
                dish.Ingredients.CopyLocale(from, to);
                dish.Name.CopyLocale(from, to);
                dish.ShortDescription.CopyLocale(from, to);

                dish.Description = dish.Description;
                dish.Ingredients = dish.Ingredients;
                dish.Name = dish.Name;
                dish.ShortDescription = dish.ShortDescription;
            }

            await _core.Restaurants.UpdateMenuCategories(categories);
            await _core.Dishes.Update(dishes);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLang(string lang)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
            var dishes = (await _core.Dishes.GetDishesForRestaurant(selectedRestaurant.Id)).ToList();

            foreach (var category in categories)
            {
                category.Name.Remove(lang);

                category.Name = category.Name;
            }

            foreach (var dish in dishes)
            {
                dish.Description.Remove(lang);
                dish.Ingredients.Remove(lang);
                dish.Name.Remove(lang);
                dish.ShortDescription.Remove(lang);

                dish.Description = dish.Description;
                dish.Ingredients = dish.Ingredients;
                dish.Name = dish.Name;
                dish.ShortDescription = dish.ShortDescription;
            }

            await _core.Restaurants.UpdateMenuCategories(categories);
            await _core.Dishes.Update(dishes);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMenuCategory(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var category = await _core.Restaurants.GetMenuCategoryById(id);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            await _core.Restaurants.DeleteMenuCategory(id);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMenuCategory(List<LocalizableStringValue> Name, List<LocalizableStringValue> Description, bool topLevel = false)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);
            var categories = await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id);

            var newCategory = new MenuCategory() as IMenuCategory;
            newCategory.Id = Guid.NewGuid().ToString();
            newCategory.RestaurantId = selectedRestaurant.Id;
            newCategory.IsTopLevelCategory = topLevel;
            newCategory.Index = topLevel ? categories.Count(x => x.IsTopLevelCategory) : categories.Count(x => !x.IsTopLevelCategory);

            var categoryName = new LocalizableString() as ILocalizableString;
            categoryName.Replace(LowerCaseLocalizableString(Name).Select(x => x as ILocalizableStringValue));

            var categoryDescription = new LocalizableString() as ILocalizableString;
            categoryDescription.Replace(LowerCaseLocalizableString(Description).Select(x => x as ILocalizableStringValue));

            newCategory.Name = categoryName;
            newCategory.Description = categoryDescription;
            newCategory.Active = true;

            await _core.Restaurants.CreateMenuCategory(newCategory);

            return RedirectToAction("Menu");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeMenuCategoriesOrder(List<MenuCategory> menuCategories)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            var categories = await _core.Restaurants.GetAllMenuCategoriesForRestaurant(selectedRestaurant.Id);
            foreach (var category in categories)
            {
                var newCategory = menuCategories.FirstOrDefault(x => x.Id == category.Id);
                if (newCategory != null)
                {
                    category.Index = newCategory.Index;
                }
            }

            await _core.Restaurants.UpdateMenuCategories(categories);

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeDishesOrder(string menuCategory, List<DishCategory> dishes)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            var dishCategories =
                await _core.Restaurants.GetDishCategoriesForMenuCategories(new List<string> {menuCategory});
            foreach (var dishCategory in dishCategories)
            {
                dishCategory.Index = dishes.First(x => x.Id == dishCategory.DishId).Index;
            }

            await _core.Dishes.UpdateMultipleDishCategories(dishCategories);

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> DishSeo(string id)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var dish = await _core.Dishes.GetDishById(id);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Index", "Home");
            }

            var seo = await _core.Dishes.GetSeoByDishId(dish.Id);
            if (seo == null) seo = new DishSeo() {ItemId = dish.Id, RestaurantId = selectedRestaurant.Id};
            return View(seo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DishSeo(DishSeo model)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Index", "Home");
            }

            var dish = await _core.Dishes.GetDishById(model.ItemId);
            if (dish.RestaurantId != selectedRestaurant.Id)
            {
                return RedirectToAction("Index", "Home");
            }

            var seo = await _core.Dishes.GetSeoByDishId(dish.Id);
            var createSeo = false;
            if (seo != null)
            {
                seo.Url = model.Url;
                seo.Header = (model as ISeo).Header;
                seo.Description = (model as ISeo).Description;
                seo.Title = (model as ISeo).Title;
                seo.Keywords = model.Keywords;
                seo.SeoText = model.SeoText;
                seo.SearchTerms = model.SearchTerms;
            }
            else
            {
                seo = model;
                seo.Id = Guid.NewGuid().ToString();
                createSeo = true;
            }

            if (createSeo) await _core.Dishes.CreateSeo(seo);
            else
            {
                try
                {
                    await _core.Dishes.UpdateSeo(seo);
                }
                catch
                {
                    ViewData["urlExist"] = true;
                    return View(seo);
                }

            }

            return RedirectToAction("DishesList");
        }

        [HttpGet]
        public async Task<IActionResult> Register(string restaurantId)
        {
            if (!string.IsNullOrEmpty(restaurantId))
            {
                var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(restaurantId);

                RegisterRestaurantModel model = new RegisterRestaurantModel();
                model.Id = restaurant.Id;
                model.Phone = restaurant.Phone;
                model.Address = restaurant.Address;
                model.City = restaurant.City;
                model.Name = restaurant.Name;
                model.Email = restaurant.Email;
                return View(model);
            }
            else
            {
                return View();
            }
        }

        [HttpGet]
        public IActionResult RequestAccess()
        {
            return View();
        }


        [HttpGet]
        public IActionResult RegisterSuccess(bool isRequestAccess)
        {
            ViewData["isRequestAccess"] = isRequestAccess;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestAccess(RegisterRestaurantModel model)
        {
            if (!string.IsNullOrEmpty(model.Id))
            {
                return RedirectToAction("Register", "Manage", new { restaurantId = model.Id  });
            } else
            {
                return View();
            }
            
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterRestaurantModel model)
        {
            if (string.IsNullOrEmpty(model.Id))
            {
                model.Id = Guid.NewGuid().ToString();
                model.RegistrationLink = "";
                model.LicenseLink = "";
                //var properties = model.GetType().GetProperties();
                //foreach (var prop in properties)
                //{
                //    if (prop.GetValue(model) == null) return BadRequest("Empty fields");
                //}

                //if (!RegisterFilesAreSupportedTypes(model)) return BadRequest("Unsupported files type");

                var restaurant = model.GetRestaurant();

                if (model.LicenseFile != null)
                {
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
                    restaurant.LicenseLink = licenseFile.Id + GetFileExtension(licenseFile.Mime);
                    await _core.Files.Create(licenseFile);
                }

                if (model.RegistrationFile != null)
                {
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
                    restaurant.RegistrationLink = registrationFile.Id + GetFileExtension(registrationFile.Mime);
                    await _core.Files.Create(registrationFile);
                }

                restaurant.Status = RestaurantStatus.Pending;
                restaurant.CreateDate = DateTime.Now;

                await _core.Restaurants.Create(restaurant, new RestaurantRole
                {
                    Id = Guid.NewGuid().ToString(),
                    RestaurantId = restaurant.Id,
                    Role = RestaurantUserRole.Owner,
                    UserId = User.Identity.Name
                });

                var restaurantInfo = new RestaurantInfo() as IRestaurantInfo;
                restaurantInfo.Id = Guid.NewGuid().ToString();
                restaurantInfo.Currency = "₴";
                restaurantInfo.Cuisine = new LocalizableString();
                restaurantInfo.FullInfo = new LocalizableString();
                restaurantInfo.ShortInfo = new LocalizableString();
                restaurantInfo.RestaurantId = model.Id;
                restaurantInfo.OpenHours = "{\"perDay\":false,\"data\":[{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true}]}";
                restaurantInfo.UserAllowedAddDishes = false;
                restaurantInfo.UserAllowedAddMenu = false;
                restaurantInfo.UserAllowedOpenMenu = false;
                restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = "ru" } } };
                await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);

                var seo = new RestaurantSeo() as IRestaurantSeo;
                seo.Id = Guid.NewGuid().ToString();
                seo.ItemId = restaurant.Id;
                seo.Url = _urls.GetNewUrlForRestaurant(seo, restaurant);
                seo.SearchTerms = restaurant.Name + " " + seo.Url;
                seo.Title = new LocalizableString() { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Name, Locale = "ru" } } };
                seo.Description = new LocalizableString() { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Name, Locale = "ru" } } };
                seo.Header = new LocalizableString() { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Name, Locale = "ru" } } };
                await _core.Restaurants.CreateSeo(seo);

                await _db.CustomerRequests.Add(new CustomerRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    UpdateDate = DateTime.Now,
                    CreateDate = DateTime.Now,
                    Status = CustomerRequestStatus.NewRequest,
                    Email = restaurant.Email,
                    RestaurantName = restaurant.Id,
                    Package = "Request for Access for: " + restaurant.Name,
                    Phone = restaurant.Phone
                });

                await LogEvent("RegisterRestaurant", new { id = restaurant.Id, name = restaurant.Name });
                if (User.IsInRole("admin") || User.IsInRole("manager"))
                {
                    return RedirectToAction("Index", "Restaurants", new { area = "Admin" });
                }
                else
                {
                    return RedirectToAction("RegisterSuccess", "Manage", new { isRequestAccess = false });
                }
            }
            else
            {
                try
                {


                    var restaurant = await _core.Restaurants.GetRestaurantByIdOrSeo(model.Id);
                    //change owner
                    var user = await _core.Users.GetUserById(User.Identity.Name);

                    var restaurantOwner = await _core.Restaurants.GetCurrentOwnerForRestaurant(restaurant.Id);
                    if (restaurantOwner == null)
                    {
                        restaurantOwner = new RestaurantRole
                        {
                            Id = Guid.NewGuid().ToString(),
                            RestaurantId = restaurant.Id,
                            Role = RestaurantUserRole.Owner,
                            UserId = User.Identity.Name
                        };
                        await _core.Restaurants.CreateRestaurantRole(restaurantOwner);
                    }
                    else
                    {
                        restaurantOwner.UserId = User.Identity.Name;
                        await _core.Restaurants.UpdateRestaurantRole(restaurantOwner);
                    }

                    //update files
                    if (model.LicenseFile != null)
                    {
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
                        restaurant.LicenseLink = licenseFile.Id + GetFileExtension(licenseFile.Mime);
                        await _core.Files.Create(licenseFile);
                    }

                    if (model.RegistrationFile != null)
                    {
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
                        restaurant.RegistrationLink = registrationFile.Id + GetFileExtension(registrationFile.Mime);
                        await _core.Files.Create(registrationFile);
                    }

                    restaurant.Status = RestaurantStatus.Pending;
                    restaurant.UpdateDate = DateTime.Now;
                    await _core.Restaurants.Update(restaurant);

                    await _db.CustomerRequests.Add(new CustomerRequest
                    {
                        Id = Guid.NewGuid().ToString(),
                        UpdateDate = DateTime.Now,
                        CreateDate = DateTime.Now,
                        Status = CustomerRequestStatus.NewRequest,
                        Email = restaurant.Email,
                        RestaurantName = model.Id,
                        Package = "Request for Access for: " + restaurant.Name,
                        Phone = restaurant.Phone
                    });

                    await LogEvent("RequestAccessRestaurant", new { id = restaurant.Id, name = restaurant.Name });
                    if (User.IsInRole("admin") || User.IsInRole("manager"))
                    {
                        return RedirectToAction("Index", "Restaurants", new { area = "Admin" });
                    }
                    else
                    {
                        return RedirectToAction("RegisterSuccess", "Manage", new { isRequestAccess = true });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

            }
            return View();
        }

        public async Task<IActionResult> CategoriesList(string restaurantId, string categoryId)
        {
            var parentCategory = await _core.Restaurants.GetMenuCategoryById(categoryId);
            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(restaurantId)).Where(x => !x.IsTopLevelCategory);

            ViewData["parentCategory"] = parentCategory;

            return View(categories);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleChildCategory(string categoryId, string childCategoryId)
        {
            var existingChildCategoryRelation = _db.MenuCategoryRelations.FirstOrDefault(x => x.ParentId == categoryId && x.ChildId == childCategoryId);
            if (existingChildCategoryRelation != null)
            {
                await _db.MenuCategoryRelations.Remove(existingChildCategoryRelation);
            }
            else
            {
                var categoryRelation = new MenuCategoryRelation
                {
                    ChildId = childCategoryId,
                    ParentId = categoryId
                };
                await _db.MenuCategoryRelations.Add(categoryRelation);
            }

            return Ok();
        }

        public async Task<IActionResult> DishesList(string dishCategoryId)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return RedirectToAction("Menu");
            }

            ViewData["dishCategoryId"] = dishCategoryId;

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
            var dishes =
                (await _core.Dishes.GetDishesForRestaurantWithSeoAndRatings(selectedRestaurant.Id, "")).ToList();
            var dishCategories =
                (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id)))
                .OrderBy(x => x.Index).ToList();
            ViewData["currency"] = (await _core.Restaurants.GetRestaurantInfo(selectedRestaurant.Id)).Currency;

            var model = new DishesListViewModel {Categories = new List<DishesListViewModelCategory>()};

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
                    var iModelCategory = (IMenuCategory) modelCategory;
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

            var includedDishIds = dishCategories.GroupBy(x => x.DishId).Select(x => x.Key);
            var allDishIds = dishes.Select(x => x.Id);
            var excludedDishes = allDishIds.Except(includedDishIds);

            var excludedCategory = new DishesListViewModelCategory
            {
                Active = true,
                Id = "",
                Index = 99999,
                RestaurantId = selectedRestaurant.Id
            };
            var excludedCategoryName = new LocalizableString {Values = new List<LocalizableStringValue>()};
            excludedCategoryName.Add(new LocalizableStringValue {Locale = "ru", Value = "-----"});
            ((IMenuCategory) excludedCategory).Name = excludedCategoryName;

            excludedCategory.Dishes =
                dishes.Where(x => excludedDishes.Contains(x.Id)).OrderBy(x => x.Name.Get()).ToList();
            if (excludedCategory.Dishes.Any()) model.Categories.Add(excludedCategory);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDishes(List<string> dishes)
        {
            var selectedRestaurant = await _usrService.GetSelecedRestaurant(User, Request);

            if (!(await _core.Restaurants.IsUserAllowedToEdit(selectedRestaurant.Id, User.Identity.Name)) &&
                !User.IsInRole("admin") &&
                !User.IsInRole("manager"))
            {
                return BadRequest("Don't have permissions");
            }

            dishes = dishes.GroupBy(x => x).Select(x => x.Key).ToList();
            await _core.Dishes.Delete(dishes, selectedRestaurant.Id);

            var categories = (await _core.Restaurants.GetMenuCategoriesForRestaurant(selectedRestaurant.Id)).ToList();
            var allDishCategories =
                (await _core.Restaurants.GetDishCategoriesForMenuCategories(categories.Select(x => x.Id))).ToList();

            foreach (var category in categories)
            {
                var dishCategories =
                    allDishCategories.Where(x => x.MenuCategoryId == category.Id).OrderBy(x => x.Index);
                var i = 0;
                foreach (var dishCategory in dishCategories)
                {
                    dishCategory.Index = i;
                    i++;
                }
            }

            await _core.Dishes.UpdateMultipleDishCategories(allDishCategories);

            selectedRestaurant.UpdateDate = DateTime.Now;
            await _core.Restaurants.Update(selectedRestaurant);

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UploadDishPhoto(string base64, string dishId)
        {
            var dish = await _core.Dishes.GetDishById(dishId);

            if (dish == null) return BadRequest("Dish not found");

            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = "image/png",
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            var dishPhoto = new DishPhoto
            {
                Id = Guid.NewGuid().ToString(),
                Index = 999,
                Status = DishPhotoStatus.Approved,
                UserId = User.Identity.Name,
                DishId = dishId,
                ImageUrl = file.Id + ".png",
                Timestamp = DateTime.Now
            };

            await _core.Dishes.AddUserDishPhoto(dishPhoto);

            await LogEvent("UploadDishPhoto", new { dishId });

            return Ok(new { id = file.Id, name = file.Id + ".png", approved = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDishPhoto(string dishId)
        {
            var dish = await _core.Dishes.GetDishById(dishId);

            if (dish == null) return BadRequest("Dish not found");

            await _core.Dishes.DeleteDishPhotosForDish(dishId);

            await LogEvent("DeletedDishPhotos", new { dishId });

            return Ok();
        }

        private async Task LogEvent(string name, object data)
        {
            var logItem = new LogItem
            {
                Id = Guid.NewGuid().ToString(),
                UserId = User.Identity.Name,
                DeviceId = Request.Cookies[".Menus.DeviceId"],
                Event = name,
                EventData = JsonConvert.SerializeObject(data),
                Timestamp = DateTime.Now
            };
            await _core.LogItems.Create(logItem);
        }

        private static bool RegisterFilesAreSupportedTypes(RegisterRestaurantModel model)
        {
            return (model.LicenseFile.ContentType == "image/png" || model.LicenseFile.ContentType == "image/jpeg" ||
                    model.LicenseFile.ContentType == "application/pdf") &&
                   (model.RegistrationFile.ContentType == "image/png" ||
                    model.RegistrationFile.ContentType == "image/jpeg" ||
                    model.LicenseFile.ContentType == "application/pdf");
        }

        private static bool DishFilesAreSupportedTypes(CreateDishModel model)
        {
            return (model.PhotoFile.ContentType == "image/png" || model.PhotoFile.ContentType == "image/jpeg");
        }

        private static LocalizableString LowerCaseLocalizableString(LocalizableString input)
        {
            foreach (var item in input.Values)
            {
                item.Value = item.Value.ToLower();
            }

            return input;
        }

        private static List<LocalizableStringValue> LowerCaseLocalizableString(List<LocalizableStringValue> input)
        {
            if (input == null) return input;
            foreach (var item in input)
            {
                item.Value = item.Value?.ToLower();
            }

            return input;
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
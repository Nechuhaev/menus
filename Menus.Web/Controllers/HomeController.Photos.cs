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

        // Службова дія: створює записи DishPhotos для фото, доданих в обхід галереї (content-bot).
        // Перебирає всі страви, тому доступна лише з паролем службових ендпоінтів (Sitemap:Password)
        [HttpGet]
        public async Task<IActionResult> UpdateAllDishPhotos(string pwd)
        {
            var servicePassword = _config["Sitemap:Password"];
            if (string.IsNullOrEmpty(servicePassword) || pwd != servicePassword) return NotFound();

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
    }
}

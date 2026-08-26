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
    public class DishPhotosController : Controller
    {
        private readonly Core _core;
        public DishPhotosController(Core core)
        {
            _core = core;
        }
        // GET: DishPhotos
        public async Task<IActionResult> Index(int? status, int page = 0)
        {
            ViewData["status"] = status;
            ViewData["page"] = page;
            var data = (await _core.Dishes.GetDishesPhotos(status)).Skip(page * 10).Take(10).ToList();
            return View(data);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangeDishPhotoStatus(string id, DishPhotoStatus status)
        {
            var dishPhoto = await _core.Dishes.GetDishPhotoEntity(id);
            if (dishPhoto != null)
            {
                dishPhoto.Status = status;
                await _core.Dishes.UpdateDishPhoto(dishPhoto);
                return Ok();
            }
            return BadRequest();
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            await _core.Dishes.DeleteDishPhoto(id);
            return Ok();
        }
    }
}
// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "admin, manager")]
    public class CustomerRequestsController : Controller
    {
        private readonly CustomerRequestService _requests;
        private readonly PosterService _poster;

        public CustomerRequestsController(CustomerRequestService requests, PosterService poster)
        {
            _requests = requests;
            _poster = poster;
        }

        // фільтри по стовпцях: ресторан, пакет, email, телефон, IP, статус, дата створення від/до
        public async Task<IActionResult> Index(CustomerRequestFilter filter, int page = 0)
        {
            filter = filter ?? new CustomerRequestFilter();
            var (rows, total) = await _requests.List(filter, page);
            ViewData["page"] = page;
            ViewData["total"] = total;
            ViewData["filter"] = filter;
            return View(rows);
        }

        public async Task<IActionResult> Edit(string id)
        {
            var row = await _requests.Get(id);
            if (row == null) return NotFound();
            ViewData["posterPending"] = await _poster.PendingByRequest(id); // встановлення з каталогу Poster чекає на заклад
            return View(row);
        }

        // заявка з каталогу Poster: прив'язати акаунт Poster до закладу (Id або адреса сторінки)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LinkPoster(string id, string restaurant)
        {
            var error = await _poster.LinkPending(id, restaurant);
            TempData["message"] = error == null ? "Poster прив'язано - меню з каси підтягнеться найближчою синхронізацією" : "Poster не прив'язано: " + error;
            return error == null ? RedirectToAction("Index") : RedirectToAction("Edit", new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, string restaurantName, string package, string email, string phone, int status)
        {
            await _requests.Update(id, restaurantName, package, email, phone, status);
            TempData["message"] = "Запит збережено";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(string id, int status)
        {
            await _requests.SetStatus(id, status);
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(List<string> ids, string returnUrl)
        {
            await _requests.Delete(ids);
            TempData["message"] = $"Видалено запитів: {ids?.Count ?? 0}";
            return Url.IsLocalUrl(returnUrl) ? (IActionResult)Redirect(returnUrl) : RedirectToAction("Index");
        }
    }
}

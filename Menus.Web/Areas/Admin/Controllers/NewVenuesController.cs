using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // «Рестораны» -> «Нові заклади»: кандидати від content-bot/new_venues.py
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class NewVenuesController : Controller
    {
        private readonly Core _core;
        private readonly NewVenueService _venues;

        public NewVenuesController(Core core, NewVenueService venues)
        {
            _core = core;
            _venues = venues;
        }

        public async Task<IActionResult> Index(string status = "new", string cityId = null)
        {
            if (!NewVenueService.Statuses.ContainsKey(status ?? "")) status = "new";
            ViewData["status"] = status;
            ViewData["cityId"] = cityId;
            ViewData["cities"] = (await _core.Restaurants.GetCities()).ToList();
            ViewData["counts"] = await _venues.Counts();
            return View(await _venues.List(status, string.IsNullOrEmpty(cityId) ? null : cityId));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(int id, string status, string note, string returnStatus, string cityId)
        {
            await _venues.SetStatus(id, status, note);
            TempData["message"] = status == "approved" ? "Схвалено: бот створить заклад уночі (new_venues.py --create), меню підтягне наступної ночі"
                : status == "rejected" ? "Позначено: не додавати" : status == "duplicate" ? "Позначено як дубль" : "Повернуто в нові";
            return RedirectToAction("Index", new { status = returnStatus, cityId });
        }
    }
}

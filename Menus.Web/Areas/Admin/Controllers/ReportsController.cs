using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // Звіти content-bot: зміни меню, знайдені на сайтах ресторанів, застосовуються лише звідси
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class ReportsController : Controller
    {
        private readonly MenuSyncService _menuSync;

        public ReportsController(MenuSyncService menuSync)
        {
            _menuSync = menuSync;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _menuSync.GetPendingSummary());
        }

        public async Task<IActionResult> Restaurant(string id)
        {
            ViewData["restaurantId"] = id;
            ViewData["branches"] = await _menuSync.GetBranches(id);
            return View(await _menuSync.GetPendingForRestaurant(id));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(string id, List<string> ids, string decision, bool includeBranches = false)
        {
            ids = ids ?? new List<string>();
            if (ids.Any())
            {
                if (decision == "reject")
                {
                    await _menuSync.Reject(id, ids);
                    TempData["message"] = $"Відхилено: {ids.Count}";
                }
                else
                {
                    var r = await _menuSync.Apply(id, ids, includeBranches);
                    TempData["message"] = $"Додано страв: {r.Added}, приховано: {r.Removed}, змінено цін: {r.Repriced}" +
                        (includeBranches ? $" (разом із філіями: {r.Branches})" : "");
                }
            }
            return RedirectToAction("Restaurant", new { id });
        }
    }
}

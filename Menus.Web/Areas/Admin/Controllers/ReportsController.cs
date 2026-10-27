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
        private readonly MenuLinkProposalService _menuLinkProposals;

        public ReportsController(MenuSyncService menuSync, MenuLinkProposalService menuLinkProposals)
        {
            _menuSync = menuSync;
            _menuLinkProposals = menuLinkProposals;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["aiCost"] = await _menuSync.GetAiCostTotals();
            ViewData["menuLinkRestaurants"] = await _menuLinkProposals.GetPendingRestaurantsCount();
            return View(await _menuSync.GetPendingSummary());
        }

        public async Task<IActionResult> Restaurant(string id, string kind)
        {
            ViewData["restaurantId"] = id;
            ViewData["kind"] = kind; // add | remove | price - фільтр, з яким відкрито звіт
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
                    TempData["message"] = $"Додано страв: {r.Added}, приховано: {r.Removed}, змінено цін: {r.Repriced}, перейменовано: {r.Renamed}" +
                        (includeBranches ? $" (разом із філіями: {r.Branches})" : "");
                }
            }
            return RedirectToAction("Restaurant", new { id });
        }

        // Посилання на меню в Expirenza/ChoiceQR, знайдені content-bot/find_menu_links.py
        public async Task<IActionResult> MenuLinks()
        {
            return View(await _menuLinkProposals.GetPending());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MenuLinkDecision(int id, string decision)
        {
            if (decision == "approve")
            {
                TempData["message"] = await _menuLinkProposals.Approve(id) ? "Посилання додано в картку ресторану" : "Пропозицію вже оброблено";
            }
            else
            {
                await _menuLinkProposals.Reject(id);
                TempData["message"] = "Відхилено";
            }
            return RedirectToAction("MenuLinks");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRecommendedMenuLinks()
        {
            TempData["message"] = $"Додано посилань: {await _menuLinkProposals.ApproveRecommended()}";
            return RedirectToAction("MenuLinks");
        }
    }
}

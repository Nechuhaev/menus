// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            if (User.IsInRole("admin")) return RedirectToAction("Index", "Users");
            if (User.IsInRole("manager")) return RedirectToAction("Managers", "Users");
            Response.StatusCode = 404;
            return View("NotFound");
        }
    }
}
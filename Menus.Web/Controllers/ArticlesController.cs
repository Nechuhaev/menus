// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Controllers
{
    public class ArticlesController : Controller
    {
        public IActionResult AboutUs()
        {
            return View();
        }

        public IActionResult Media()
        {
            return View();
        }


        public IActionResult ForBusiness()
        {
            return View();
        }

        public IActionResult ForOwners()
        {
            return View();
        }

        public IActionResult Marketing()
        {
            return View();
        }

        public IActionResult Agreement()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }
    }
}
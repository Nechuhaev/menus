// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract;
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
    public class FeedbacksController : Controller
    {
        private readonly Core _core;
        private readonly IDbContext _db;
        public FeedbacksController(Core core, IDbContext db)
        {
            _core = core;
            _db = db;
        }
        // GET: Feedbacks
        public async Task<IActionResult> Index(int page = 0)
        {
            ViewData["page"] = page;
            var feedbacks = (await _core.Typos.GetAll()).OrderByDescending(x => x.CreateDate).Skip(page * 10).Take(10).ToList();
            return View(feedbacks);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> ChangeStatus(string id, TypoRequestStatus status)
        {
            var request = _db.TypoRequests.First(x => x.Id == id);
            request.Status = status;
            await _db.TypoRequests.Update(request);
            return Ok();
        }
    }
}
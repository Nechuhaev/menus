// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CustomerRequestsController : Controller
    {
        private readonly IDbContext _db;

        public CustomerRequestsController(IDbContext db)
        {
            _db = db;
        }

        [Authorize(Roles = "admin, manager")]
        public IActionResult Index(int page = 0)
        {
            ViewData["page"] = page;
            IEnumerable<ICustomerRequest> requests = _db.CustomerRequests.OrderByDescending(x => x.CreateDate).Skip(page * 10).Take(10).ToList();

            return View(requests);
        }

        [ValidateAntiForgeryToken]
        [HttpPost]
        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> ChangeStatus(string id, CustomerRequestStatus status)
        {
            var request = _db.CustomerRequests.First(x => x.Id == id);
            request.Status = status;
            request.UpdateDate = DateTime.Now;
            await _db.CustomerRequests.Update(request);
            return Ok();
        }
    }
}
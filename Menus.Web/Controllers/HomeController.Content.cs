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
        public async Task<IActionResult> SendTypo(string email, string text, string url)
        {
            var userId = User.Identity.Name;
            var typo = new TypoRequest
            {
                Id = Guid.NewGuid().ToString(),
                CreateDate = DateTime.Now,
                Text = text,
                Url = url,
                Email = email,
                UserId = string.IsNullOrEmpty(userId) ? "1a950ec5-7fd3-4d82-adca-d27fda6025b5" : userId
            };

            url = url.Replace($"{Request.Scheme}://{Request.Host}", "");

            await _core.Typos.Create(typo);

            return LocalRedirect(url);
        }

        [Authorize]
        public IActionResult MobileTypo(string url)
        {
            ViewData["url"] = url;
            return View();
        }

        public async Task<IActionResult> ContentPage(string id)
        {
            var page = await _core.Pages.GetPublishedByUrl(id, PageCategory.Content);
            return page == null ? View("NotFound") : View(page);
        }

        public async Task<IActionResult> NewsPage(string id)
        {
            var page = await _core.Pages.GetPublishedByUrl(id, PageCategory.News);

            if(string.IsNullOrEmpty(id))
            {
                return View("Blog", (await _core.Pages.GetBlog()).Where(x => x.Status == PageStatus.Published));
            }

            if (page != null)
            {
                page.Views += 1;
                await _core.Pages.Update(page);
            }

            ViewData["otherBlogs"] = (await _core.Pages.GetBlog())
                .Where(x => x.Id != page.Id && x.Status == PageStatus.Published)
                .OrderByDescending(x => x.Timestamp)
                .Take(6)
                .ToList();
            return page == null ? View("NotFound") : View("BlogPage", page);
        }

        public async Task<IActionResult> Blog()
        {
            return View((await _core.Pages.GetBlog()).Where(x => x.Status == PageStatus.Published));
        }

        [HttpPost]
        public async Task<IActionResult> SendFormdata(IFormCollection form)
        {
            var mailText = new StringBuilder();
            mailText.AppendLine($"New form submit from {Request.Headers["Referer"]}<br/><br/>");
            foreach(var field in form)
            {
                mailText.AppendLine($"{field.Key}: {field.Value}<br/>");
            }

            _email.SendEmail(_config["formsEmailReceiver"], "Form submit", mailText.ToString());

            await LogEvent("FormSubmit", new
            {
                url = Request.Headers["Referer"],
                form = form.Select(x => new { key = x.Key, value = x.Value.FirstOrDefault() })
            });

            if (
                form.ContainsKey("Package") &&
                form.ContainsKey("Restaurant") &&
                form.ContainsKey("Email") &&
                form.ContainsKey("Phone")
            )
            {
                await _db.CustomerRequests.Add(new CustomerRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    UpdateDate = DateTime.Now,
                    CreateDate = DateTime.Now,
                    Status = CustomerRequestStatus.New,
                    Email = form["Email"],
                    RestaurantName = form["Restaurant"],
                    Package = form["Package"],
                    Phone = form["Phone"]
                });
            }

            Response.Headers.Add("Access-Control-Allow-Origin", "*");

            return Ok();
        }
    }
}

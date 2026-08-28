// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Menus.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;

namespace Menus.Web.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class PagesController : Controller
    {
        private readonly Core _core;
        private readonly IConfiguration _configuration;

        public PagesController(Core core, IConfiguration configuration)
        {
            _core = core;
            _configuration = configuration;
        }

        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Index(string q, PageStatus? status, PageCategory? category)
        {
            IEnumerable<IPage> pages;
            if (string.IsNullOrEmpty(q) && status == null && category == null) pages = await _core.Pages.GetAll();
            else pages = await _core.Pages.Search(q, status, category);

            ViewData["q"] = q;
            ViewData["status"] = status;
            ViewData["category"] = category;

            return View(pages.Take(50));
        }

        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Content(string q, PageStatus? status)
        {
            var category = PageCategory.Content;
            IEnumerable<IPage> pages;
            if (string.IsNullOrEmpty(q) && status == null) pages = await _core.Pages.GetPages();
            else pages = await _core.Pages.Search(q, status, category);

            ViewData["q"] = q;
            ViewData["status"] = status;
            ViewData["category"] = category;

            return View(pages.Take(50));
        }

        [Authorize(Roles = "admin, manager")]
        public async Task<IActionResult> Blog(string q, PageStatus? status)
        {
            var category = PageCategory.News;
            IEnumerable<IPage> pages;
            if (string.IsNullOrEmpty(q) && status == null) pages = await _core.Pages.GetBlog();
            else pages = await _core.Pages.Search(q, status, category);

            pages = pages.OrderByDescending(x => x.Timestamp);

            ViewData["q"] = q;
            ViewData["status"] = status;
            ViewData["category"] = category;
            ViewData["seoStatus"] = GetSeoStatuses();

            return View(pages.Take(50));
        }

        private Dictionary<string, string> GetSeoStatuses()
        {
            var result = new Dictionary<string, string>();
            try
            {
                using (var connection = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();
                    using (var command = new MySqlCommand("SELECT PageId, Fixed FROM ContentOptimizationLog", connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var pageId = reader.GetString(0);
                            var fixedFlag = reader.GetBoolean(1);
                            result[pageId] = fixedFlag ? "Виправлено" : "Перевірено, без проблем";
                        }
                    }
                }
            }
            catch
            {
                // Optimization log table may not exist yet if the content bot hasn't run; treat as "not checked".
            }
            return result;
        }

        [HttpGet]
        public IActionResult Create(PageCategory type)
        {
            return View(new Page
            {
                Id = Guid.NewGuid().ToString(),
                Category = type
            });
        }

        [Authorize(Roles = "admin, manager")]
        [HttpPost]
        public async Task<IActionResult> Create(CreatePageModel model)
        {
            model.Id = Guid.NewGuid().ToString();
            var page = model.GetPage() as IPage;

            var title = new LocalizableString() as ILocalizableString;
            title.Replace(model.Title.Select(x => x as ILocalizableStringValue));
            page.Title = title;

            var shortContent = new LocalizableString() as ILocalizableString;
            shortContent.Replace(model.ShortContent.Select(x => x as ILocalizableStringValue));
            page.ShortContent = shortContent;

            var content = new LocalizableString() as ILocalizableString;
            content.Replace(model.Content.Select(x => x as ILocalizableStringValue));
            page.Content = content;

            var seoTitle = new LocalizableString() as ILocalizableString;
            seoTitle.Replace(model.SeoTitle.Select(x => x as ILocalizableStringValue));
            page.SeoTitle = seoTitle;

            var seoDescription = new LocalizableString() as ILocalizableString;
            seoDescription.Replace(model.SeoDescription.Select(x => x as ILocalizableStringValue));
            page.SeoDescription = seoDescription;

            var seoKeywords = new LocalizableString() as ILocalizableString;
            seoKeywords.Replace(model.SeoKeywords.Select(x => x as ILocalizableStringValue));
            page.SeoKeywords = seoKeywords;

            var seoText = new LocalizableString() as ILocalizableString;
            seoText.Replace(model.SeoText.Select(x => x as ILocalizableStringValue));
            page.SeoText = seoText;

            page.Timestamp = DateTime.Now;

            if (string.IsNullOrWhiteSpace(page.Url))
            {
                page.Url = await GenerateUniqueUrl(page.Title.Get());
            }

            if (page.Photo != null && page.Photo.StartsWith("data"))
            {
                page.Photo = await UploadPhoto(page.Photo);
            }

            await _core.Pages.Create(page);

            return page.Category == PageCategory.Content ? RedirectToAction("Content") : RedirectToAction("Blog");
        }

        private async Task<string> GenerateUniqueUrl(string title)
        {
            var baseUrl = Services.UrlGenerateService.Translit(title);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "page-" + Guid.NewGuid().ToString().Substring(0, 8);
            }

            var existingPages = await _core.Pages.GetAll();
            var existingUrls = existingPages.Select(x => x.Url).ToHashSet();

            var candidate = baseUrl;
            var attempt = 1;
            while (existingUrls.Contains(candidate))
            {
                candidate = $"{baseUrl}-{attempt}";
                attempt++;
            }

            return candidate;
        }

        private async Task<string> UploadPhoto(string base64)
        {
            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = Helpers.ImageHelper.GetMimeType(base64),
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            return file.Id + Helpers.ImageHelper.GetExtension(base64);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var page = await _core.Pages.GetById(id);

            return View(page);
        }

        [Authorize(Roles = "admin, manager")]
        [HttpPost]
        public async Task<IActionResult> Edit(CreatePageModel model)
        {
            var page = model.GetPage() as IPage;

            var title = new LocalizableString() as ILocalizableString;
            title.Replace(model.Title.Select(x => x as ILocalizableStringValue));
            page.Title = title;

            var shortContent = new LocalizableString() as ILocalizableString;
            shortContent.Replace(model.ShortContent.Select(x => x as ILocalizableStringValue));
            page.ShortContent = shortContent;

            var content = new LocalizableString() as ILocalizableString;
            content.Replace(model.Content.Select(x => x as ILocalizableStringValue));
            page.Content = content;

            var seoTitle = new LocalizableString() as ILocalizableString;
            seoTitle.Replace(model.SeoTitle.Select(x => x as ILocalizableStringValue));
            page.SeoTitle = seoTitle;

            var seoDescription = new LocalizableString() as ILocalizableString;
            seoDescription.Replace(model.SeoDescription.Select(x => x as ILocalizableStringValue));
            page.SeoDescription = seoDescription;

            var seoKeywords = new LocalizableString() as ILocalizableString;
            seoKeywords.Replace(model.SeoKeywords.Select(x => x as ILocalizableStringValue));
            page.SeoKeywords = seoKeywords;

            var seoText = new LocalizableString() as ILocalizableString;
            seoText.Replace(model.SeoText.Select(x => x as ILocalizableStringValue));
            page.SeoText = seoText;

            if (page.Photo != null && page.Photo.StartsWith("data"))
            {
                page.Photo = await UploadPhoto(page.Photo);
            }
            else if (page.Photo == null)
            {
                page.Photo = null;
            }
            page.Timestamp = DateTime.Now;

            if (string.IsNullOrWhiteSpace(page.Url))
            {
                page.Url = await GenerateUniqueUrl(page.Title.Get());
            }

            await _core.Pages.Update(page);

            return page.Category == PageCategory.Content ? RedirectToAction("Content") : RedirectToAction("Blog");
        }

        [HttpPost]
        public async Task<IActionResult> UploadBase64Photo(string base64)
        {
            base64 = base64.Substring(base64.IndexOf(',') + 1);
            base64 = base64.Trim('\0');
            byte[] bytes = Convert.FromBase64String(base64);
            var file = new DbFile
            {
                Data = bytes,
                Id = Guid.NewGuid().ToString(),
                Mime = Helpers.ImageHelper.GetMimeType(base64),
                Size = bytes.Length
            };

            await _core.Files.Create(file);

            return Ok(new { id = file.Id, name = file.Id + Helpers.ImageHelper.GetExtension(base64), approved = true });
        }

        [ValidateAntiForgeryToken]
        [Authorize(Roles = "admin, manager")]
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            await _core.Pages.Delete(id);
            return Ok();
        }
    }
}

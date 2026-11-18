using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // «Страницы» → «Лендинги»: правка текстів лендингів (/uk/iframe-menu тощо) без зміни коду
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class SiteTextsController : Controller
    {
        private readonly SiteTextService _texts;

        public SiteTextsController(SiteTextService texts)
        {
            _texts = texts;
        }

        // окремого розділу немає - лендинги в «Страницах»
        public IActionResult Index() => RedirectToAction("Content", "Pages");

        public async Task<IActionResult> Edit(string id)
        {
            var page = SiteTextService.Pages.FirstOrDefault(x => x.Key == id);
            if (page == null) return NotFound();
            var defaults = page.Langs.ToDictionary(l => l, l => _texts.GetDefaults(page, l));
            var overrides = new Dictionary<string, Dictionary<string, string>>();
            foreach (var l in page.Langs) overrides[l] = await _texts.GetOverrides(page.Key, l);
            ViewData["defaults"] = defaults;
            ViewData["overrides"] = overrides;
            return View(page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(string id)
        {
            var page = SiteTextService.Pages.FirstOrDefault(x => x.Key == id);
            if (page == null) return NotFound();
            foreach (var lang in page.Langs)
            {
                var values = new Dictionary<string, string>();
                foreach (var key in page.Keys)
                {
                    var field = $"{lang}.{key}";
                    if (Request.Form.ContainsKey(field)) values[key] = Request.Form[field];
                }
                await _texts.Save(page, lang, values);
            }
            TempData["message"] = "Збережено. Сторінка на сайті оновиться протягом 5-30 хвилин (кеш).";
            return RedirectToAction("Edit", new { id });
        }
    }
}

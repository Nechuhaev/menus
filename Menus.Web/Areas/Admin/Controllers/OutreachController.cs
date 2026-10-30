using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // Розсилка власникам ресторанів: шаблон листа, список закладів з контактами, відправка email,
    // Telegram/Instagram - текст для ручного надсилання
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class OutreachController : Controller
    {
        private readonly Core _core;
        private readonly OutreachService _outreach;

        public OutreachController(Core core, OutreachService outreach)
        {
            _core = core;
            _outreach = outreach;
        }

        public async Task<IActionResult> Index(string cityId, string preview)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var recipients = await _outreach.GetRecipients();
            if (!string.IsNullOrEmpty(cityId)) recipients = recipients.Where(x => x.City == cityId).ToList();
            var template = await _outreach.GetTemplate();

            // текст для кожного закладу: перегляд листа, копіювання для Telegram/Instagram
            var texts = recipients.ToDictionary(x => x.Id, x => Render(template, x, cities, null));
            ViewData["cities"] = cities;
            ViewData["cityId"] = cityId;
            ViewData["template"] = template;
            ViewData["texts"] = texts;
            ViewData["optOuts"] = await _outreach.GetOptOuts();
            ViewData["preview"] = recipients.FirstOrDefault(x => x.Id == preview) ?? recipients.FirstOrDefault();
            return View(recipients);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTemplate(string subject, string body, string cityId)
        {
            await _outreach.SaveTemplate(subject, body);
            TempData["message"] = "Шаблон збережено";
            return RedirectToAction("Index", new { cityId });
        }

        // emails - значення «restaurantId|email» з галочок у таблиці
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(List<string> emails, string cityId)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var recipients = (await _outreach.GetRecipients()).ToDictionary(x => x.Id);
            var template = await _outreach.GetTemplate();
            var letters = new List<(OutreachRecipient, string, string, string, string)>();
            foreach (var pair in emails ?? new List<string>())
            {
                var parts = pair.Split('|');
                if (parts.Length != 2 || !recipients.TryGetValue(parts[0], out var r) || !r.Emails.Contains(parts[1])) continue;
                var token = Guid.NewGuid().ToString("N");
                var (subject, body) = Render(template, r, cities, Url.Action("Unsubscribe", "Outreach",
                    new { area = "", culture = "uk", id = token }, Request.Scheme));
                letters.Add((r, parts[1], subject, body, token));
            }
            var queued = await _outreach.QueueEmails(letters);
            TempData["message"] = $"У черзі листів: {queued} (по одному на {OutreachService.IntervalSeconds} с). " +
                                  $"Пропущено (вже надіслано чи відписались): {letters.Count - queued}";
            return RedirectToAction("Index", new { cityId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manual(string restaurantId, string channel, string recipient, string cityId)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var r = (await _outreach.GetRecipients()).FirstOrDefault(x => x.Id == restaurantId);
            if (r != null && (channel == "telegram" || channel == "instagram"))
            {
                await _outreach.MarkManual(restaurantId, channel, recipient, Render(await _outreach.GetTemplate(), r, cities, null).Body);
                TempData["message"] = $"Позначено: {r.Name} - {recipient}";
            }
            return RedirectToAction("Index", new { cityId });
        }

        private (string Subject, string Body) Render(OutreachTemplate template, OutreachRecipient r,
            List<Menus.Abstract.Models.ICity> cities, string unsubscribeUrl)
        {
            var city = cities.FirstOrDefault(x => x.Id == r.City);
            return OutreachService.Render(template, r, city?.Name.Get("uk") ?? "", city?.Name.Get("en")?.ToLower() ?? "kyiv",
                $"{Request.Scheme}://{Request.Host}", unsubscribeUrl);
        }
    }
}

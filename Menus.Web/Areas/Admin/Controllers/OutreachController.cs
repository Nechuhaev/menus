using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Areas.Admin.Controllers
{
    // Розсилка власникам ресторанів: список закладів (сторінками), картка закладу (контакти, відправка, історія),
    // шаблони листів з назвами
    [Area("Admin")]
    [Authorize(Roles = "admin")]
    public class OutreachController : Controller
    {
        private readonly Core _core;
        private readonly OutreachService _outreach;
        private readonly TelegramOwnersService _telegram;

        public OutreachController(Core core, OutreachService outreach, TelegramOwnersService telegram)
        {
            _core = core;
            _outreach = outreach;
            _telegram = telegram;
        }

        // filter: contacts - є контакти (за замовчуванням), new - є контакти й ще не писали, sent - уже писали, all - усі
        public async Task<IActionResult> Index(string q, string cityId, string filter = "contacts", int page = 1)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            IEnumerable<OutreachRecipient> list = await _outreach.GetRestaurants();
            if (!string.IsNullOrWhiteSpace(q)) list = list.Where(x => (x.Name ?? "").IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(cityId)) list = list.Where(x => x.City == cityId);
            switch (filter)
            {
                case "new": list = list.Where(x => x.Contacts.Any() && x.LastSent == null); break;
                case "sent": list = list.Where(x => x.LastSent != null); break;
                case "all": break;
                default: list = list.Where(x => x.Contacts.Any()); break;
            }
            var filtered = list.ToList();
            var pages = Math.Max(1, (filtered.Count + OutreachService.PageSize - 1) / OutreachService.PageSize);
            page = Math.Min(Math.Max(1, page), pages);

            ViewData["cities"] = cities;
            ViewData["q"] = q;
            ViewData["cityId"] = cityId;
            ViewData["filter"] = filter;
            ViewData["page"] = page;
            ViewData["pages"] = pages;
            ViewData["total"] = filtered.Count;
            ViewData["templates"] = await _outreach.GetTemplates();
            ViewData["optOuts"] = await _outreach.GetOptOuts();
            return View(filtered.Skip((page - 1) * OutreachService.PageSize).Take(OutreachService.PageSize).ToList());
        }

        public async Task<IActionResult> Restaurant(string id, int? templateId)
        {
            var r = await _outreach.GetRestaurant(id);
            if (r == null) return NotFound();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var template = await _outreach.GetTemplate(templateId);
            ViewData["templates"] = await _outreach.GetTemplates();
            ViewData["template"] = template;
            ViewData["text"] = Render(template, r, cities, null);
            ViewData["cityName"] = cities.FirstOrDefault(x => x.Id == r.City)?.Name.Get("uk");
            ViewData["optOuts"] = await _outreach.GetOptOuts();
            ViewData["telegramLinks"] = await _telegram.GetLinks(id);
            ViewData["telegramStart"] = _telegram.StartLink(id);
            ViewData["telegramConfigured"] = _telegram.IsConfigured;
            return View(r);
        }

        // Повідомлення в Telegram власнику, що підключився до бота
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTelegram(string id, int templateId, long chatId)
        {
            var r = await _outreach.GetRestaurant(id);
            var link = (await _telegram.GetLinks(id)).FirstOrDefault(x => x.ChatId == chatId);
            if (r == null || link == null) return NotFound();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var body = Render(await _outreach.GetTemplate(templateId), r, cities, null).Body;
            var error = await _telegram.SendMessage(chatId, body);
            await _outreach.LogTelegram(id, link.Title, body, error == null ? "sent" : "failed", templateId, error);
            TempData["message"] = error == null ? $"Надіслано в Telegram: {link.Title}" : $"Telegram не надіслав ({link.Title}): {error}";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlinkTelegram(string id, long chatId, int? templateId)
        {
            await _telegram.Unlink(id, chatId);
            TempData["message"] = "Telegram відключено від ресторану";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        // Один раз після налаштування токена: Telegram надсилатиме повідомлення боту на сайт
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetTelegramWebhook()
        {
            var url = Url.Action("Webhook", "TelegramOwners", new { area = "", culture = "uk" }, "https");
            var error = _telegram.IsConfigured ? await _telegram.SetWebhook(url) : "немає TelegramOwnersBot__Token у /root/menus.env";
            TempData["message"] = error == null ? $"Вебхук бота підключено: {url}" : $"Вебхук не підключено: {error}";
            return RedirectToAction("Templates");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(string id, int templateId, string email)
        {
            var r = await _outreach.GetRestaurant(id);
            if (r == null || !r.Emails.Contains(email)) return NotFound();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var token = Guid.NewGuid().ToString("N");
            var (subject, body) = Render(await _outreach.GetTemplate(templateId), r, cities, UnsubscribeUrl(token));
            var error = await _outreach.SendEmailNow(id, templateId, email, subject, body, token);
            TempData["message"] = error == null ? $"Лист надіслано: {email}" : $"Не надіслано ({email}): {error}";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manual(string id, int templateId, string channel, string recipient)
        {
            var r = await _outreach.GetRestaurant(id);
            if (r == null || !r.Values(channel).Contains(recipient)) return NotFound();
            var cities = (await _core.Restaurants.GetCities()).ToList();
            await _outreach.MarkManual(id, templateId, channel, recipient, Render(await _outreach.GetTemplate(templateId), r, cities, null).Body);
            TempData["message"] = $"Позначено надісланим: {recipient}";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddContact(string id, string kind, string value, int? templateId)
        {
            var error = await _outreach.AddContact(id, kind, value);
            TempData["message"] = error ?? "Контакт додано";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateContact(string id, int contactId, string value, int? templateId)
        {
            var error = await _outreach.UpdateContact(id, contactId, value);
            TempData["message"] = error ?? "Контакт змінено";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteContact(string id, int contactId, int? templateId)
        {
            await _outreach.HideContact(id, contactId);
            TempData["message"] = "Контакт видалено";
            return RedirectToAction("Restaurant", new { id, templateId });
        }

        // Масово зі списку: emails - значення «restaurantId|email» з галочок
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(List<string> emails, int templateId, string q, string cityId, string filter, int page = 1)
        {
            var cities = (await _core.Restaurants.GetCities()).ToList();
            var restaurants = (await _outreach.GetRestaurants()).ToDictionary(x => x.Id);
            var template = await _outreach.GetTemplate(templateId);
            var letters = new List<(OutreachRecipient, string, string, string, string)>();
            foreach (var pair in emails ?? new List<string>())
            {
                var parts = pair.Split('|');
                if (parts.Length != 2 || !restaurants.TryGetValue(parts[0], out var r) || !r.Emails.Contains(parts[1])) continue;
                var token = Guid.NewGuid().ToString("N");
                var (subject, body) = Render(template, r, cities, UnsubscribeUrl(token));
                letters.Add((r, parts[1], subject, body, token));
            }
            var queued = await _outreach.QueueEmails(letters, template.Id);
            TempData["message"] = $"«{template.Name}»: у черзі листів {queued} (по одному на {OutreachService.IntervalSeconds} с). " +
                                  $"Пропущено (вже надіслано чи відписались): {letters.Count - queued}";
            return RedirectToAction("Index", new { q, cityId, filter, page });
        }

        public async Task<IActionResult> Templates()
        {
            ViewData["telegramConfigured"] = _telegram.IsConfigured;
            ViewData["telegramBot"] = _telegram.BotName;
            return View(await _outreach.GetTemplates());
        }

        [HttpGet]
        public async Task<IActionResult> EditTemplate(int? id)
        {
            var template = id == null ? new OutreachTemplate { Name = "", Subject = "", Body = "" } : (await _outreach.GetTemplates()).FirstOrDefault(x => x.Id == id);
            if (template == null) return NotFound();
            return View(template);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTemplate(int? id, string name, string subject, string body)
        {
            await _outreach.SaveTemplate(id, name, subject, body);
            TempData["message"] = "Шаблон збережено";
            return RedirectToAction("Templates");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTemplate(int id)
        {
            await _outreach.DeleteTemplate(id);
            TempData["message"] = "Шаблон видалено";
            return RedirectToAction("Templates");
        }

        private string UnsubscribeUrl(string token) =>
            Url.Action("Unsubscribe", "Outreach", new { area = "", culture = "uk", id = token }, Request.Scheme);

        private (string Subject, string Body) Render(OutreachTemplate template, OutreachRecipient r, List<ICity> cities, string unsubscribeUrl)
        {
            var city = cities.FirstOrDefault(x => x.Id == r.City);
            return OutreachService.Render(template, r, city?.Name.Get("uk") ?? "", city?.Name.Get("en")?.ToLower() ?? "kyiv",
                $"{Request.Scheme}://{Request.Host}", unsubscribeUrl, _telegram.StartLink(r.Id));
        }
    }
}

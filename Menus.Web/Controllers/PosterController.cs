using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Menus.Domain;
using Menus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Controllers
{
    // Підключення каси Poster: /uk/poster/connect (кнопка в кабінеті або «Під'єднати» в каталозі Poster) -> OAuth Poster ->
    // /uk/poster/callback (токен, вибір точки продажу) -> кабінет. /uk/poster/webhook - зміни меню в касі.
    public class PosterController : Controller
    {
        private const string RestaurantCookie = "poster_rid";
        private readonly PosterService _poster;
        private readonly UserService _users;
        private readonly Core _core;

        public PosterController(PosterService poster, UserService users, Core core)
        {
            _poster = poster;
            _users = users;
            _core = core;
        }

        private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

        private async Task<string> AllowedRestaurant(string restaurantId)
        {
            if (string.IsNullOrEmpty(restaurantId) || User?.Identity?.IsAuthenticated != true) return null;
            if (User.IsInRole("admin") || User.IsInRole("manager")) return restaurantId;
            return await _core.Restaurants.IsUserAllowedToEdit(restaurantId, User.Identity.Name) ? restaurantId : null;
        }

        // кнопка в кабінеті або посилання з каталогу Poster; без закладу в кабінеті - підключення з заявкою (Install)
        public async Task<IActionResult> Connect()
        {
            if (!_poster.IsConfigured) return Content("Інтеграцію з Poster ще не налаштовано на сервері.");
            var restaurant = User?.Identity?.IsAuthenticated == true ? await _users.GetSelecedRestaurant(User, Request) : null;
            var id = await AllowedRestaurant(restaurant?.Id);
            if (id == null)
            {
                Response.Cookies.Delete(RestaurantCookie);
                return Redirect(_poster.AuthorizeUrl(BaseUrl));
            }
            Response.Cookies.Append(RestaurantCookie, id, new CookieOptions { IsEssential = true, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(20) });
            return Redirect(_poster.AuthorizeUrl(BaseUrl));
        }

        // кабінет (кука poster_rid) або каталог Poster (без куки, часто без входу на TipMyMenu)
        public async Task<IActionResult> Callback(string code, string account)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(account))
                return Fail("Poster не передав код підключення - встановіть застосунок ще раз.");
            var (token, error) = await _poster.ExchangeCode(account, code, BaseUrl);
            if (token == null) return Fail("Poster не підтвердив підключення: " + error);
            var id = await AllowedRestaurant(Request.Cookies[RestaurantCookie]);
            if (id == null && User?.Identity?.IsAuthenticated == true && !User.IsInRole("admin") && !User.IsInRole("manager"))
            {
                // власник закладу встановив застосунок з каталогу Poster - його заклад у кабінеті
                var restaurant = await _users.GetSelecedRestaurant(User, Request);
                id = await AllowedRestaurant(restaurant?.Id);
                if (id != null)
                    Response.Cookies.Append(RestaurantCookie, id, new CookieOptions { IsEssential = true, HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(20) });
            }
            var spots = await _poster.Spots(token);
            TempData["posterToken"] = token;
            TempData["posterAccount"] = account;
            if (id == null)
            {
                // закладу на TipMyMenu ще немає: форма-заявка (назва, місто, контакт) - адмін створює сторінку й прив'язує Poster
                ViewData["spots"] = spots;
                ViewData["account"] = account;
                return View("Install");
            }
            if (spots.Count > 1)
            {
                // кілька точок продажу - власник обирає, яка з них цей заклад (токен до вибору - у зашифрованому TempData)
                ViewData["spots"] = spots;
                return View("SelectSpot");
            }
            TempData.Remove("posterToken");
            TempData.Remove("posterAccount");
            var spot = spots.FirstOrDefault();
            await _poster.Save(id, account, token, spot.Id, spot.Name);
            Response.Cookies.Delete(RestaurantCookie);
            TempData["posterMessage"] = "Poster підключено. Меню з каси з'явиться на сайті після найближчої синхронізації (зазвичай уночі).";
            return Redirect("/uk/restaurants/about#poster");
        }

        private IActionResult Fail(string message)
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                TempData["posterMessage"] = message;
                return Redirect("/uk/restaurants/about#poster");
            }
            ViewData["error"] = message;
            return View("Install");
        }

        // заявка з каталогу Poster: заклад, місто, контакт; точка - якщо їх кілька
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Install(string spot, string venue, string city, string contact, string email)
        {
            var token = TempData["posterToken"] as string;
            var account = TempData["posterAccount"] as string;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account))
                return Fail("Час заповнення форми минув - встановіть застосунок TipMyMenu в Poster ще раз.");
            var spots = await _poster.Spots(token);
            var chosen = spots.FirstOrDefault(x => x.Id == spot);
            if (chosen.Id == null && spots.Count == 1) chosen = spots[0];
            if (string.IsNullOrWhiteSpace(venue) || string.IsNullOrWhiteSpace(contact) || (spots.Count > 1 && chosen.Id == null))
            {
                TempData.Keep("posterToken");
                TempData.Keep("posterAccount");
                ViewData["spots"] = spots;
                ViewData["account"] = account;
                ViewData["error"] = spots.Count > 1 && chosen.Id == null ? "Оберіть точку продажу." : "Вкажіть назву закладу й телефон або Telegram.";
                ViewData["form"] = (venue, city, contact, email);
                return View("Install");
            }
            var title = venue.Trim() + (string.IsNullOrWhiteSpace(city) ? "" : ", " + city.Trim());
            await _poster.SavePending(account, token, chosen.Id, chosen.Name, title, contact, email);
            ViewData["done"] = true;
            ViewData["account"] = account;
            return View("Install");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectSpot(string spotId, string spotName)
        {
            var id = await AllowedRestaurant(Request.Cookies[RestaurantCookie]);
            var token = TempData["posterToken"] as string;
            var account = TempData["posterAccount"] as string;
            if (id == null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(account))
            {
                TempData["posterMessage"] = "Час вибору минув - підключіть Poster ще раз.";
                return Redirect("/uk/restaurants/about#poster");
            }
            await _poster.Save(id, account, token, spotId, spotName);
            Response.Cookies.Delete(RestaurantCookie);
            TempData["posterMessage"] = "Poster підключено. Меню з каси з'явиться на сайті після найближчої синхронізації (зазвичай уночі).";
            return Redirect("/uk/restaurants/about#poster");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Disconnect()
        {
            var restaurant = await _users.GetSelecedRestaurant(User, Request);
            var id = await AllowedRestaurant(restaurant?.Id);
            if (id != null)
            {
                var restored = await _poster.Disconnect(id);
                TempData["posterMessage"] = restored
                    ? "Poster відключено. Меню знову оновлюватиметься з попереднього джерела, як до підключення."
                    : "Poster відключено. Меню на сайті лишається, але більше не оновлюється з каси.";
            }
            return Redirect("/uk/restaurants/about#poster");
        }

        // Poster надсилає POST при зміні сутностей; відповідь 200 завжди, інакше Poster повторює до 15 разів
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook()
        {
            try
            {
                string body;
                using (var reader = new StreamReader(Request.Body))
                {
                    body = await reader.ReadToEndAsync();
                }
                JObject hook = null;
                if (body.TrimStart().StartsWith("{")) hook = JObject.Parse(body);
                else if (Request.HasFormContentType)
                {
                    hook = new JObject();
                    foreach (var kv in Request.Form) hook[kv.Key] = kv.Value.ToString();
                }
                if (hook != null) await _poster.HandleWebhook(hook);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Poster webhook failed: {ex.Message}");
            }
            return Ok();
        }
    }
}

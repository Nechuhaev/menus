using System.Net;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Controllers
{
    // Відписка від розсилки власникам ресторанів (посилання в кожному листі). Відкриття посилання лише питає -
    // відписує кнопка (POST): поштові сканери відкривають посилання з листів самі
    public class OutreachController : Controller
    {
        private readonly OutreachService _outreach;

        public OutreachController(OutreachService outreach)
        {
            _outreach = outreach;
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Unsubscribe(string id) =>
            Page("<form method=\"post\"><p>Більше не отримувати листів від TipMyMenu на цю адресу?</p>" +
                 "<button type=\"submit\" style=\"padding:10px 18px;font-size:16px\">Відписатися</button></form>");

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Unsubscribe(string id, bool confirm = true)
        {
            var email = string.IsNullOrEmpty(id) ? null : await _outreach.Unsubscribe(id);
            return Page(email == null
                ? "<p>Посилання недійсне.</p>"
                : $"<p>Готово: на адресу {WebUtility.HtmlEncode(email)} листів від TipMyMenu більше не буде.</p>");
        }

        private IActionResult Page(string body)
        {
            Response.Headers["X-Robots-Tag"] = "noindex";
            return Content("<!doctype html><html lang=\"uk\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\">" +
                           "<title>TipMyMenu</title></head><body style=\"font-family:Arial,sans-serif;padding:24px\">" + body +
                           "<p><a href=\"/uk\">tipmymenu.com</a></p></body></html>", "text/html; charset=utf-8");
        }
    }
}

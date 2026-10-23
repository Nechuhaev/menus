using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Menus.Web.Controllers
{
    // Свіжий антифорджері-токен для сторінок, які Cloudflare віддає з кешу (EdgeCacheAttribute).
    // Заодно ставить куки токена й DeviceId, яких закешована сторінка не містить
    public class AntiforgeryController : Controller
    {
        private readonly IAntiforgery _antiforgery;

        public AntiforgeryController(IAntiforgery antiforgery)
        {
            _antiforgery = antiforgery;
        }

        [HttpGet("/antiforgery/token")]
        public IActionResult Token()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            Response.Headers["Cache-Control"] = "no-cache, no-store";
            return Json(new { token = tokens.RequestToken });
        }
    }
}

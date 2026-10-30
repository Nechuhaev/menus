using System.IO;
using System.Threading.Tasks;
using Menus.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Controllers
{
    // Вебхук бота власників (@TipMyMenuOwnersBot): Telegram надсилає сюди повідомлення боту.
    // Підключається кнопкою в адмінці («Розсилка» -> «Шаблони»); чужі запити відсікає секретний заголовок
    public class TelegramOwnersController : Controller
    {
        private readonly TelegramOwnersService _telegram;
        private readonly OutreachService _outreach;

        public TelegramOwnersController(TelegramOwnersService telegram, OutreachService outreach)
        {
            _telegram = telegram;
            _outreach = outreach;
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Webhook()
        {
            if (!_telegram.IsConfigured || Request.Headers["X-Telegram-Bot-Api-Secret-Token"] != _telegram.WebhookSecret)
                return Unauthorized();
            string json;
            using (var reader = new StreamReader(Request.Body))
            {
                json = await reader.ReadToEndAsync();
            }
            try
            {
                await _telegram.HandleUpdate(JObject.Parse(json), _outreach);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return BadRequest();
            }
            // 200 навіть якщо оновлення не стосується нас: інакше Telegram повторюватиме його
            return Ok();
        }
    }
}

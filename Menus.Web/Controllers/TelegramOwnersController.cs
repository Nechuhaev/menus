using System;
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
            TelegramOwnersService.LastWebhookAt = DateTime.UtcNow;
            if (!_telegram.IsConfigured || Request.Headers["X-Telegram-Bot-Api-Secret-Token"] != _telegram.WebhookSecret)
            {
                TelegramOwnersService.LastWebhookResult = "відхилено: не збігся секретний заголовок (натисніть «Підключити вебхук бота» ще раз)";
                return Unauthorized();
            }
            try
            {
                string json;
                using (var reader = new StreamReader(Request.Body))
                {
                    json = await reader.ReadToEndAsync();
                }
                var replyError = await _telegram.HandleUpdate(JObject.Parse(json), _outreach);
                TelegramOwnersService.LastWebhookResult = replyError == null ? "оброблено" : "оброблено, але бот не відповів: " + replyError;
            }
            catch (Exception ex)
            {
                // 200 і при помилці: інакше Telegram повторюватиме те саме оновлення без кінця
                TelegramOwnersService.LastWebhookResult = "помилка: " + ex.Message;
                Console.WriteLine($"TelegramOwners webhook FAILED: {ex}");
            }
            return Ok();
        }
    }
}

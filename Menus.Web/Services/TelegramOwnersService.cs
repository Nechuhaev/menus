using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Services
{
    public class TelegramOwnerLink
    {
        public string RestaurantId { get; set; }
        public long ChatId { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public DateTime LinkedAt { get; set; }
        public string Title => !string.IsNullOrEmpty(Username) ? "@" + Username : !string.IsNullOrEmpty(FirstName) ? FirstName : ChatId.ToString();
    }

    // Бот власників (@TipMyMenuOwnersBot): бот не може написати людині першим, тому власник один раз
    // натискає Start за персональним посиланням ресторану (t.me/<бот>?start=<код>) - бот запам'ятовує чат.
    // Далі адмін надсилає повідомлення з картки ресторану, а відповіді власника пишуться в історію розсилки.
    // Токен - TelegramOwnersBot__Token у /root/menus.env
    public class TelegramOwnersService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS OutreachTelegramLinks (
                RestaurantId VARCHAR(255) NOT NULL,
                ChatId BIGINT NOT NULL,
                Username VARCHAR(255) NULL,
                FirstName VARCHAR(255) NULL,
                LinkedAt DATETIME NOT NULL,
                PRIMARY KEY (RestaurantId, ChatId)
            ) DEFAULT CHARSET=utf8mb4";

        private readonly IDbConnection _connection;
        private readonly string _token;

        public string BotName { get; }
        public bool IsConfigured => !string.IsNullOrEmpty(_token);

        public TelegramOwnersService(EfDbContext dbContext, IConfiguration configuration)
        {
            _connection = dbContext.Database.GetDbConnection();
            _token = configuration["TelegramOwnersBot:Token"];
            BotName = configuration["TelegramOwnersBot:Name"] ?? "TipMyMenuOwnersBot";
        }

        // --- коди підключення ---

        // «r<id без дефісів>_<підпис>»: підпис (HMAC токеном бота) не дає підключитися до чужого ресторану, перебравши id
        public string StartCode(string restaurantId)
        {
            var id = (restaurantId ?? "").Replace("-", "");
            return $"r{id}_{Sign(id)}";
        }

        public string StartLink(string restaurantId) => IsConfigured ? $"https://t.me/{BotName}?start={StartCode(restaurantId)}" : "";

        private string ParseStartCode(string code)
        {
            var parts = (code ?? "").Trim().Split('_');
            if (parts.Length != 2 || !parts[0].StartsWith("r") || parts[0].Length != 33) return null;
            var id = parts[0].Substring(1);
            if (!CryptographicEquals(Sign(id), parts[1])) return null;
            return Guid.TryParseExact(id, "N", out var guid) ? guid.ToString() : null;
        }

        private string Sign(string value)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_token ?? "")))
            {
                return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            }
        }

        // Заголовок X-Telegram-Bot-Api-Secret-Token: вебхук приймає лише запити від Telegram
        public string WebhookSecret
        {
            get
            {
                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("webhook:" + _token))).Replace("-", "").Substring(0, 40);
                }
            }
        }

        private static bool CryptographicEquals(string a, string b) =>
            a != null && b != null && a.Length == b.Length && a.Zip(b, (x, y) => x ^ y).Aggregate(0, (acc, v) => acc | v) == 0;

        // --- зв'язки чат <-> ресторан ---

        public async Task<List<TelegramOwnerLink>> GetLinks(string restaurantId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return (await _connection.QueryAsync<TelegramOwnerLink>(
                "SELECT CONCAT(RestaurantId, '') AS RestaurantId, ChatId, Username, FirstName, LinkedAt FROM OutreachTelegramLinks WHERE RestaurantId = @restaurantId ORDER BY LinkedAt",
                new { restaurantId })).ToList();
        }

        public async Task<HashSet<string>> GetLinkedRestaurants()
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return new HashSet<string>(await _connection.QueryAsync<string>("SELECT DISTINCT CONCAT(RestaurantId, '') FROM OutreachTelegramLinks"));
        }

        public async Task Unlink(string restaurantId, long chatId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            await _connection.ExecuteAsync("DELETE FROM OutreachTelegramLinks WHERE RestaurantId = @restaurantId AND ChatId = @chatId", new { restaurantId, chatId });
        }

        // --- Bot API ---

        public async Task<string> SendMessage(long chatId, string text)
        {
            if (!IsConfigured) return "бот не налаштований (TelegramOwnersBot__Token)";
            if (text.Length > 4000) text = text.Substring(0, 4000);
            return await Call("sendMessage", new { chat_id = chatId, text, disable_web_page_preview = true });
        }

        public async Task<string> SetWebhook(string url) =>
            await Call("setWebhook", new { url, secret_token = WebhookSecret, allowed_updates = new[] { "message" }, drop_pending_updates = true });

        // null - успіх, інакше опис помилки від Telegram
        private async Task<string> Call(string method, object payload)
        {
            try
            {
                var response = await Http.PostAsync($"https://api.telegram.org/bot{_token}/{method}",
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                return body.Value<bool?>("ok") == true ? null : body.Value<string>("description") ?? response.StatusCode.ToString();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // --- вебхук ---

        // Повідомлення боту: /start <код> - підключення до ресторану; інше - відповідь власника в історію розсилки
        public async Task HandleUpdate(JObject update, OutreachService outreach)
        {
            var message = update["message"] as JObject;
            var chatId = message?["chat"]?.Value<long?>("id");
            if (message == null || chatId == null || message["chat"]?.Value<string>("type") != "private") return;
            var from = message["from"] as JObject;
            var username = from?.Value<string>("username");
            var firstName = from?.Value<string>("first_name");
            var text = message.Value<string>("text") ?? "";
            await _connection.ExecuteAsync(CreateTableSql);

            if (text.StartsWith("/start"))
            {
                var restaurantId = ParseStartCode(text.Substring(6));
                var name = restaurantId == null ? null : await _connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT Name FROM Restaurants WHERE Id = @restaurantId", new { restaurantId });
                if (name == null)
                {
                    await SendMessage(chatId.Value, "Вітаємо! Це бот TipMyMenu для власників закладів. Щоб підключитися, відкрийте персональне посилання з нашого листа.");
                    return;
                }
                await _connection.ExecuteAsync(@"INSERT INTO OutreachTelegramLinks (RestaurantId, ChatId, Username, FirstName, LinkedAt)
                    VALUES (@restaurantId, @chatId, @username, @firstName, NOW())
                    ON DUPLICATE KEY UPDATE Username = VALUES(Username), FirstName = VALUES(FirstName)",
                    new { restaurantId, chatId, username, firstName });
                await outreach.LogTelegram(restaurantId, Title(chatId.Value, username, firstName), "підключився до бота", "received");
                await SendMessage(chatId.Value, $"Дякуємо! Telegram підключено до «{name}» на TipMyMenu. Сюди ми напишемо щодо меню закладу, а ви можете відповідати прямо тут.");
                return;
            }

            var restaurants = (await _connection.QueryAsync<string>(
                "SELECT CONCAT(RestaurantId, '') FROM OutreachTelegramLinks WHERE ChatId = @chatId", new { chatId })).ToList();
            foreach (var restaurantId in restaurants)
                await outreach.LogTelegram(restaurantId, Title(chatId.Value, username, firstName), text, "received");
            if (restaurants.Any()) await SendMessage(chatId.Value, "Дякуємо, повідомлення отримали - відповімо найближчим часом.");
        }

        private static string Title(long chatId, string username, string firstName) =>
            !string.IsNullOrEmpty(username) ? "@" + username : !string.IsNullOrEmpty(firstName) ? firstName : chatId.ToString();
    }
}

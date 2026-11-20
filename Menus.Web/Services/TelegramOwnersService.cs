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

        // Останній запит Telegram на вебхук (до перезапуску сайту) - діагностика на сторінці шаблонів
        public static DateTime? LastWebhookAt;
        public static string LastWebhookResult;

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

        // Стан вебхука з боку Telegram: адреса, запити в черзі, остання помилка доставки
        public async Task<JObject> GetWebhookInfo()
        {
            if (!IsConfigured) return null;
            try
            {
                var body = JObject.Parse(await Http.GetStringAsync($"https://api.telegram.org/bot{_token}/getWebhookInfo"));
                return body["result"] as JObject;
            }
            catch (Exception ex)
            {
                return new JObject { ["last_error_message"] = "getWebhookInfo: " + ex.Message };
            }
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
        // Повертає помилку відповіді бота (null - відповів або відповідати не треба)
        public async Task<string> HandleUpdate(JObject update, OutreachService outreach, DishStopService stops = null, Menus.Abstract.ITaskRunner runner = null)
        {
            var message = update["message"] as JObject;
            var chatId = message?["chat"]?.Value<long?>("id");
            if (message == null || chatId == null || message["chat"]?.Value<string>("type") != "private") return null;
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
                if (name == null && stops != null && (await LinkedRestaurants(chatId.Value)).Any())
                {
                    return await SendMessage(chatId.Value, HelpText);
                }
                if (name == null)
                {
                    // прийшов з сайту (лендинги «Для власників», «Меню для сайту») - заявка в «Запити», щоб не загубити контакт
                    await SaveLead(chatId.Value, username, firstName, "натиснув Start у боті");
                    return await SendMessage(chatId.Value, "Вітаємо! Це бот TipMyMenu для власників закладів. Напишіть назву закладу й місто - " +
                        "ми безкоштовно підключимо меню, QR-код на стіл і меню для вашого сайту. Якщо отримали від нас лист - відкрийте персональне посилання з нього.");
                }
                await _connection.ExecuteAsync(@"INSERT INTO OutreachTelegramLinks (RestaurantId, ChatId, Username, FirstName, LinkedAt)
                    VALUES (@restaurantId, @chatId, @username, @firstName, NOW())
                    ON DUPLICATE KEY UPDATE Username = VALUES(Username), FirstName = VALUES(FirstName)",
                    new { restaurantId, chatId, username, firstName });
                await outreach.LogTelegram(restaurantId, Title(chatId.Value, username, firstName), "підключився до бота", "received");
                return await SendMessage(chatId.Value, $"Дякуємо! Telegram підключено до «{name}» на TipMyMenu. Сюди ми напишемо щодо меню закладу, а ви можете відповідати прямо тут.");
            }

            var restaurants = await LinkedRestaurants(chatId.Value);
            foreach (var restaurantId in restaurants)
                await outreach.LogTelegram(restaurantId, Title(chatId.Value, username, firstName), text, "received");
            // стоп-лист і ціни: команди лише з чату, підключеного до закладу персональним посиланням
            if (restaurants.Any() && stops != null)
            {
                var reply = await HandleMenuCommand(chatId.Value, restaurants, text.Trim(), stops, runner);
                if (reply != null)
                {
                    foreach (var restaurantId in restaurants)
                        await outreach.LogTelegram(restaurantId, "бот", reply, "sent");
                    return await SendMessage(chatId.Value, reply);
                }
            }
            if (!restaurants.Any())
            {
                // не підключений до закладу: повідомлення - заявка в «Запити» (адмін відповідає в Telegram за @username)
                await SaveLead(chatId.Value, username, firstName, text);
            }
            return await SendMessage(chatId.Value, "Дякуємо, повідомлення отримали - відповімо найближчим часом.");
        }

        private async Task<List<string>> LinkedRestaurants(long chatId) =>
            (await _connection.QueryAsync<string>("SELECT CONCAT(RestaurantId, '') FROM OutreachTelegramLinks WHERE ChatId = @chatId", new { chatId })).ToList();

        private const string HelpText = "Команди для меню закладу:\n" +
            "/stop назва - «Немає в наявності» до 6:00 ранку\n" +
            "/back назва - повернути страву в меню\n" +
            "/price назва 185 - нова ціна\n" +
            "/stoplist - що зараз у стоп-листі\n" +
            "Назву можна писати частково: «/stop борщ».";

        private class PendingCommand
        {
            public string Command;
            public decimal? Price;
            public List<(string Id, string RestaurantId, string Name)> Options;
            public DateTime At;
        }

        // вибір страви номером, якщо назва підійшла кільком
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, PendingCommand> Pending =
            new System.Collections.Concurrent.ConcurrentDictionary<long, PendingCommand>();

        private static string Norm(string v) => System.Text.RegularExpressions.Regex.Replace((v ?? "").ToLowerInvariant().Replace("ё", "е"), @"['’ʼ`""«»]", "").Trim();

        // null - не команда (звичайне повідомлення власника)
        private async Task<string> HandleMenuCommand(long chatId, List<string> restaurants, string text, DishStopService stops, Menus.Abstract.ITaskRunner runner)
        {
            if (Pending.TryGetValue(chatId, out var pending) && DateTime.UtcNow - pending.At < TimeSpan.FromMinutes(10) &&
                int.TryParse(text, out var number) && number >= 1 && number <= pending.Options.Count)
            {
                Pending.TryRemove(chatId, out _);
                return await Apply(pending.Command, pending.Options[number - 1], pending.Price, stops, runner);
            }
            var lower = text.ToLowerInvariant();
            if (lower == "/help" || lower == "/menu") return HelpText;
            if (lower.StartsWith("/stoplist"))
            {
                var lines = new List<string>();
                var dishes = await LoadDishes(restaurants);
                foreach (var r in restaurants)
                {
                    var active = await stops.Active(r);
                    lines.AddRange(dishes.Where(d => active.Contains(d.Id)).Select(d => "• " + d.Name));
                }
                return lines.Any() ? "Зараз у стоп-листі:\n" + string.Join("\n", lines) : "Стоп-лист порожній: усі страви в меню.";
            }
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^/(stop|back|price)(?:@\w+)?\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return text.StartsWith("/") ? HelpText : null;
            var command = m.Groups[1].Value.ToLowerInvariant();
            var query = m.Groups[2].Value.Trim();
            decimal? price = null;
            if (command == "price")
            {
                var pm = System.Text.RegularExpressions.Regex.Match(query, @"^(.+?)\s+(\d+(?:[.,]\d{1,2})?)\s*(?:грн|₴)?$");
                if (!pm.Success) return "Формат: /price назва 185";
                query = pm.Groups[1].Value.Trim();
                price = decimal.Parse(pm.Groups[2].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
                if (price <= 0 || price > 100000) return "Ціна має бути від 1 до 100000.";
            }
            var q = Norm(query);
            var all = await LoadDishes(restaurants);
            var exact = all.Where(d => Norm(d.Name) == q).ToList();
            var found = exact.Any() ? exact : all.Where(d => Norm(d.Name).Contains(q)).ToList();
            if (!found.Any()) return $"Не знайшов страву «{query}» у меню. Напишіть частину назви, наприклад «/{command} борщ».";
            if (found.Count == 1) return await Apply(command, found[0], price, stops, runner);
            if (found.Count > 10) return $"Під «{query}» підходить {found.Count} страв - уточніть назву.";
            Pending[chatId] = new PendingCommand { Command = command, Price = price, Options = found, At = DateTime.UtcNow };
            return "Яку саме? Надішліть номер:\n" + string.Join("\n", found.Select((d, i) => $"{i + 1}. {d.Name}"));
        }

        private async Task<List<(string Id, string RestaurantId, string Name)>> LoadDishes(List<string> restaurants) =>
            (await _connection.QueryAsync<(string Id, string RestaurantId, string Name)>(
                "SELECT CONCAT(Id, ''), CONCAT(RestaurantId, ''), Name FROM Dishes WHERE RestaurantId IN @restaurants AND Status = 0",
                new { restaurants }))
            .Select(d => (d.Id, d.RestaurantId, Name: Menus.Web.Models.LocalizableString.FromJson(d.Name).Get("uk", true)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Name)).ToList();

        private async Task<string> Apply(string command, (string Id, string RestaurantId, string Name) dish, decimal? price, DishStopService stops, Menus.Abstract.ITaskRunner runner)
        {
            string reply;
            if (command == "stop")
            {
                await stops.Stop(dish.Id, dish.RestaurantId, DishStopService.NextMorningUtc(), "telegram");
                reply = $"«{dish.Name}» - у стоп-листі до 6:00 ранку. Повернути раніше: /back {dish.Name}";
            }
            else if (command == "back")
            {
                await stops.Back(dish.Id);
                reply = $"«{dish.Name}» знову в меню.";
            }
            else
            {
                await _connection.ExecuteAsync("UPDATE Dishes SET Price = @price WHERE Id = @id", new { price, id = dish.Id });
                reply = $"Ціна «{dish.Name}»: {price.Value:0.##} ₴.";
            }
            runner?.RunUpdateRestaurantIframeCache(dish.RestaurantId);
            return reply + " На сайті оновиться протягом 30 хвилин.";
        }

        // заявка в адмінку «Запити» (таблиця CustomerRequests): текст - у полі «Заклад», контакт Telegram - у «Телефон»
        private Task SaveLead(long chatId, string username, string firstName, string text) =>
            _connection.ExecuteAsync(@"INSERT INTO CustomerRequests (Id, RestaurantName, Email, Phone, Package, Status, CreateDate, UpdateDate)
                VALUES (@id, @text, '', @contact, 'Telegram-бот', 0, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                new
                {
                    id = Guid.NewGuid().ToString(),
                    text = Cut(text),
                    contact = Cut("Telegram " + Title(chatId, username, firstName) + (string.IsNullOrEmpty(username) ? "" : " (t.me/" + username + ")"))
                });

        private static string Cut(string value) => (value ?? "").Length > 250 ? value.Substring(0, 250) : value ?? "";

        private static string Title(long chatId, string username, string firstName) =>
            !string.IsNullOrEmpty(username) ? "@" + username : !string.IsNullOrEmpty(firstName) ? firstName : chatId.ToString();
    }
}

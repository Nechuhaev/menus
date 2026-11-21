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
using Newtonsoft.Json.Linq;

namespace Menus.Web.Services
{
    public class PosterConnection
    {
        public string RestaurantId { get; set; }
        public string Account { get; set; }
        public string Token { get; set; }
        public string SpotId { get; set; }
        public string SpotName { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime? LastWebhookAt { get; set; }
    }

    // Каса Poster: власник підключає застосунок TipMyMenu (OAuth) - меню з каси читає content-bot (menu_platforms.read_poster)
    // за посиланням «poster://{акаунт}?spot={точка}» у RestaurantMenuLinks; вебхук Poster про зміну меню ставить заклад
    // у чергу синхронізації. Ключі застосунку - Poster__ApplicationId / Poster__ApplicationSecret у menus.env.
    public class PosterService
    {
        public const string CallbackPath = "/uk/poster/callback";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly IDbConnection _connection;
        private readonly string _appId;
        private readonly string _secret;
        private static bool _ready;

        public PosterService(EfDbContext dbContext, IConfiguration configuration)
        {
            _connection = dbContext.Database.GetDbConnection();
            _appId = configuration["Poster:ApplicationId"];
            _secret = configuration["Poster:ApplicationSecret"];
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_secret);

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS PosterConnections (
                RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
                Account VARCHAR(100) NOT NULL,
                Token VARCHAR(255) NOT NULL,
                SpotId VARCHAR(32) NULL,
                SpotName VARCHAR(255) NULL,
                ConnectedAt DATETIME NOT NULL,
                LastWebhookAt DATETIME NULL,
                KEY IX_PosterConnections_Account (Account)
            ) DEFAULT CHARSET=utf8mb4");
            _ready = true;
        }

        public string AuthorizeUrl(string baseUrl) =>
            $"https://joinposter.com/api/auth?application_id={Uri.EscapeDataString(_appId ?? "")}&redirect_uri={Uri.EscapeDataString(baseUrl + CallbackPath)}&response_type=code";

        // code + account з редиректу Poster -> токен акаунта (null - помилка, текст у error)
        public async Task<(string Token, string Error)> ExchangeCode(string account, string code, string baseUrl)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(account ?? "", "^[a-z0-9_-]{1,100}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return (null, "некоректний акаунт Poster");
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["application_id"] = _appId,
                ["application_secret"] = _secret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = baseUrl + CallbackPath,
                ["code"] = code ?? ""
            });
            try
            {
                var resp = await Http.PostAsync($"https://{account}.joinposter.com/api/v2/auth/access_token", form);
                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                var token = json.Value<string>("access_token");
                if (string.IsNullOrEmpty(token))
                    return (null, json.Value<string>("error_message") ?? json.Value<string>("error") ?? "Poster не видав токен");
                return (token, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        // точки продажу акаунта: (id, назва, адреса)
        public async Task<List<(string Id, string Name, string Address)>> Spots(string token)
        {
            try
            {
                var json = JObject.Parse(await Http.GetStringAsync($"https://joinposter.com/api/spots.getSpots?token={Uri.EscapeDataString(token)}"));
                return (json["response"] as JArray ?? new JArray())
                    .Select(x => (x.Value<string>("spot_id"), x.Value<string>("spot_name") ?? x.Value<string>("name"), x.Value<string>("spot_adress") ?? x.Value<string>("address")))
                    .Where(x => !string.IsNullOrEmpty(x.Item1)).ToList();
            }
            catch (Exception)
            {
                return new List<(string, string, string)>();
            }
        }

        public async Task<PosterConnection> Get(string restaurantId)
        {
            await Ensure();
            return await _connection.QueryFirstOrDefaultAsync<PosterConnection>("SELECT * FROM PosterConnections WHERE RestaurantId = @restaurantId", new { restaurantId });
        }

        public async Task<HashSet<string>> ConnectedRestaurants()
        {
            try
            {
                await Ensure();
                return new HashSet<string>(await _connection.QueryAsync<string>("SELECT RestaurantId FROM PosterConnections"), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return new HashSet<string>();
            }
        }

        // підключення: токен і точка; меню закладу - з Poster (посилання poster://), заклад - першим у черзі синхронізації
        public async Task Save(string restaurantId, string account, string token, string spotId, string spotName)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"INSERT INTO PosterConnections (RestaurantId, Account, Token, SpotId, SpotName, ConnectedAt)
                VALUES (@restaurantId, @account, @token, @spotId, @spotName, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE Account = @account, Token = @token, SpotId = @spotId, SpotName = @spotName, ConnectedAt = UTC_TIMESTAMP()",
                new { restaurantId, account = account.ToLowerInvariant(), token, spotId, spotName });
            var url = $"poster://{account.ToLowerInvariant()}" + (string.IsNullOrEmpty(spotId) ? "" : $"?spot={spotId}");
            await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS RestaurantMenuLinks (
                    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY, Url VARCHAR(1000) NOT NULL, UpdatedAt DATETIME NOT NULL) DEFAULT CHARSET=utf8mb4;
                INSERT INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (@restaurantId, @url, NOW())
                ON DUPLICATE KEY UPDATE Url = @url, UpdatedAt = NOW()", new { restaurantId, url });
            await QueueSync(new[] { restaurantId });
        }

        public async Task Disconnect(string restaurantId)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"DELETE FROM PosterConnections WHERE RestaurantId = @restaurantId;
                DELETE FROM RestaurantMenuLinks WHERE RestaurantId = @restaurantId AND Url LIKE 'poster://%'", new { restaurantId });
        }

        // наступний запуск sync_restaurants.py бере заклад першим (як ще не перевірений)
        private async Task QueueSync(IEnumerable<string> restaurantIds)
        {
            var ids = restaurantIds.ToList();
            if (!ids.Any()) return;
            try
            {
                await _connection.ExecuteAsync("UPDATE RestaurantSyncLog SET LastCheckedAt = '2000-01-01' WHERE RestaurantId IN @ids", new { ids });
            }
            catch (Exception)
            {
                // журналу синхронізації ще немає - заклад і так новий у черзі
            }
        }

        // вебхук Poster: verify = md5(account;object;object_id;action;data;time;secret). Зміна меню - заклад у чергу синхронізації
        public async Task<string> HandleWebhook(JObject hook)
        {
            if (!IsConfigured || hook == null) return "not configured";
            var account = hook.Value<string>("account") ?? "";
            var parts = new[] { account, hook.Value<string>("object"), hook.Value<string>("object_id"), hook.Value<string>("action") };
            var data = hook["data"]?.Type == JTokenType.String ? hook.Value<string>("data") : hook["data"]?.ToString(Newtonsoft.Json.Formatting.None);
            var source = string.Join(";", parts) + (string.IsNullOrEmpty(data) ? "" : ";" + data) + ";" + hook.Value<string>("time") + ";" + _secret;
            string md5;
            using (var hash = MD5.Create())
            {
                md5 = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-", "").ToLowerInvariant();
            }
            if (!string.Equals(md5, hook.Value<string>("verify"), StringComparison.OrdinalIgnoreCase)) return "bad signature";
            await Ensure();
            var restaurants = (await _connection.QueryAsync<string>("SELECT RestaurantId FROM PosterConnections WHERE Account = @account",
                new { account = account.ToLowerInvariant() })).ToList();
            await _connection.ExecuteAsync("UPDATE PosterConnections SET LastWebhookAt = UTC_TIMESTAMP() WHERE Account = @account", new { account = account.ToLowerInvariant() });
            var obj = (hook.Value<string>("object") ?? "").ToLowerInvariant();
            if (obj == "product" || obj == "dish" || obj == "category" || obj == "menu")
                await QueueSync(restaurants);
            return "ok";
        }
    }
}

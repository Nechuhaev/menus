using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class ChecklistItem
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Hint { get; set; }
        public string Href { get; set; }
        public bool Done { get; set; }
    }

    // Кабінет «Про заклад»: «Підготуйте заклад» - пункти з галочками, що ставляться самі за даними
    // (меню з цінами, фото, скани QR, меню на сайті, Telegram); «Google Maps» - після «Скопіювати» (OwnerChecklistDone).
    // Та сама картина - підсумок угорі «Розсилки» (Summary).
    public class OwnerChecklistService
    {
        public const int MinDishes = 5;
        public const int MinPhotos = 3;

        private readonly IDbConnection _connection;
        private static bool _ready;

        public OwnerChecklistService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS OwnerChecklistDone (
                RestaurantId VARCHAR(255) NOT NULL,
                Item VARCHAR(32) NOT NULL,
                DoneAt DATETIME NOT NULL,
                PRIMARY KEY (RestaurantId, Item)
            ) DEFAULT CHARSET=utf8mb4");
            _ready = true;
        }

        public async Task MarkDone(string restaurantId, string item)
        {
            await Ensure();
            await _connection.ExecuteAsync("INSERT IGNORE INTO OwnerChecklistDone (RestaurantId, Item, DoneAt) VALUES (@restaurantId, @item, UTC_TIMESTAMP())",
                new { restaurantId, item });
        }

        private async Task<T> Safe<T>(Func<Task<T>> query, T fallback)
        {
            try { return await query(); } catch (Exception) { return fallback; }
        }

        public async Task<List<ChecklistItem>> Get(string restaurantId, string telegramLink)
        {
            await Ensure();
            (long Priced, long Photos) dishes = await Safe(() => _connection.QueryFirstAsync<(long Priced, long Photos)>(@"
                SELECT CAST(IFNULL(SUM(Price > 0), 0) AS SIGNED),
                       CAST(IFNULL(SUM(PhotoLink IS NOT NULL AND PhotoLink <> '' AND PhotoLink <> '[]'), 0) AS SIGNED)
                FROM Dishes WHERE RestaurantId = @restaurantId AND Status = 0", new { restaurantId }), (0L, 0L));
            var scans = await Safe(() => _connection.ExecuteScalarAsync<long?>("SELECT SUM(Scans) FROM QrScanDays WHERE RestaurantId = @restaurantId", new { restaurantId }), null);
            var iframe = await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM IframeDesign WHERE RestaurantId = @restaurantId", new { restaurantId }), 0L);
            var telegram = await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM OutreachTelegramLinks WHERE RestaurantId = @restaurantId", new { restaurantId }), 0L);
            var gbp = await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM OwnerChecklistDone WHERE RestaurantId = @restaurantId AND Item = 'gbp'", new { restaurantId }), 0L);
            return new List<ChecklistItem>
            {
                new ChecklistItem { Key = "menu", Title = "Меню з цінами", Hint = $"щонайменше {MinDishes} страв з ціною", Href = "/uk/restaurants/manage/menu", Done = dishes.Priced >= MinDishes },
                new ChecklistItem { Key = "photos", Title = "Фото страв", Hint = $"щонайменше {MinPhotos} страви з фото - їх частіше обирають", Href = "/uk/restaurants/manage/menu", Done = dishes.Photos >= MinPhotos },
                new ChecklistItem { Key = "qr", Title = "QR-код на столах", Hint = "роздрукуйте табличку - галочка з'явиться після першого скану", Href = "#qr-menu", Done = (scans ?? 0) > 0 },
                new ChecklistItem { Key = "gbp", Title = "Меню в Google Maps", Hint = "скопіюйте посилання й додайте в профіль закладу в Google", Href = "#gbp-menu", Done = gbp > 0 },
                new ChecklistItem { Key = "iframe", Title = "Меню на вашому сайті", Hint = "налаштуйте вигляд і вставте код на сайт", Href = "#embed-menu", Done = iframe > 0 },
                new ChecklistItem { Key = "telegram", Title = "Telegram-бот", Hint = "стоп-лист і ціни прямо з Telegram: /stop, /price", Href = string.IsNullOrEmpty(telegramLink) ? null : telegramLink, Done = telegram > 0 },
            };
        }

        // заклади зі сканами QR за 28 днів (фільтр «Розсилки»)
        public async Task<HashSet<string>> ScannedRestaurants() =>
            new HashSet<string>(await Safe(() => _connection.QueryAsync<string>(
                "SELECT DISTINCT RestaurantId FROM QrScanDays WHERE Day >= UTC_DATE() - INTERVAL 27 DAY"), Enumerable.Empty<string>()));

        public class OutreachSummary
        {
            public int Restaurants, WithOwner, WithTelegram, QrScans28, Requests28, NewRequests;
        }

        // підсумок для «Розсилки»: активні заклади, з власником, з Telegram, скани QR і заявки за 28 днів
        public async Task<OutreachSummary> Summary()
        {
            var s = new OutreachSummary();
            s.Restaurants = (int)await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Restaurants WHERE Status = 0"), 0L);
            s.WithOwner = (int)await Safe(() => _connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(DISTINCT rr.RestaurantId) FROM RestaurantRoles rr JOIN Restaurants r ON r.Id = rr.RestaurantId WHERE r.Status = 0"), 0L);
            s.WithTelegram = (int)await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(DISTINCT RestaurantId) FROM OutreachTelegramLinks"), 0L);
            s.QrScans28 = (int)(await Safe(() => _connection.ExecuteScalarAsync<long?>("SELECT SUM(Scans) FROM QrScanDays WHERE Day >= UTC_DATE() - INTERVAL 27 DAY"), null) ?? 0);
            s.Requests28 = (int)await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM CustomerRequests WHERE CreateDate >= UTC_TIMESTAMP() - INTERVAL 28 DAY"), 0L);
            s.NewRequests = (int)await Safe(() => _connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM CustomerRequests WHERE Status IN (0, 2)"), 0L);
            return s;
        }
    }
}

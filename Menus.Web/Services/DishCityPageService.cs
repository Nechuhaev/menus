using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Menus.Web.Services
{
    public class DishCityPage
    {
        public string Id { get; set; }
        public string CityId { get; set; }
        public string KeyUk { get; set; }
        public string Slug { get; set; }
        public string Title { get; set; }
        public bool Enabled { get; set; }
        public int Venues { get; set; }
        public int Restaurants { get; set; }
        public decimal MinPrice { get; set; }
        public decimal MedianPrice { get; set; }
        public decimal MaxPrice { get; set; }
        public string DishIds { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Title - {"uk": "...", "ru": "...", "en": "..."} з content-bot/dish_city_pages.py
        public string GetTitle(string culture)
        {
            var titles = string.IsNullOrEmpty(Title) ? new Dictionary<string, string>() : JsonConvert.DeserializeObject<Dictionary<string, string>>(Title);
            return titles.TryGetValue(culture, out var title) && !string.IsNullOrWhiteSpace(title) ? title
                : titles.TryGetValue("uk", out var uk) ? uk : KeyUk;
        }
    }

    // Один заклад на сторінці «страва + місто»: найкраща з його страв за цим ключем
    public class DishCityItem
    {
        public string DishName { get; set; }
        public string DishUrl { get; set; }
        public string RestaurantName { get; set; }
        public string RestaurantUrl { get; set; }
        public string Address { get; set; }
        public string Currency { get; set; }
        public decimal Price { get; set; }
        public float Rating { get; set; }
        public int RatingsCount { get; set; }
        public string Photo { get; set; }
    }

    // Сторінки «страва + місто». Таблицю наповнює content-bot/dish_city_pages.py (щоночі),
    // сторінку вмикає адмін; показується лише ввімкнена сторінка з порогом закладів
    public class DishCityPageService
    {
        public const int MinVenues = 5;

        private readonly IDbConnection _connection;

        public DishCityPageService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS DishCityPages (
                Id CHAR(36) NOT NULL PRIMARY KEY,
                CityId VARCHAR(255) NOT NULL,
                KeyUk VARCHAR(255) NOT NULL,
                Slug VARCHAR(255) NOT NULL,
                Title TEXT NOT NULL,
                Enabled TINYINT(1) NOT NULL DEFAULT 0,
                Venues INT NOT NULL,
                Restaurants INT NOT NULL,
                MinPrice DECIMAL(10,2) NOT NULL,
                MedianPrice DECIMAL(10,2) NOT NULL,
                MaxPrice DECIMAL(10,2) NOT NULL,
                DishIds MEDIUMTEXT NOT NULL,
                UpdatedAt DATETIME NOT NULL,
                UNIQUE KEY UX_DishCityPages_City_Key (CityId, KeyUk),
                KEY IX_DishCityPages_City_Slug (CityId, Slug)
            ) DEFAULT CHARSET=utf8mb4";

        private const string Columns = "Id, CityId, KeyUk, Slug, Title, Enabled, Venues, Restaurants, MinPrice, MedianPrice, MaxPrice, UpdatedAt";

        public async Task<DishCityPage> GetPublished(string cityId, string slug)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return await _connection.QueryFirstOrDefaultAsync<DishCityPage>(
                $"SELECT {Columns}, DishIds FROM DishCityPages WHERE CityId = @cityId AND Slug = @slug AND Enabled = 1 AND Venues >= @MinVenues",
                new { cityId, slug, MinVenues });
        }

        public async Task<List<DishCityPage>> GetPublishedForCity(string cityId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return (await _connection.QueryAsync<DishCityPage>(
                $"SELECT {Columns} FROM DishCityPages WHERE CityId = @cityId AND Enabled = 1 AND Venues >= @MinVenues ORDER BY Venues DESC",
                new { cityId, MinVenues })).ToList();
        }

        public async Task<List<DishCityPage>> GetAllPublished()
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return (await _connection.QueryAsync<DishCityPage>(
                $"SELECT {Columns} FROM DishCityPages WHERE Enabled = 1 AND Venues >= @MinVenues",
                new { MinVenues })).ToList();
        }

        // Для адмінки: усі кандидати міста, від найпоширеніших
        public async Task<List<DishCityPage>> GetAllForCity(string cityId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return (await _connection.QueryAsync<DishCityPage>(
                $"SELECT {Columns} FROM DishCityPages WHERE CityId = @cityId ORDER BY Venues DESC, KeyUk",
                new { cityId })).ToList();
        }

        public async Task SetEnabled(IEnumerable<string> enabledIds, IEnumerable<string> shownIds)
        {
            var shown = shownIds.ToList();
            if (shown.Count == 0) return;
            var enabled = new HashSet<string>(enabledIds);
            await _connection.ExecuteAsync("UPDATE DishCityPages SET Enabled = 1 WHERE Id IN @ids",
                new { ids = shown.Where(enabled.Contains).DefaultIfEmpty("-").ToList() });
            await _connection.ExecuteAsync("UPDATE DishCityPages SET Enabled = 0 WHERE Id IN @ids",
                new { ids = shown.Where(x => !enabled.Contains(x)).DefaultIfEmpty("-").ToList() });
        }

        // Заклади сторінки: по одній страві на ресторан (з оцінкою, з фото, дешевша),
        // спершу заклади з оцінками
        public async Task<List<DishCityItem>> GetItems(DishCityPage page, string culture)
        {
            var ids = JsonConvert.DeserializeObject<List<string>>(page.DishIds ?? "[]");
            if (ids.Count == 0) return new List<DishCityItem>();

            var rows = await _connection.QueryAsync<DishRow>(@"
                SELECT d.Id, d.Name, d.Price, d.PhotoLink, d.RatingFloat, d.RatingsCount, ds.Url AS DishUrl,
                       r.Id AS RestaurantId, r.Name AS RestaurantName, rs.Url AS RestaurantUrl,
                       ri.DisplayAddress, ri.Currency
                FROM Dishes d
                JOIN Restaurants r ON r.Id = d.RestaurantId
                JOIN DishSeo ds ON ds.ItemId = d.Id
                JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                WHERE d.Id IN @ids AND d.Status = 0 AND r.Status = 0",
                new { ids });

            return rows
                .GroupBy(x => x.RestaurantId)
                .Select(g => g
                    .OrderByDescending(x => x.RatingsCount > 0)
                    .ThenByDescending(x => x.RatingFloat)
                    .ThenByDescending(x => FirstPhoto(x.PhotoLink) != null)
                    .ThenBy(x => x.Price <= 0)
                    .ThenBy(x => x.Price)
                    .First())
                .Select(x => new DishCityItem
                {
                    DishName = Localized(x.Name, culture),
                    DishUrl = x.DishUrl,
                    RestaurantName = x.RestaurantName,
                    RestaurantUrl = x.RestaurantUrl,
                    Address = Localized(x.DisplayAddress, culture),
                    Currency = x.Currency,
                    Price = x.Price,
                    Rating = x.RatingFloat,
                    RatingsCount = x.RatingsCount,
                    Photo = FirstPhoto(x.PhotoLink)
                })
                .OrderByDescending(x => x.RatingsCount > 0)
                .ThenByDescending(x => x.Rating)
                .ThenByDescending(x => x.Photo != null)
                .ThenBy(x => x.RestaurantName)
                .ToList();
        }

        private static string Localized(string json, string culture)
        {
            var value = LocalizableString.FromJson(json);
            foreach (var locale in new[] { culture, "uk", "ru", "en" })
            {
                var text = value.Get(locale);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            return "";
        }

        private static string FirstPhoto(string photoLink)
        {
            if (string.IsNullOrWhiteSpace(photoLink)) return null;
            try
            {
                return JsonConvert.DeserializeObject<List<string>>(photoLink)?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private class DishRow
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
            public string PhotoLink { get; set; }
            public float RatingFloat { get; set; }
            public int RatingsCount { get; set; }
            public string DishUrl { get; set; }
            public string RestaurantId { get; set; }
            public string RestaurantName { get; set; }
            public string RestaurantUrl { get; set; }
            public string DisplayAddress { get; set; }
            public string Currency { get; set; }
        }
    }
}

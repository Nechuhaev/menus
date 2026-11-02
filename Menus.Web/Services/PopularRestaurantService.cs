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
    // «Популярні заклади міста» на головній: ресторани з найбільшими показами в Google на позиціях 5-12.
    // Список щотижня рахує content-bot/popular_restaurants.py з API Search Console; сайт лише читає.
    // Посилання з головної підтягують сторінки, яким до першої п'ятірки бракує небагато
    public class PopularRestaurantService
    {
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS PopularRestaurants (
                CityId VARCHAR(255) NOT NULL,
                RestaurantId VARCHAR(255) NOT NULL,
                Position INT NOT NULL,
                Impressions INT NOT NULL,
                Clicks INT NOT NULL,
                AvgPosition DECIMAL(6,2) NOT NULL,
                UpdatedAt DATETIME NOT NULL,
                PRIMARY KEY (CityId, RestaurantId)
            ) DEFAULT CHARSET=utf8mb4";

        // на кожному відкритті головної - кеш на 10 хвилин
        private static Dictionary<string, List<string>> _byCity;
        private static DateTime _loadedAt;

        private readonly IDbConnection _connection;

        public PopularRestaurantService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private class Row
        {
            public string CityId { get; set; }
            public string RestaurantId { get; set; }
        }

        public async Task<List<string>> GetIds(string cityId)
        {
            if (string.IsNullOrEmpty(cityId)) return new List<string>();
            if (_byCity == null || DateTime.UtcNow - _loadedAt > TimeSpan.FromMinutes(10))
            {
                await _connection.ExecuteAsync(CreateTableSql);
                _byCity = (await _connection.QueryAsync<Row>(
                        "SELECT CONCAT(CityId, '') AS CityId, CONCAT(RestaurantId, '') AS RestaurantId FROM PopularRestaurants ORDER BY Position"))
                    .GroupBy(x => x.CityId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.RestaurantId).ToList(), StringComparer.OrdinalIgnoreCase);
                _loadedAt = DateTime.UtcNow;
            }
            return _byCity.TryGetValue(cityId, out var ids) ? ids : new List<string>();
        }
    }
}

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    // Посилання на меню ресторану в QR-сервісі (Expirenza, ChoiceQR), яке адмін указує в картці.
    // content-bot читає звідти страви, фото й ціни без AI. Таблиця спільна з ботом (sync_restaurants.py)
    public class RestaurantMenuLinkService
    {
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS RestaurantMenuLinks (
                RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
                Url VARCHAR(1000) NOT NULL,
                UpdatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4";

        private readonly IDbConnection _connection;

        public RestaurantMenuLinkService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<string> Get(string restaurantId)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return await _connection.QueryFirstOrDefaultAsync<string>(
                "SELECT Url FROM RestaurantMenuLinks WHERE RestaurantId = @restaurantId", new { restaurantId });
        }

        // Порожнє або не http(s) посилання видаляє запис
        public async Task Set(string restaurantId, string url)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            url = url?.Trim();
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                await _connection.ExecuteAsync("DELETE FROM RestaurantMenuLinks WHERE RestaurantId = @restaurantId", new { restaurantId });
                return;
            }
            await _connection.ExecuteAsync(@"
                INSERT INTO RestaurantMenuLinks (RestaurantId, Url, UpdatedAt) VALUES (@restaurantId, @url, NOW())
                ON DUPLICATE KEY UPDATE Url = VALUES(Url), UpdatedAt = NOW()", new { restaurantId, url });
        }
    }
}

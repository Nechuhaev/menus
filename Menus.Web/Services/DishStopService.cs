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
    // Стоп-лист: страва тимчасово «Немає в наявності» (окремо від статусу страви - sync_restaurants.py її не чіпає).
    // Ставить власник у Telegram-боті (/stop) чи в кабінеті; знімається сама о 6:00 за Києвом наступного дня або вручну.
    public class DishStopService
    {
        private readonly IDbConnection _connection;
        private static bool _ready;

        public DishStopService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS DishStopList (
                DishId VARCHAR(255) NOT NULL PRIMARY KEY,
                RestaurantId VARCHAR(255) NOT NULL,
                Until DATETIME NULL,
                Source VARCHAR(64) NULL,
                CreatedAt DATETIME NOT NULL,
                KEY IX_DishStopList_Restaurant (RestaurantId)
            ) DEFAULT CHARSET=utf8mb4");
            _ready = true;
        }

        // наступні 6:00 за Києвом (UTC) - стоп-лист «на сьогодні»
        public static DateTime NextMorningUtc()
        {
            TimeZoneInfo zone = TimeZoneInfo.Utc;
            foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
            {
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); break; } catch (Exception) { }
            }
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            var morning = local.Date.AddHours(6);
            if (local >= morning) morning = morning.AddDays(1);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(morning, DateTimeKind.Unspecified), zone);
        }

        // id страв закладу в стоп-листі зараз
        public async Task<HashSet<string>> Active(string restaurantId)
        {
            if (string.IsNullOrEmpty(restaurantId)) return new HashSet<string>();
            try
            {
                await Ensure();
                return new HashSet<string>(await _connection.QueryAsync<string>(
                    "SELECT DishId FROM DishStopList WHERE RestaurantId = @restaurantId AND (Until IS NULL OR Until > UTC_TIMESTAMP())",
                    new { restaurantId }));
            }
            catch (Exception)
            {
                return new HashSet<string>();
            }
        }

        public async Task<bool> IsStopped(string dishId)
        {
            await Ensure();
            return await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM DishStopList WHERE DishId = @dishId AND (Until IS NULL OR Until > UTC_TIMESTAMP())", new { dishId }) > 0;
        }

        // until = null - до ручного повернення
        public async Task Stop(string dishId, string restaurantId, DateTime? untilUtc, string source)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"INSERT INTO DishStopList (DishId, RestaurantId, Until, Source, CreatedAt)
                VALUES (@dishId, @restaurantId, @untilUtc, @source, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE RestaurantId = @restaurantId, Until = @untilUtc, Source = @source, CreatedAt = UTC_TIMESTAMP()",
                new { dishId, restaurantId, untilUtc, source });
        }

        public async Task Back(string dishId)
        {
            await Ensure();
            await _connection.ExecuteAsync("DELETE FROM DishStopList WHERE DishId = @dishId", new { dishId });
        }
    }
}

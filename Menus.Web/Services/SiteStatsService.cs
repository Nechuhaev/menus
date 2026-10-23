using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class SiteStats
    {
        public int Restaurants { get; set; }
        public int Dishes { get; set; }
        public int Cities { get; set; }
    }

    // Цифри сервісу для «Про нас»: активні заклади, їхні активні страви, міста. Кеш на годину
    public class SiteStatsService
    {
        private static SiteStats _cached;
        private static DateTime _cachedAt;

        private readonly IDbConnection _connection;

        public SiteStatsService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<SiteStats> Get()
        {
            if (_cached != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromHours(1)) return _cached;
            _cached = await _connection.QueryFirstAsync<SiteStats>(@"
                SELECT (SELECT COUNT(*) FROM Restaurants WHERE Status = 0) AS Restaurants,
                       (SELECT COUNT(*) FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                        WHERE d.Status = 0 AND r.Status = 0) AS Dishes,
                       (SELECT COUNT(DISTINCT City) FROM Restaurants WHERE Status = 0) AS Cities");
            _cachedAt = DateTime.UtcNow;
            return _cached;
        }
    }
}

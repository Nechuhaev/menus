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
    // «Тонкі» сторінки, закриті від індексації (noindex, follow і без sitemap): тонкі ресторани й тонкі напої.
    // Список щоночі перераховує content-bot/thin_pages.py --save; сайт лише читає його
    public class SeoNoindexService
    {
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS SeoNoindex (
                ItemId VARCHAR(255) NOT NULL PRIMARY KEY,
                Kind VARCHAR(20) NOT NULL,
                UpdatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4";

        // Перевіряється на кожній сторінці страви й ресторану, тому кешується на 10 хвилин
        private static HashSet<string> _ids;
        private static DateTime _loadedAt;

        private readonly IDbConnection _connection;

        public SeoNoindexService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<HashSet<string>> GetIds()
        {
            if (_ids != null && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(10)) return _ids;
            await _connection.ExecuteAsync(CreateTableSql);
            _ids = new HashSet<string>(await _connection.QueryAsync<string>("SELECT ItemId FROM SeoNoindex"), StringComparer.OrdinalIgnoreCase);
            _loadedAt = DateTime.UtcNow;
            return _ids;
        }

        public async Task<bool> IsNoindex(string id) => !string.IsNullOrEmpty(id) && (await GetIds()).Contains(id);
    }
}

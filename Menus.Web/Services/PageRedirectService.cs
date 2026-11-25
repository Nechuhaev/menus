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
    // 301 зі статті-дубля на основну (PageRedirects). Дубль ховається (Pages.Status = Hidden) - зникає з блогу й sitemap,
    // а стара адреса з Google веде на основну статтю. Злиття додаються в Merges і застосовуються один раз.
    public class PageRedirectService
    {
        private static readonly (string From, string To)[] Merges =
        {
            // три статті про ресторан «Гага» (Київ) конкурували в Google - лишаємо ту, що має найбільше показів
            ("restoran-haha-v-kyievi-adresa-meniu-ta-atmosfera-zakladu", "restoran-gaga-u-kyievi-ohliad-meniu-tsin-ta-atmosfery-hruzynskoi-hosty"),
            ("restoran-haha-v-kyievi-adresa-meniu-ta-atmosfera-zakladu-1", "restoran-gaga-u-kyievi-ohliad-meniu-tsin-ta-atmosfery-hruzynskoi-hosty"),
            // четверта стаття про «Гагу» («найкраща українська кухня») - теж дубль
            ("restoran-haha-naykrashcha-ukrainska-kukhnia-u-kyievi", "restoran-gaga-u-kyievi-ohliad-meniu-tsin-ta-atmosfery-hruzynskoi-hosty"),
        };

        private static Dictionary<string, string> _cache;
        private static DateTime _cacheAt;
        private static bool _ready;
        private readonly IDbConnection _connection;

        public PageRedirectService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS PageRedirects (
                    FromUrl VARCHAR(255) NOT NULL PRIMARY KEY,
                    ToUrl VARCHAR(255) NOT NULL,
                    CreatedAt DATETIME NOT NULL
                ) DEFAULT CHARSET=utf8mb4");
            foreach (var (from, to) in Merges)
            {
                // новий запис - ховаємо дубль; якщо адмін потім опублікує його знову, повторно не ховаємо
                var added = await _connection.ExecuteAsync(
                    "INSERT IGNORE INTO PageRedirects (FromUrl, ToUrl, CreatedAt) VALUES (@from, @to, UTC_TIMESTAMP())", new { from, to });
                if (added > 0)
                    await _connection.ExecuteAsync("UPDATE Pages SET Status = 0 WHERE Url = @from", new { from });
            }
            _ready = true;
        }

        // куди перенаправити статтю (null - нікуди)
        public async Task<string> Target(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                await Ensure();
                if (_cache == null || DateTime.UtcNow - _cacheAt > TimeSpan.FromMinutes(10))
                {
                    _cache = (await _connection.QueryAsync<(string FromUrl, string ToUrl)>("SELECT FromUrl, ToUrl FROM PageRedirects"))
                        .ToDictionary(x => x.FromUrl, x => x.ToUrl, StringComparer.OrdinalIgnoreCase);
                    _cacheAt = DateTime.UtcNow;
                }
                return _cache.TryGetValue(url.Trim(), out var to) && !string.Equals(to, url, StringComparison.OrdinalIgnoreCase) ? to : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

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
    public class RestaurantStats
    {
        public int Impressions { get; set; }
        public int Clicks { get; set; }
        public decimal Position { get; set; }   // середня позиція в Google (зважена на покази)
        public int Views { get; set; }          // перегляди сторінки (GA4)
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int QrScans { get; set; }        // скани QR-коду на стіл (QrScanDays)
        public bool HasData => Impressions > 0 || Views > 0 || QrScans > 0;
        // пошукові запити, за якими знаходять заклад (SeoPageQueryDays) - лише для одного закладу
        public List<(string Query, int Clicks, int Impressions)> Queries { get; set; } = new List<(string, int, int)>();
    }

    // Сторінка закладу в Google і GA4 (усі мови разом): SeoPageDays / GaPageDays, які щодня пише content-bot/seo_daily.py.
    // Для кабінету власника, картки в «Розсилці», змінних листів і щомісячного звіту.
    public class RestaurantStatsService
    {
        private const string PageSlugSql = "LOWER(SUBSTRING_INDEX(SUBSTRING_INDEX({0}, '/restaurant/', -1), '/', 1))";
        private const string PageRegex = "/restaurant/[^/]+/?$";
        private readonly IDbConnection _connection;

        public RestaurantStatsService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        // Усі заклади за 28 днів - кеш на 6 год для публічних сторінок (блок «Власник закладу»).
        // Оновлює один запит (інші не чекають: беруть старий кеш або нічого), щоб не тримати з'єднання з базою.
        private static Dictionary<string, RestaurantStats> _all28;
        private static DateTime _all28At;
        private static readonly System.Threading.SemaphoreSlim All28Lock = new System.Threading.SemaphoreSlim(1, 1);

        public async Task<RestaurantStats> Cached28(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return null;
            if ((_all28 == null || DateTime.UtcNow - _all28At > TimeSpan.FromHours(6)) && All28Lock.Wait(0))
            {
                try
                {
                    var last = await LastDay();
                    _all28 = last == null ? new Dictionary<string, RestaurantStats>() : await GetRange(last.Value.AddDays(-27), last.Value);
                    _all28At = DateTime.UtcNow;
                }
                catch (Exception)
                {
                    _all28At = DateTime.UtcNow; // повтор через 6 год, а не на кожному запиті
                }
                finally
                {
                    All28Lock.Release();
                }
            }
            return _all28 != null && _all28.TryGetValue(slug.ToLowerInvariant(), out var s) ? s : null;
        }

        // останні 28 днів до останнього дня з даними
        public async Task<RestaurantStats> GetLast28(string slug, string restaurantId = null)
        {
            var last = await LastDay();
            if (last == null || string.IsNullOrEmpty(slug)) return new RestaurantStats();
            var s = (await GetRange(last.Value.AddDays(-27), last.Value, slug)).TryGetValue(slug.ToLowerInvariant(), out var found)
                ? found : new RestaurantStats { From = last.Value.AddDays(-27), To = last.Value };
            s.Queries = await TopQueries(slug, last.Value.AddDays(-27), last.Value);
            // скани QR - за останні 28 календарних днів (лічильник живий, без затримки Search Console)
            if (restaurantId != null) s.QrScans = await QrScans(restaurantId, DateTime.UtcNow.Date.AddDays(-27), DateTime.UtcNow.Date);
            return s;
        }

        public async Task<int> QrScans(string restaurantId, DateTime from, DateTime to)
        {
            if (string.IsNullOrEmpty(restaurantId)) return 0;
            try
            {
                return await _connection.ExecuteScalarAsync<int?>(
                    "SELECT CAST(SUM(Scans) AS SIGNED) FROM QrScanDays WHERE RestaurantId = @restaurantId AND Day BETWEEN @from AND @to",
                    new { restaurantId, from, to }) ?? 0;
            }
            catch (Exception)
            {
                return 0; // ще жодного скану - таблиці немає
            }
        }

        // топ запитів Google сторінки закладу за період (за показами)
        public async Task<List<(string Query, int Clicks, int Impressions)>> TopQueries(string slug, DateTime from, DateTime to, int limit = 10)
        {
            if (string.IsNullOrEmpty(slug)) return new List<(string, int, int)>();
            var like = "%/restaurant/" + slug.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            try
            {
                return (await _connection.QueryAsync<(string Query, long Clicks, long Impressions)>(@"
                    SELECT Query, CAST(SUM(Clicks) AS SIGNED), CAST(SUM(Impressions) AS SIGNED) FROM SeoPageQueryDays
                    WHERE Day BETWEEN @from AND @to AND (Page LIKE @like OR Page LIKE @likeSlash)
                    GROUP BY Query ORDER BY 3 DESC, 2 DESC LIMIT @limit", new { from, to, like, likeSlash = like + "/", limit }, commandTimeout: 60))
                    .Select(x => (x.Query, (int)x.Clicks, (int)x.Impressions)).ToList();
            }
            catch (Exception)
            {
                return new List<(string, int, int)>(); // seo_daily.py ще не зберігав запити
            }
        }

        public async Task<DateTime?> LastDay()
        {
            try
            {
                return await _connection.ExecuteScalarAsync<DateTime?>("SELECT MAX(Day) FROM SeoPageDays");
            }
            catch (Exception)
            {
                return null; // seo_daily.py ще не запускався
            }
        }

        // slug закладу (нижній регістр) -> статистика за період; slug = null - усі заклади
        public async Task<Dictionary<string, RestaurantStats>> GetRange(DateTime from, DateTime to, string slug = null)
        {
            var result = new Dictionary<string, RestaurantStats>();
            RestaurantStats Get(string key) => result.TryGetValue(key, out var s) ? s : result[key] = new RestaurantStats { From = from, To = to };
            // один заклад - точний збіг кінця адреси (LIKE без REGEXP по всій таблиці)
            var one = slug == null ? "" : " AND (Page LIKE @like OR Page LIKE @likeSlash)";
            var oneGa = slug == null ? "" : " AND (Path LIKE @like OR Path LIKE @likeSlash)";
            var like = "%/restaurant/" + (slug ?? "").ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var args = new { from, to, re = PageRegex, like, likeSlash = like + "/" };
            try
            {
                foreach (var r in await _connection.QueryAsync<(string Slug, long Clicks, long Impressions, decimal PositionSum)>(
                             $@"SELECT {string.Format(PageSlugSql, "Page")}, CAST(SUM(Clicks) AS SIGNED), CAST(SUM(Impressions) AS SIGNED),
                                       CAST(SUM(Position * Impressions) AS DECIMAL(18,2))
                                FROM SeoPageDays WHERE Day BETWEEN @from AND @to AND Page REGEXP @re{one} GROUP BY 1", args, commandTimeout: 120))
                {
                    var s = Get(Uri.UnescapeDataString(r.Slug ?? ""));
                    s.Clicks += (int)r.Clicks;
                    s.Impressions += (int)r.Impressions;
                    s.Position = s.Impressions > 0 ? Math.Round(r.PositionSum / s.Impressions, 1) : 0;
                }
            }
            catch (Exception)
            {
                // таблиці ще немає
            }
            try
            {
                foreach (var r in await _connection.QueryAsync<(string Slug, long Views)>(
                             $@"SELECT {string.Format(PageSlugSql, "Path")}, CAST(SUM(Views) AS SIGNED)
                                FROM GaPageDays WHERE Day BETWEEN @from AND @to AND Path REGEXP @re{oneGa} GROUP BY 1", args, commandTimeout: 120))
                {
                    Get(Uri.UnescapeDataString(r.Slug ?? "")).Views += (int)r.Views;
                }
            }
            catch (Exception)
            {
                // GA4 ще не підключено
            }
            return result;
        }

        // «1 240 показів у Google, 85 переходів» - для кабінету й листів
        public static string Describe(RestaurantStats s) =>
            $"{s.Impressions:N0} показів у Google, {s.Clicks:N0} переходів з Google" + (s.Views > 0 ? $", {s.Views:N0} переглядів сторінки" : "")
            + (s.QrScans > 0 ? $", {s.QrScans:N0} сканів QR" : "");
    }
}

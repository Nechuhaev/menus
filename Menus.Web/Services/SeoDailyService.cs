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
    public class SeoDayPoint
    {
        public DateTime Day { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public int Views { get; set; }
        public int Sessions { get; set; }
    }

    public class SeoPagePeriod
    {
        public string Page { get; set; }
        public string Kind { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public decimal Position { get; set; }
        public int PrevClicks { get; set; }
        public int PrevImpressions { get; set; }
        public decimal? PrevPosition { get; set; }
        public int Views { get; set; }
        public int PrevViews { get; set; }
        public double Ctr => Impressions == 0 ? 0 : 100.0 * Clicks / Impressions;
        public bool IsNew => PrevImpressions == 0 && Impressions > 0;
    }

    public class SeoKindPeriod
    {
        public string Kind { get; set; }
        public int Pages { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public int PrevClicks { get; set; }
        public int PrevImpressions { get; set; }
        public int Views { get; set; }
        public int PrevViews { get; set; }
    }

    public class SeoSlugStats
    {
        public string Slug { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public int Views { get; set; }
        public int Sessions { get; set; }
    }

    // Щоденна статистика сторінок: SeoPageDays (Search Console) і GaPageDays (GA4) наповнює content-bot/seo_daily.py.
    // Період і такий самий попередній період, графіки по днях для всього сайту, виду сторінок чи однієї сторінки.
    public class SeoDailyService
    {
        // вид сторінки в SQL - ті самі правила, що SeoReportService.KindOf (Page - повна адреса, Path - шлях GA4)
        private static readonly (string Kind, string Path)[] KindRules =
        {
            ("dish", "/(uk|ru|en)/[^/]+/restaurant/[^/]+/[^/]+/?$"),
            ("restaurant", "/(uk|ru|en)/[^/]+/restaurant/[^/]+/?$"),
            ("dishcity", "/(uk|ru|en)/[^/]+/dishes/[^/]+/?$"),
            ("article", "/(uk|ru|en)/(news|blog)/"),
        };

        private static string KindSql(string column, bool fullUrl) =>
            "CASE " + string.Join(" ", KindRules.Select(r =>
                $"WHEN {column} REGEXP '^{(fullUrl ? "https?://[^/]+" : "")}{r.Path}' THEN '{r.Kind}'")) + " ELSE 'other' END";

        private readonly IDbConnection _connection;

        public SeoDailyService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        // останній день із даними Search Console (null - бот ще не запускався)
        public async Task<DateTime?> GetLastDay()
        {
            try
            {
                return await _connection.ExecuteScalarAsync<DateTime?>("SELECT MAX(Day) FROM SeoPageDays");
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string PathOf(string page)
        {
            var path = Uri.TryCreate(page ?? "", UriKind.Absolute, out var uri) ? uri.AbsolutePath : page ?? "";
            return Uri.UnescapeDataString(path).TrimEnd('/').ToLowerInvariant();
        }

        // Сторінки за період [from, to] з цифрами попереднього періоду [prevFrom, prevTo] і переглядами GA4
        public async Task<List<SeoPagePeriod>> GetPages(DateTime from, DateTime to, DateTime prevFrom, DateTime prevTo)
        {
            var pages = (await _connection.QueryAsync<SeoPagePeriod>(@"
                SELECT Page,
                       CAST(SUM(IF(Day >= @from, Clicks, 0)) AS SIGNED) AS Clicks,
                       CAST(SUM(IF(Day >= @from, Impressions, 0)) AS SIGNED) AS Impressions,
                       IFNULL(SUM(IF(Day >= @from, Position * Impressions, 0)) / NULLIF(SUM(IF(Day >= @from, Impressions, 0)), 0), 0) AS Position,
                       CAST(SUM(IF(Day <= @prevTo, Clicks, 0)) AS SIGNED) AS PrevClicks,
                       CAST(SUM(IF(Day <= @prevTo, Impressions, 0)) AS SIGNED) AS PrevImpressions,
                       SUM(IF(Day <= @prevTo, Position * Impressions, 0)) / NULLIF(SUM(IF(Day <= @prevTo, Impressions, 0)), 0) AS PrevPosition
                FROM SeoPageDays WHERE Day BETWEEN @prevFrom AND @to GROUP BY Page",
                new { from, to, prevFrom, prevTo }, commandTimeout: 120)).ToList();
            var views = await GaByPath(from, to, prevFrom, prevTo);
            foreach (var page in pages)
            {
                page.Kind = SeoReportService.KindOf(page.Page);
                if (views.TryGetValue(PathOf(page.Page), out var v))
                {
                    page.Views = v.Views;
                    page.PrevViews = v.PrevViews;
                }
            }
            return pages;
        }

        private async Task<Dictionary<string, (int Views, int PrevViews)>> GaByPath(DateTime from, DateTime to, DateTime prevFrom, DateTime prevTo)
        {
            try
            {
                var rows = await _connection.QueryAsync<(string Path, long Views, long PrevViews)>(@"
                    SELECT Path, CAST(SUM(IF(Day >= @from, Views, 0)) AS SIGNED), CAST(SUM(IF(Day <= @prevTo, Views, 0)) AS SIGNED)
                    FROM GaPageDays WHERE Day BETWEEN @prevFrom AND @to GROUP BY Path",
                    new { from, to, prevFrom, prevTo }, commandTimeout: 120);
                var result = new Dictionary<string, (int, int)>();
                foreach (var r in rows)
                {
                    var key = PathOf(r.Path);
                    result.TryGetValue(key, out var old);
                    result[key] = (old.Item1 + (int)r.Views, old.Item2 + (int)r.PrevViews);
                }
                return result;
            }
            catch (Exception)
            {
                return new Dictionary<string, (int, int)>(); // GA4 ще не підключено
            }
        }

        public static List<SeoKindPeriod> Totals(IEnumerable<SeoPagePeriod> pages) =>
            pages.GroupBy(x => x.Kind).Select(g => new SeoKindPeriod
            {
                Kind = g.Key, Pages = g.Count(x => x.Impressions > 0),
                Clicks = g.Sum(x => x.Clicks), Impressions = g.Sum(x => x.Impressions),
                PrevClicks = g.Sum(x => x.PrevClicks), PrevImpressions = g.Sum(x => x.PrevImpressions),
                Views = g.Sum(x => x.Views), PrevViews = g.Sum(x => x.PrevViews)
            }).OrderByDescending(x => x.Impressions).ToList();

        // По днях за період: усі сторінки, вид сторінок (kind) або одна сторінка (page - повна адреса з Search Console)
        public async Task<List<SeoDayPoint>> GetDaily(DateTime from, DateTime to, string kind, string page)
        {
            var gscWhere = "Day BETWEEN @from AND @to";
            var gaWhere = "Day BETWEEN @from AND @to";
            if (!string.IsNullOrEmpty(page))
            {
                gscWhere += " AND Page = @page";
                gaWhere += " AND LOWER(TRIM(TRAILING '/' FROM Path)) = @path";
            }
            else if (!string.IsNullOrEmpty(kind))
            {
                gscWhere += $" AND {KindSql("Page", true)} = @kind";
                gaWhere += $" AND {KindSql("Path", false)} = @kind";
            }
            var args = new { from, to, kind, page, path = PathOf(page) };
            var days = (await _connection.QueryAsync<SeoDayPoint>(
                    $"SELECT Day, CAST(SUM(Clicks) AS SIGNED) AS Clicks, CAST(SUM(Impressions) AS SIGNED) AS Impressions FROM SeoPageDays WHERE {gscWhere} GROUP BY Day",
                    args, commandTimeout: 120))
                .ToDictionary(x => x.Day.Date);
            try
            {
                foreach (var g in await _connection.QueryAsync<SeoDayPoint>(
                             $"SELECT Day, CAST(SUM(Views) AS SIGNED) AS Views, CAST(SUM(Sessions) AS SIGNED) AS Sessions FROM GaPageDays WHERE {gaWhere} GROUP BY Day",
                             args, commandTimeout: 120))
                {
                    if (!days.TryGetValue(g.Day.Date, out var d)) days[g.Day.Date] = d = new SeoDayPoint { Day = g.Day.Date };
                    d.Views = g.Views;
                    d.Sessions = g.Sessions;
                }
            }
            catch (Exception)
            {
                // GA4 ще не підключено
            }
            // кожен день періоду, і без даних (нуль), щоб графік не стискав пропуски
            var result = new List<SeoDayPoint>();
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                result.Add(days.TryGetValue(day, out var p) ? p : new SeoDayPoint { Day = day });
            return result;
        }

        // «Страва + місто»: кліки й покази (Search Console) та перегляди й сеанси (GA4) сторінок міста за період, за slug
        public async Task<Dictionary<string, SeoSlugStats>> GetDishCityStats(string citySlug, DateTime from, DateTime to)
        {
            var result = new Dictionary<string, SeoSlugStats>();
            SeoSlugStats Get(string slug) => result.TryGetValue(slug, out var s) ? s : result[slug] = new SeoSlugStats { Slug = slug };
            var pattern = $"/(uk|ru|en)/{System.Text.RegularExpressions.Regex.Escape(citySlug ?? "")}/dishes/[^/]+/?$";
            try
            {
                foreach (var r in await _connection.QueryAsync<(string Page, long Clicks, long Impressions)>(
                             "SELECT Page, CAST(SUM(Clicks) AS SIGNED), CAST(SUM(Impressions) AS SIGNED) FROM SeoPageDays WHERE Day BETWEEN @from AND @to AND Page REGEXP @re GROUP BY Page",
                             new { from, to, re = "^https?://[^/]+" + pattern }, commandTimeout: 120))
                {
                    var s = Get(PathOf(r.Page).Split('/').Last());
                    s.Clicks += (int)r.Clicks;
                    s.Impressions += (int)r.Impressions;
                }
            }
            catch (Exception)
            {
                return result; // бот ще не запускався
            }
            try
            {
                foreach (var r in await _connection.QueryAsync<(string Path, long Views, long Sessions)>(
                             "SELECT Path, CAST(SUM(Views) AS SIGNED), CAST(SUM(Sessions) AS SIGNED) FROM GaPageDays WHERE Day BETWEEN @from AND @to AND Path REGEXP @re GROUP BY Path",
                             new { from, to, re = "^" + pattern }, commandTimeout: 120))
                {
                    var s = Get(PathOf(r.Path).Split('/').Last());
                    s.Views += (int)r.Views;
                    s.Sessions += (int)r.Sessions;
                }
            }
            catch (Exception)
            {
                // GA4 ще не підключено
            }
            return result;
        }

        // Дати періоду: last - останній день із даними; period: 1, 7, 28, 90 або custom (from/to)
        public static (DateTime From, DateTime To, DateTime PrevFrom, DateTime PrevTo) Range(DateTime last, string period, DateTime? from, DateTime? to)
        {
            DateTime f, t;
            if (period == "custom" && from.HasValue && to.HasValue && from <= to)
            {
                f = from.Value.Date;
                t = to.Value.Date;
            }
            else
            {
                var days = int.TryParse(period, out var n) && n > 0 && n <= 366 ? n : 7;
                t = last.Date;
                f = t.AddDays(1 - days);
            }
            var length = (t - f).Days + 1;
            return (f, t, f.AddDays(-length), f.AddDays(-1));
        }
    }
}

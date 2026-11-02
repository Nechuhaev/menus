using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class SeoPageWeek
    {
        public string Page { get; set; }
        public string Kind { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public decimal Position { get; set; }
        public int PrevClicks { get; set; }
        public int PrevImpressions { get; set; }
        public decimal? PrevPosition { get; set; }
        public double Ctr => Impressions == 0 ? 0 : 100.0 * Clicks / Impressions;
        public bool IsNew => PrevImpressions == 0 && Impressions > 0;
    }

    public class SeoKindTotal
    {
        public string Kind { get; set; }
        public int Pages { get; set; }
        public int Clicks { get; set; }
        public int Impressions { get; set; }
        public int PrevClicks { get; set; }
        public int PrevImpressions { get; set; }
    }

    // Тижневий звіт SEO: SeoPageWeeks наповнює content-bot/seo_weekly.py з API Search Console,
    // тут - порівняння тижня з попереднім за сторінками й видами сторінок
    public class SeoReportService
    {
        public static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            ["restaurant"] = "Ресторани", ["dish"] = "Страви", ["dishcity"] = "Страва + місто",
            ["article"] = "Статті", ["other"] = "Інші"
        };

        private static readonly Regex Dish = new Regex(@"^/(uk|ru|en)/[^/]+/restaurant/[^/]+/[^/]+/?$");
        private static readonly Regex Restaurant = new Regex(@"^/(uk|ru|en)/[^/]+/restaurant/[^/]+/?$");
        private static readonly Regex DishCity = new Regex(@"^/(uk|ru|en)/[^/]+/dishes/[^/]+/?$");
        private static readonly Regex Article = new Regex(@"^/(uk|ru|en)/(news|blog)/");

        private readonly IDbConnection _connection;

        public SeoReportService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public static string KindOf(string url)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url ?? "";
            if (Dish.IsMatch(path)) return "dish";
            if (Restaurant.IsMatch(path)) return "restaurant";
            if (DishCity.IsMatch(path)) return "dishcity";
            if (Article.IsMatch(path)) return "article";
            return "other";
        }

        public async Task<List<DateTime>> GetWeeks()
        {
            try
            {
                return (await _connection.QueryAsync<DateTime>("SELECT DISTINCT WeekEnd FROM SeoPageWeeks ORDER BY WeekEnd DESC")).ToList();
            }
            catch (Exception)
            {
                return new List<DateTime>(); // таблицю ще не створив бот
            }
        }

        // Сторінки тижня з цифрами попереднього тижня (і ті, що зникли: покази лише в попередньому)
        public async Task<List<SeoPageWeek>> GetPages(DateTime week, DateTime? previous)
        {
            var current = (await _connection.QueryAsync<SeoPageWeek>(
                "SELECT Page, Clicks, Impressions, Position FROM SeoPageWeeks WHERE WeekEnd = @week", new { week })).ToList();
            var before = previous == null ? new Dictionary<string, SeoPageWeek>() : (await _connection.QueryAsync<SeoPageWeek>(
                    "SELECT Page, Clicks, Impressions, Position FROM SeoPageWeeks WHERE WeekEnd = @previous", new { previous }))
                .ToDictionary(x => x.Page);
            foreach (var page in current)
            {
                if (!before.TryGetValue(page.Page, out var old)) continue;
                page.PrevClicks = old.Clicks;
                page.PrevImpressions = old.Impressions;
                page.PrevPosition = old.Position;
                before.Remove(page.Page);
            }
            current.AddRange(before.Values.Select(old => new SeoPageWeek
            {
                Page = old.Page, PrevClicks = old.Clicks, PrevImpressions = old.Impressions, PrevPosition = old.Position
            }));
            foreach (var page in current) page.Kind = KindOf(page.Page);
            return current;
        }

        public static List<SeoKindTotal> Totals(IEnumerable<SeoPageWeek> pages) =>
            pages.GroupBy(x => x.Kind).Select(g => new SeoKindTotal
            {
                Kind = g.Key, Pages = g.Count(x => x.Impressions > 0),
                Clicks = g.Sum(x => x.Clicks), Impressions = g.Sum(x => x.Impressions),
                PrevClicks = g.Sum(x => x.PrevClicks), PrevImpressions = g.Sum(x => x.PrevImpressions)
            }).OrderByDescending(x => x.Impressions).ToList();
    }
}

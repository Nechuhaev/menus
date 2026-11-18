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
    public class CatalogVenue
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Slug { get; set; }
        public string Cuisine { get; set; }
        public string DisplayAddress { get; set; }
        public string Photos { get; set; }
        public string OpenHours { get; set; }
        public string Currency { get; set; }
        public DateTime CreateDate { get; set; }
        public string VenueType { get; set; }
        public string DistrictUk { get; set; }
        public string DistrictRu { get; set; }
        public string DistrictEn { get; set; }
        public string DistrictSlug { get; set; }
        public string Cuisines { get; set; } // slug кухонь через кому (venue_types.py або адмін)
        public bool CuisinesManual { get; set; }
        public List<string> CuisineList => (Cuisines ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        public decimal MedianPrice { get; set; }
        public int Dishes { get; set; }
        public int Ratings { get; set; }
        public int Impressions { get; set; }

        public string Type => string.IsNullOrEmpty(VenueType) ? "restorany" : VenueType;
        public string District(string culture) =>
            (culture == "en" ? DistrictEn : culture == "ru" ? DistrictRu : DistrictUk) ?? DistrictUk ?? DistrictEn;
        public string Localized(string json, string culture) =>
            string.IsNullOrWhiteSpace(json) ? "" : Menus.Web.Models.LocalizableString.FromJson(json).Get(culture, true);
        public string Photo => Menus.Web.Helpers.JsonList.Parse(Photos).FirstOrDefault();
        // популярність: покази сторінки в Google за 28 днів + оцінки страв + повнота меню
        public double Popularity => Impressions + Ratings * 20 + Math.Min(Dishes, 100);
    }

    public class DishPriceRow
    {
        public string RestaurantId { get; set; }
        public decimal Price { get; set; }
        public bool Multiple { get; set; }
        public int RatingsCount { get; set; }
    }

    public class CatalogPageSettings
    {
        public string CityId { get; set; }
        public string VenueType { get; set; } // "" - усі заклади міста
        public bool Enabled { get; set; }
        public string CustomText { get; set; } // JSON {uk, ru, en}
    }

    // Каталог міста: /{culture}/{city}/restaurants і /{culture}/{city}/restaurants/{тип}.
    // Тип і район закладу пише content-bot/venue_types.py (RestaurantVenue), налаштування сторінок - адмінка (CatalogPages).
    public class CatalogService
    {
        public const int MinVenues = 5;

        // slug типу -> назва (множина) uk / ru / en; «restorany» - окремої сторінки немає (це сторінка всього міста)
        public static readonly Dictionary<string, (string Uk, string Ru, string En)> Types = new Dictionary<string, (string, string, string)>
        {
            ["kafe"] = ("Кафе", "Кафе", "Cafes"),
            ["bary"] = ("Бари", "Бары", "Bars"),
            ["paby"] = ("Паби", "Пабы", "Pubs"),
            ["kavyarni"] = ("Кав'ярні", "Кофейни", "Coffee shops"),
            ["pizzerii"] = ("Піцерії", "Пиццерии", "Pizzerias"),
            ["sushi"] = ("Суші-бари", "Суши-бары", "Sushi bars"),
            ["restorany"] = ("Ресторани", "Рестораны", "Restaurants"),
        };

        // кухні: slug -> (uk, ru, en) - «Грузинська кухня» / «Грузинская кухня» / «Georgian»; сторінки /restaurants/kukhnia/{slug}
        public static readonly Dictionary<string, (string Uk, string Ru, string En)> Cuisines = new Dictionary<string, (string, string, string)>
        {
            ["hruzynska"] = ("Грузинська кухня", "Грузинская кухня", "Georgian"),
            ["italiiska"] = ("Італійська кухня", "Итальянская кухня", "Italian"),
            ["yaponska"] = ("Японська кухня", "Японская кухня", "Japanese"),
            ["ukrainska"] = ("Українська кухня", "Украинская кухня", "Ukrainian"),
            ["yevropeiska"] = ("Європейська кухня", "Европейская кухня", "European"),
            ["aziiska"] = ("Азійська кухня", "Азиатская кухня", "Asian"),
            ["kytaiska"] = ("Китайська кухня", "Китайская кухня", "Chinese"),
            ["koreiska"] = ("Корейська кухня", "Корейская кухня", "Korean"),
            ["vietnamska"] = ("В'єтнамська кухня", "Вьетнамская кухня", "Vietnamese"),
            ["taiska"] = ("Тайська кухня", "Тайская кухня", "Thai"),
            ["uzbetska"] = ("Узбецька кухня", "Узбекская кухня", "Uzbek"),
            ["krymskotatarska"] = ("Кримськотатарська кухня", "Крымскотатарская кухня", "Crimean Tatar"),
            ["virmenska"] = ("Вірменська кухня", "Армянская кухня", "Armenian"),
            ["yevreiska"] = ("Єврейська кухня", "Еврейская кухня", "Jewish"),
            ["amerykanska"] = ("Американська кухня", "Американская кухня", "American"),
            ["meksykanska"] = ("Мексиканська кухня", "Мексиканская кухня", "Mexican"),
            ["frantsuzka"] = ("Французька кухня", "Французская кухня", "French"),
            ["seredzemnomorska"] = ("Середземноморська кухня", "Средиземноморская кухня", "Mediterranean"),
            ["ryba-moreprodukty"] = ("Риба й морепродукти", "Рыба и морепродукты", "Fish & seafood"),
            ["miasna"] = ("М'ясо та стейки", "Мясо и стейки", "Steak & meat"),
            ["vegetarianska"] = ("Вегетаріанська кухня", "Вегетарианская кухня", "Vegetarian"),
        };

        public static string CuisineName(string cuisine, string culture)
        {
            if (string.IsNullOrEmpty(cuisine) || !Cuisines.TryGetValue(cuisine, out var c)) return cuisine ?? "";
            return culture == "en" ? c.En : culture == "ru" ? c.Ru : c.Uk;
        }

        // ключ налаштувань сторінки кухні в CatalogPages.VenueType
        public static string CuisineKey(string cuisine) => "c:" + cuisine;

        public static bool IsCuisinePublished(string cuisine, int venues, CatalogPageSettings settings) =>
            Cuisines.ContainsKey(cuisine ?? "") && venues >= MinVenues && (settings?.Enabled ?? true);

        // кухні міста, що мають сторінку: (кухня, закладів)
        public async Task<List<(string Cuisine, int Venues)>> PublishedCuisines(string cityId)
        {
            var settings = await GetSettings();
            return (await GetCity(cityId)).SelectMany(v => v.CuisineList).GroupBy(c => c)
                .Select(g => (Cuisine: g.Key, Venues: g.Count()))
                .Where(x => IsCuisinePublished(x.Cuisine, x.Venues, settings.FirstOrDefault(s => s.CityId == cityId && s.VenueType == CuisineKey(x.Cuisine))))
                .OrderByDescending(x => x.Venues).ToList();
        }

        public static string TypeName(string type, string culture)
        {
            if (string.IsNullOrEmpty(type) || !Types.TryGetValue(type, out var t)) t = Types["restorany"];
            return culture == "en" ? t.En : culture == "ru" ? t.Ru : t.Uk;
        }

        private static List<CatalogVenue> _all;
        private static DateTime _loadedAt;
        private static readonly System.Threading.SemaphoreSlim Lock = new System.Threading.SemaphoreSlim(1, 1);
        private static bool _tablesReady;
        private readonly IDbConnection _connection;

        public CatalogService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task Ensure()
        {
            if (_tablesReady) return;
            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS RestaurantVenue (
                    RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
                    VenueType VARCHAR(32) NOT NULL,
                    TypeReason VARCHAR(255) NULL,
                    TypeManual TINYINT(1) NOT NULL DEFAULT 0,
                    DistrictUk VARCHAR(255) NULL,
                    DistrictRu VARCHAR(255) NULL,
                    DistrictEn VARCHAR(255) NULL,
                    DistrictSlug VARCHAR(128) NULL,
                    DistrictManual TINYINT(1) NOT NULL DEFAULT 0,
                    Lat VARCHAR(32) NULL,
                    Lon VARCHAR(32) NULL,
                    UpdatedAt DATETIME NOT NULL
                ) DEFAULT CHARSET=utf8mb4;
                CREATE TABLE IF NOT EXISTS CatalogPages (
                    CityId VARCHAR(255) NOT NULL,
                    VenueType VARCHAR(32) NOT NULL,
                    Enabled TINYINT(1) NOT NULL DEFAULT 1,
                    CustomText TEXT NULL,
                    PRIMARY KEY (CityId, VenueType)
                ) DEFAULT CHARSET=utf8mb4;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS Cuisines VARCHAR(500) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS CuisinesReason VARCHAR(255) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS CuisinesManual TINYINT(1) NOT NULL DEFAULT 0;");
            _tablesReady = true;
        }

        // усі активні заклади з типом, районом і цінами; кеш 30 хв (сторінки й так у EdgeCache)
        public async Task<List<CatalogVenue>> GetAll(bool fresh = false)
        {
            if (!fresh && _all != null && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(30)) return _all;
            await Lock.WaitAsync();
            try
            {
                if (!fresh && _all != null && DateTime.UtcNow - _loadedAt < TimeSpan.FromMinutes(30)) return _all;
                await Ensure();
                var venues = (await _connection.QueryAsync<CatalogVenue>(@"
                    SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, rs.Url AS Slug, r.CreateDate,
                           ri.Cuisine, ri.DisplayAddress, ri.Photos, ri.OpenHours, ri.Currency,
                           v.VenueType, v.DistrictUk, v.DistrictRu, v.DistrictEn, v.DistrictSlug,
                           v.Cuisines, IFNULL(v.CuisinesManual, 0) AS CuisinesManual
                    FROM Restaurants r
                    JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                    LEFT JOIN RestaurantInfo ri ON ri.RestaurantId = r.Id
                    LEFT JOIN RestaurantVenue v ON v.RestaurantId = r.Id
                    WHERE r.Status = 0", commandTimeout: 120)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());

                // ціни страв (без «за 100 г»), кількість страв і оцінок
                var prices = new Dictionary<string, List<decimal>>();
                foreach (var d in await _connection.QueryAsync<DishPriceRow>(@"
                    SELECT CONCAT(d.RestaurantId, '') AS RestaurantId, d.Price, d.IsPriceMultiple AS Multiple, d.RatingsCount
                    FROM Dishes d JOIN Restaurants r ON r.Id = d.RestaurantId
                    WHERE d.Status = 0 AND r.Status = 0", commandTimeout: 120))
                {
                    if (!venues.TryGetValue(d.RestaurantId, out var v)) continue;
                    v.Dishes++;
                    v.Ratings += d.RatingsCount;
                    if (d.Price > 0 && !d.Multiple)
                    {
                        if (!prices.TryGetValue(d.RestaurantId, out var list)) prices[d.RestaurantId] = list = new List<decimal>();
                        list.Add(d.Price);
                    }
                }
                foreach (var p in prices)
                {
                    p.Value.Sort();
                    venues[p.Key].MedianPrice = p.Value[p.Value.Count / 2];
                }

                // покази сторінки закладу в Google за 28 днів (SeoPageDays, якщо бот уже збирає)
                try
                {
                    var bySlug = venues.Values.Where(x => !string.IsNullOrEmpty(x.Slug))
                        .GroupBy(x => x.Slug.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToList());
                    foreach (var row in await _connection.QueryAsync<(string Slug, long Impressions)>(@"
                        SELECT LOWER(SUBSTRING_INDEX(SUBSTRING_INDEX(Page, '/restaurant/', -1), '/', 1)), CAST(SUM(Impressions) AS SIGNED)
                        FROM SeoPageDays
                        WHERE Day >= DATE_SUB((SELECT MAX(Day) FROM SeoPageDays), INTERVAL 28 DAY)
                          AND Page REGEXP '/restaurant/[^/]+/?$'
                        GROUP BY 1", commandTimeout: 120))
                    {
                        if (bySlug.TryGetValue(Uri.UnescapeDataString(row.Slug ?? ""), out var list))
                            foreach (var v in list) v.Impressions += (int)row.Impressions;
                    }
                }
                catch (Exception)
                {
                    // seo_daily.py ще не запускався
                }

                _all = venues.Values.ToList();
                _loadedAt = DateTime.UtcNow;
                return _all;
            }
            finally
            {
                Lock.Release();
            }
        }

        public async Task<List<CatalogVenue>> GetCity(string cityId) =>
            (await GetAll()).Where(x => x.City == cityId).ToList();

        public async Task<List<CatalogPageSettings>> GetSettings()
        {
            await Ensure();
            return (await _connection.QueryAsync<CatalogPageSettings>("SELECT CityId, VenueType, Enabled, CustomText FROM CatalogPages")).ToList();
        }

        public async Task<CatalogPageSettings> GetSettings(string cityId, string type) =>
            (await GetSettings()).FirstOrDefault(x => x.CityId == cityId && x.VenueType == (type ?? ""))
            ?? new CatalogPageSettings { CityId = cityId, VenueType = type ?? "", Enabled = true };

        public async Task SaveSettings(string cityId, string type, bool enabled, string customTextJson)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"
                INSERT INTO CatalogPages (CityId, VenueType, Enabled, CustomText) VALUES (@cityId, @type, @enabled, @customTextJson)
                ON DUPLICATE KEY UPDATE Enabled = @enabled, CustomText = @customTextJson",
                new { cityId, type = type ?? "", enabled, customTextJson });
        }

        // сторінка показується: тип відомий, закладів >= MinVenues, адмін не вимкнув
        public static bool IsPublished(string type, int venues, CatalogPageSettings settings) =>
            (string.IsNullOrEmpty(type) || (Types.ContainsKey(type) && type != "restorany")) && venues >= MinVenues && (settings?.Enabled ?? true);

        // типи міста, що мають сторінку: (тип, закладів)
        public async Task<List<(string Type, int Venues)>> PublishedTypes(string cityId)
        {
            var settings = await GetSettings();
            return (await GetCity(cityId)).GroupBy(x => x.Type)
                .Select(g => (Type: g.Key, Venues: g.Count()))
                .Where(x => IsPublished(x.Type, x.Venues, settings.FirstOrDefault(s => s.CityId == cityId && s.VenueType == x.Type)))
                .OrderByDescending(x => x.Venues).ToList();
        }

        // ручна зміна типу / району з адмінки: бот її більше не перезаписує
        public async Task SetVenue(string restaurantId, string type, string districtUk, string districtRu, string districtEn, string districtSlug,
            IEnumerable<string> cuisines = null)
        {
            await Ensure();
            if (cuisines != null)
            {
                var list = string.Join(",", cuisines.Where(c => Cuisines.ContainsKey(c)).Distinct());
                await _connection.ExecuteAsync(@"
                    INSERT INTO RestaurantVenue (RestaurantId, VenueType, Cuisines, CuisinesReason, CuisinesManual, UpdatedAt)
                    VALUES (@restaurantId, 'restorany', @list, 'вручну (адмін)', 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE Cuisines = @list, CuisinesReason = 'вручну (адмін)', CuisinesManual = 1, UpdatedAt = UTC_TIMESTAMP()",
                    new { restaurantId, list = list == "" ? null : list });
            }
            if (!string.IsNullOrEmpty(type) && Types.ContainsKey(type))
                await _connection.ExecuteAsync(@"
                    INSERT INTO RestaurantVenue (RestaurantId, VenueType, TypeReason, TypeManual, UpdatedAt) VALUES (@restaurantId, @type, 'вручну (адмін)', 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE VenueType = @type, TypeReason = 'вручну (адмін)', TypeManual = 1, UpdatedAt = UTC_TIMESTAMP()",
                    new { restaurantId, type });
            if (districtSlug != null)
                await _connection.ExecuteAsync(@"
                    INSERT INTO RestaurantVenue (RestaurantId, VenueType, DistrictUk, DistrictRu, DistrictEn, DistrictSlug, DistrictManual, UpdatedAt)
                    VALUES (@restaurantId, 'restorany', @districtUk, @districtRu, @districtEn, @districtSlug, 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE DistrictUk = @districtUk, DistrictRu = @districtRu, DistrictEn = @districtEn,
                                            DistrictSlug = @districtSlug, DistrictManual = 1, UpdatedAt = UTC_TIMESTAMP()",
                    new { restaurantId, districtUk, districtRu, districtEn, districtSlug = districtSlug == "" ? null : districtSlug });
            _all = null; // наступний запит перечитає базу
        }
    }
}

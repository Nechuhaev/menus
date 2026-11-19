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
        public string Features { get; set; } // особливості через кому (venue_types.py або адмін); «nedorogo» - рахується за цінами
        public bool FeaturesManual { get; set; }
        public decimal? LunchPrice { get; set; }
        public string ChainSlug { get; set; } // мережа: заклади з однаковою назвою в місті (venue_types.py або адмін)
        public string ChainName { get; set; }
        public bool ChainManual { get; set; }
        public List<string> FeatureList => (Features ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
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

        // особливості: slug -> (кнопка uk/ru/en, заголовок uk/ru/en); у заголовку {gen} - «Києва», {loc} - «в Києві», {city} - «Kyiv»
        public static readonly Dictionary<string, (string Uk, string Ru, string En, string HUk, string HRu, string HEn)> Features =
            new Dictionary<string, (string, string, string, string, string, string)>
            {
                ["nedorogo"] = ("Недорого", "Недорого", "Budget",
                    "Недорогі ресторани й кафе {gen}", "Недорогие рестораны и кафе {gen}", "Budget restaurants & cafes in {city}"),
                ["tsilodobovo"] = ("Цілодобово", "Круглосуточно", "24/7",
                    "Цілодобові заклади {gen}", "Круглосуточные заведения {gen}", "24/7 restaurants & cafes in {city}"),
                ["snidanky"] = ("Сніданки", "Завтраки", "Breakfast",
                    "Сніданки {loc}: де поснідати", "Завтраки {loc}: где позавтракать", "Breakfast in {city}"),
                ["biznes-lanch"] = ("Бізнес-ланч", "Бизнес-ланч", "Business lunch",
                    "Бізнес-ланчі {loc}", "Бизнес-ланчи {loc}", "Business lunch in {city}"),
                ["kalian"] = ("Кальян", "Кальян", "Hookah",
                    "Заклади з кальяном {gen}", "Заведения с кальяном {gen}", "Hookah places in {city}"),
                ["dytiache-menu"] = ("Дитяче меню", "Детское меню", "Kids menu",
                    "Заклади з дитячим меню {gen}", "Заведения с детским меню {gen}", "Restaurants with kids menu in {city}"),
            };

        public static string FeatureName(string feature, string culture)
        {
            if (string.IsNullOrEmpty(feature) || !Features.TryGetValue(feature, out var f)) return feature ?? "";
            return culture == "en" ? f.En : culture == "ru" ? f.Ru : f.Uk;
        }

        public static string FeatureKey(string feature) => "f:" + feature;

        // заклад має особливість; «недорого» - типова ціна страви не вище межі міста
        public static bool HasFeature(CatalogVenue v, string feature, decimal cheapLimit) =>
            feature == "nedorogo" ? cheapLimit > 0 && v.MedianPrice > 0 && v.MedianPrice <= cheapLimit : v.FeatureList.Contains(feature);

        // «недорого»: типова ціна страви закладу в нижній третині міста
        public static decimal CheapLimit(List<CatalogVenue> venues)
        {
            var medians = venues.Where(x => x.MedianPrice > 0).Select(x => x.MedianPrice).OrderBy(x => x).ToList();
            return medians.Count < 9 ? 0 : medians[medians.Count / 3];
        }

        public static bool IsFeaturePublished(string feature, int venues, CatalogPageSettings settings) =>
            Features.ContainsKey(feature ?? "") && venues >= MinVenues && (settings?.Enabled ?? true);

        public async Task<List<(string Feature, int Venues)>> PublishedFeatures(string cityId)
        {
            var settings = await GetSettings();
            var venues = await GetCity(cityId);
            var limit = CheapLimit(venues);
            return Features.Keys.Select(f => (Feature: f, Venues: venues.Count(v => HasFeature(v, f, limit))))
                .Where(x => IsFeaturePublished(x.Feature, x.Venues, settings.FirstOrDefault(s => s.CityId == cityId && s.VenueType == FeatureKey(x.Feature))))
                .ToList();
        }

        // мережі: сторінка /restaurants/merezha/{slug}, якщо в місті від ChainMin закладів мережі
        public const int ChainMin = 2;

        public static string ChainKey(string chain) => "m:" + chain;

        public static bool IsChainPublished(int venues, CatalogPageSettings settings) =>
            venues >= ChainMin && (settings?.Enabled ?? true);

        // мережі міста: (slug, назва, закладів) - усі з ChainSlug, published - лише ті, що мають сторінку
        public static List<(string Slug, string Name, int Venues)> Chains(IEnumerable<CatalogVenue> venues) =>
            venues.Where(v => !string.IsNullOrEmpty(v.ChainSlug)).GroupBy(v => v.ChainSlug)
                .Select(g => (Slug: g.Key, Name: g.GroupBy(v => v.ChainName ?? v.Name).OrderByDescending(n => n.Count()).First().Key, Venues: g.Count()))
                .OrderByDescending(x => x.Venues).ThenBy(x => x.Name).ToList();

        public async Task<List<(string Slug, string Name, int Venues)>> PublishedChains(string cityId)
        {
            var settings = await GetSettings();
            return Chains(await GetCity(cityId))
                .Where(x => IsChainPublished(x.Venues, settings.FirstOrDefault(s => s.CityId == cityId && s.VenueType == ChainKey(x.Slug))))
                .ToList();
        }

        // ключ назви мережі (як chain_key у venue_types.py): без регістру, пробілів і розділових знаків
        public static string ChainNameKey(string name) =>
            System.Text.RegularExpressions.Regex.Replace((UrlGenerateService.Translit(name ?? "") ?? "").ToLowerInvariant()
                .Replace("j", "i").Replace("y", "i").Replace("kh", "h").Replace("ts", "c"), "[^a-z0-9]", "");

        // сторінки «страва + місто», що належать кухні (за назвою страви uk)
        public static readonly Dictionary<string, string> CuisineDishes = new Dictionary<string, string>
        {
            ["hruzynska"] = "хінкал|хачапур|чахохбіл|оджахур|чашушул|пхалі|лобіо|сацив|шкмерул|аджапсандал",
            ["italiiska"] = "піц|паст|спагет|карбонар|різот|лазань|тірамісу|брускет|равіол",
            ["yaponska"] = "суші|рол|рамен|сашимі|темпур|місо|удон",
            ["ukrainska"] = "борщ|варени|деруни|голубц|сирник|галушк|холодец|банош|пампуш",
            ["uzbetska"] = "плов|лагман|самс|манти|шурп",
            ["aziiska"] = "вок|рамен|пад тай|том ям|фо бо|локшин",
            ["kytaiska"] = "вок|локшин|качк|димсам|дім сам",
            ["amerykanska"] = "бургер|ребр|крильц|хот.?дог|наггетс",
            ["ryba-moreprodukty"] = "лосос|форел|мідії|устриц|креветк|кальмар|дорадо|сібас|судак",
            ["miasna"] = "стейк|рибай|стриплойн|тібон|ребр|шашлик|телятин",
            ["frantsuzka"] = "круасан|рататуй|крем.?брюле|кіш|фуа",
            ["seredzemnomorska"] = "хумус|фалафел|гірос|грецьк|дорадо|сібас",
            ["yevreiska"] = "хумус|фалафел|шакшук|форшмак|фаршир",
            ["meksykanska"] = "тако|буріто|кесаділ|начос|гуакамол",
            ["virmenska"] = "хоровац|лаваш|толма|долма",
            ["krymskotatarska"] = "чебурек|янтик|плов|манти|лагман",
            ["koreiska"] = "кімчі|бібімбап|пулькогі|токпокі",
            ["vietnamska"] = "фо бо|бун бо|спрінг|нем",
            ["taiska"] = "пад тай|том ям|том кха|карі",
            ["vegetarianska"] = "веган|овоч|салат|фалафел|хумус",
        };

        // ---- перелінковка: посилання на опубліковані сторінки каталогу (лише ті, що є на сайті й не вимкнені в адмінці) ----

        // тип, особливості й кухні закладу - для сторінки закладу
        public async Task<List<(string Href, string Title)>> VenueLinks(CatalogVenue v, string citySlug, string cityName, string culture, int max = 6)
        {
            var result = new List<(string, string)>();
            var basePath = $"/{culture}/{citySlug}/restaurants";
            if ((await PublishedTypes(v.City)).Any(x => x.Type == v.Type))
                result.Add(($"{basePath}/{v.Type}", Menus.Web.Helpers.CatalogText.Heading(TypeName(v.Type, culture), citySlug, cityName, culture)));
            var cheap = CheapLimit(await GetCity(v.City));
            foreach (var (f, _) in await PublishedFeatures(v.City))
                if (HasFeature(v, f, cheap)) result.Add(($"{basePath}/{f}", FeatureHeadingText(f, citySlug, cityName, culture)));
            var cuisines = (await PublishedCuisines(v.City)).Select(x => x.Cuisine).ToList();
            foreach (var c in v.CuisineList.Where(cuisines.Contains))
                result.Add(($"{basePath}/kukhnia/{c}", Menus.Web.Helpers.CatalogText.CuisineHeading(CuisineName(c, culture), citySlug, cityName, culture)));
            return result.Take(max).ToList();
        }

        // кухні, до яких належить страва (за назвою uk) - для сторінки «страва + місто»
        public async Task<List<(string Href, string Title)>> DishLinks(string cityId, string dishTitleUk, string citySlug, string cityName, string culture)
        {
            var result = new List<(string, string)>();
            foreach (var (c, _) in await PublishedCuisines(cityId))
                if (CuisineDishes.TryGetValue(c, out var rx) && System.Text.RegularExpressions.Regex.IsMatch(dishTitleUk ?? "", rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    result.Add(($"/{culture}/{citySlug}/restaurants/kukhnia/{c}", Menus.Web.Helpers.CatalogText.CuisineHeading(CuisineName(c, culture), citySlug, cityName, culture)));
            return result.Take(3).ToList();
        }

        // усі заклади міста + типи - для хабу страв міста
        public async Task<List<(string Href, string Title)>> CityLinks(string cityId, string citySlug, string cityName, string culture)
        {
            var result = new List<(string, string)>();
            var basePath = $"/{culture}/{citySlug}/restaurants";
            var venues = await GetCity(cityId);
            if (!IsPublished(null, venues.Count, await GetSettings(cityId, ""))) return result;
            result.Add((basePath, Menus.Web.Helpers.CatalogText.Heading(TypeName("restorany", culture), citySlug, cityName, culture)));
            foreach (var (t, _) in await PublishedTypes(cityId))
                result.Add(($"{basePath}/{t}", Menus.Web.Helpers.CatalogText.Heading(TypeName(t, culture), citySlug, cityName, culture)));
            foreach (var (f, _) in await PublishedFeatures(cityId))
                result.Add(($"{basePath}/{f}", FeatureHeadingText(f, citySlug, cityName, culture)));
            return result;
        }

        public static string FeatureHeadingText(string f, string citySlug, string cityName, string culture)
        {
            var info = Features[f];
            return Menus.Web.Helpers.CatalogText.FeatureHeading(culture == "en" ? info.HEn : culture == "ru" ? info.HRu : info.HUk, citySlug, cityName, culture);
        }

        // міста, де є сторінка «усі заклади» - для футера; легкий запит з окремим кешем 30 хв (без цін і страв)
        private static List<string> _catalogCities;
        private static DateTime _catalogCitiesAt;

        // з готового кешу закладів, без запитів до бази (футер є на кожній сторінці)
        public Task<List<string>> CatalogCityIds()
        {
            if (_catalogCities != null && DateTime.UtcNow - _catalogCitiesAt < TimeSpan.FromMinutes(5)) return Task.FromResult(_catalogCities);
            var all = _all;
            var settings = _settings ?? new List<CatalogPageSettings>();
            if (all == null) return Task.FromResult(_catalogCities ?? new List<string>());
            _catalogCities = all.GroupBy(x => x.City)
                .Where(g => IsPublished(null, g.Count(), settings.FirstOrDefault(s => s.CityId == g.Key && s.VenueType == "")))
                .Select(g => g.Key).ToList();
            _catalogCitiesAt = DateTime.UtcNow;
            return Task.FromResult(_catalogCities);
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
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS CuisinesManual TINYINT(1) NOT NULL DEFAULT 0;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS Features VARCHAR(255) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS FeaturesReason VARCHAR(255) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS FeaturesManual TINYINT(1) NOT NULL DEFAULT 0;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS LunchPrice DECIMAL(10,2) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS ChainSlug VARCHAR(128) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS ChainName VARCHAR(255) NULL;
                ALTER TABLE RestaurantVenue ADD COLUMN IF NOT EXISTS ChainManual TINYINT(1) NOT NULL DEFAULT 0;
                ALTER TABLE CatalogPages MODIFY VenueType VARCHAR(160) NOT NULL;");
            _tablesReady = true;
        }

        // усі активні заклади з типом, районом і цінами; кеш 30 хв (сторінки й так у EdgeCache)
        public async Task<List<CatalogVenue>> GetAll(bool fresh = false)
        {
            // сторінки закладів, страв і футер: лише готовий кеш, без очікування на перечитування бази
            // (інакше під час оновлення кешу черга запитів забирає всі з'єднання з базою і сайт висне)
            if (_cachedOnly && !fresh) return _all ?? new List<CatalogVenue>();
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
                           v.Cuisines, IFNULL(v.CuisinesManual, 0) AS CuisinesManual,
                           v.Features, IFNULL(v.FeaturesManual, 0) AS FeaturesManual, v.LunchPrice,
                           v.ChainSlug, v.ChainName, IFNULL(v.ChainManual, 0) AS ChainManual
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

        // тип, район і кухні всіх закладів напряму з RestaurantVenue (без 30-хвилинного кешу) - для таблиць адмінки
        public async Task<Dictionary<string, CatalogVenue>> GetVenueMap()
        {
            await Ensure();
            return (await _connection.QueryAsync<CatalogVenue>(@"
                SELECT CONCAT(RestaurantId, '') AS Id, VenueType, DistrictUk, DistrictRu, DistrictEn, DistrictSlug,
                       Cuisines, IFNULL(CuisinesManual, 0) AS CuisinesManual,
                       Features, IFNULL(FeaturesManual, 0) AS FeaturesManual, LunchPrice,
                       ChainSlug, ChainName, IFNULL(ChainManual, 0) AS ChainManual
                FROM RestaurantVenue")).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        }

        public async Task<List<CatalogVenue>> GetCity(string cityId) =>
            (await GetAll()).Where(x => x.City == cityId).ToList();

        // налаштування сторінок: кеш 2 хв (читаються на кожній сторінці закладу), збереження в адмінці скидає кеш
        private static List<CatalogPageSettings> _settings;
        private static DateTime _settingsAt;

        public async Task<List<CatalogPageSettings>> GetSettings()
        {
            if (_settings != null && DateTime.UtcNow - _settingsAt < TimeSpan.FromMinutes(2)) return _settings;
            if (_cachedOnly && _settings != null) return _settings;
            await Ensure();
            _settings = (await _connection.QueryAsync<CatalogPageSettings>("SELECT CityId, VenueType, Enabled, CustomText FROM CatalogPages")).ToList();
            _settingsAt = DateTime.UtcNow;
            return _settings;
        }

        // режим «лише кеш» для цього запиту (сервіс scoped - один на запит)
        private bool _cachedOnly;

        public CatalogService CachedOnly()
        {
            _cachedOnly = true;
            return this;
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
            _settings = null;
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
            IEnumerable<string> cuisines = null, IEnumerable<string> features = null, string chainSlug = null, string chainName = null)
        {
            await Ensure();
            if (chainSlug != null) // "" - не мережа
                await _connection.ExecuteAsync(@"
                    INSERT INTO RestaurantVenue (RestaurantId, VenueType, ChainSlug, ChainName, ChainManual, UpdatedAt)
                    VALUES (@restaurantId, 'restorany', @slug, @name, 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE ChainSlug = @slug, ChainName = @name, ChainManual = 1, UpdatedAt = UTC_TIMESTAMP()",
                    new { restaurantId, slug = chainSlug == "" ? null : chainSlug, name = chainSlug == "" ? null : chainName });
            if (features != null)
            {
                var list = string.Join(",", features.Where(f => Features.ContainsKey(f) && f != "nedorogo").Distinct());
                await _connection.ExecuteAsync(@"
                    INSERT INTO RestaurantVenue (RestaurantId, VenueType, Features, FeaturesReason, FeaturesManual, UpdatedAt)
                    VALUES (@restaurantId, 'restorany', @list, 'вручну (адмін)', 1, UTC_TIMESTAMP())
                    ON DUPLICATE KEY UPDATE Features = @list, FeaturesReason = 'вручну (адмін)', FeaturesManual = 1, UpdatedAt = UTC_TIMESTAMP()",
                    new { restaurantId, list = list == "" ? null : list });
            }
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

using System;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    // Вигляд iframe ресторану (меню для сайту закладу): обкладинка, колір, тема, що показувати.
    // Редагують адмін і власник у кабінеті «Про заклад» (блок «Меню для вашого сайту»).
    // Значення за замовчуванням - фірмовий вигляд TipMyMenu і все показується (Hide* = false)
    public class IframeDesign
    {
        public string RestaurantId { get; set; }
        // "" - перше фото закладу (або фірмовий фон, якщо фото немає); "brand" - фірмовий фон; інакше - id фото закладу
        public string Cover { get; set; } = "";
        public string Accent { get; set; } = DefaultAccent;
        public string Theme { get; set; } = "light"; // light | dark
        public bool HidePhotos { get; set; }
        public bool HideDescriptions { get; set; }
        public bool HideWeight { get; set; }
        public bool HideLanguages { get; set; }

        public const string DefaultAccent = "#ffb80e";
        public bool Dark => Theme == "dark";

        // текст на кнопці кольору акценту: темний на світлому акценті, білий на темному
        public string AccentText
        {
            get
            {
                var hex = (Accent ?? DefaultAccent).TrimStart('#');
                if (hex.Length != 6) return "#231f20";
                double C(int i) => int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
                var luminance = 0.2126 * C(0) + 0.7152 * C(2) + 0.0722 * C(4);
                return luminance > 0.55 ? "#231f20" : "#ffffff";
            }
        }
    }

    public class IframeDesignService
    {
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS IframeDesign (
                RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
                Cover VARCHAR(255) NOT NULL DEFAULT '',
                Accent VARCHAR(16) NOT NULL DEFAULT '#ffb80e',
                Theme VARCHAR(16) NOT NULL DEFAULT 'light',
                HidePhotos TINYINT(1) NOT NULL DEFAULT 0,
                HideDescriptions TINYINT(1) NOT NULL DEFAULT 0,
                HideWeight TINYINT(1) NOT NULL DEFAULT 0,
                HideLanguages TINYINT(1) NOT NULL DEFAULT 0,
                UpdatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4";

        private static readonly Regex HexColor = new Regex("^#[0-9a-fA-F]{6}$");
        private static bool _tableReady;
        // iframe відкривають часто - кеш на 5 хвилин, скидається при збереженні
        private static readonly ConcurrentDictionary<string, (DateTime At, IframeDesign Design)> Cache = new ConcurrentDictionary<string, (DateTime, IframeDesign)>();

        private readonly IDbConnection _connection;

        public IframeDesignService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_tableReady) return;
            await _connection.ExecuteAsync(CreateTableSql);
            _tableReady = true;
        }

        public async Task<IframeDesign> Get(string restaurantId)
        {
            if (string.IsNullOrEmpty(restaurantId)) return new IframeDesign();
            if (Cache.TryGetValue(restaurantId, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromMinutes(5)) return cached.Design;
            await Ensure();
            var design = await _connection.QueryFirstOrDefaultAsync<IframeDesign>(
                "SELECT * FROM IframeDesign WHERE RestaurantId = @restaurantId", new { restaurantId })
                ?? new IframeDesign { RestaurantId = restaurantId };
            Cache[restaurantId] = (DateTime.UtcNow, design);
            return design;
        }

        public async Task Save(IframeDesign d)
        {
            await Ensure();
            d.Accent = d.Accent != null && HexColor.IsMatch(d.Accent) ? d.Accent.ToLowerInvariant() : IframeDesign.DefaultAccent;
            d.Theme = d.Theme == "dark" ? "dark" : "light";
            d.Cover = (d.Cover ?? "").Trim();
            if (d.Cover.Length > 255) d.Cover = "";
            await _connection.ExecuteAsync(@"
                INSERT INTO IframeDesign (RestaurantId, Cover, Accent, Theme, HidePhotos, HideDescriptions, HideWeight, HideLanguages, UpdatedAt)
                VALUES (@RestaurantId, @Cover, @Accent, @Theme, @HidePhotos, @HideDescriptions, @HideWeight, @HideLanguages, NOW())
                ON DUPLICATE KEY UPDATE Cover = VALUES(Cover), Accent = VALUES(Accent), Theme = VALUES(Theme), HidePhotos = VALUES(HidePhotos),
                    HideDescriptions = VALUES(HideDescriptions), HideWeight = VALUES(HideWeight), HideLanguages = VALUES(HideLanguages), UpdatedAt = NOW()", d);
            Cache.TryRemove(d.RestaurantId, out _);
        }
    }
}

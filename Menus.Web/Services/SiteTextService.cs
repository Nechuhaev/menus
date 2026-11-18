using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Menus.Web.Services
{
    public class SitePageInfo
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }          // без мови: "iframe-menu" -> /uk/iframe-menu
        public string ResourceView { get; set; } // "Views.Articles.IframeMenu" (Resources/Views.Articles.IframeMenu.uk.resx)
        public string[] Langs { get; set; }
        public string[] Keys { get; set; }       // порядок полів у редакторі (як на сторінці)
    }

    // Тексти статичних сторінок (лендингів), які адмін правит у «Страницы» → «Лендинги»: правка зберігається в SiteTexts,
    // без правки - текст із файлу перекладу (.resx). Порожнє поле в редакторі - повернути текст за замовчуванням.
    public class SiteTextService
    {
        public static readonly List<SitePageInfo> Pages = new List<SitePageInfo>
        {
            new SitePageInfo
            {
                Key = "iframe-menu", Title = "Меню для сайту закладу (iframe)", Url = "iframe-menu",
                ResourceView = "Views.Articles.IframeMenu", Langs = new[] { "uk", "ru" },
                Keys = new[] { "MetaTitle", "MetaDescription", "Badge", "HeroTitle1", "HeroTitleAccent", "HeroTitle2", "HeroLead",
                    "CtaConnect", "CtaHow", "FactCode", "FactCodeText", "FactPrice", "FactPriceText", "FactLangs", "FactLangsText",
                    "PhoneTitle", "PhoneNote", "BenefitsTitle", "BenefitsSub" }
                    .Concat(Enumerable.Range(1, 10).SelectMany(i => new[] { $"B{i}Title", $"B{i}Text" }))
                    .Concat(new[] { "HowTitle", "HowSub" })
                    .Concat(Enumerable.Range(1, 3).SelectMany(i => new[] { $"S{i}Title", $"S{i}Text" }))
                    .Concat(new[] { "CodeTitle", "CodeNote", "FeaturesTitle" })
                    .Concat(Enumerable.Range(1, 10).Select(i => $"F{i}"))
                    .Concat(new[] { "FaqTitle" })
                    .Concat(Enumerable.Range(1, 7).SelectMany(i => new[] { $"Q{i}", $"A{i}" }))
                    .Concat(new[] { "FinalTitle", "FinalText" }).ToArray()
            },
        };

        private static Dictionary<string, string> _cache; // "page|lang|key" -> текст
        private static DateTime _cacheAt;
        private static bool _tableReady;
        private readonly IDbConnection _connection;
        private readonly IStringLocalizerFactory _localizers;

        public SiteTextService(EfDbContext dbContext, IStringLocalizerFactory localizers)
        {
            _connection = dbContext.Database.GetDbConnection();
            _localizers = localizers;
        }

        private async Task Ensure()
        {
            if (_tableReady) return;
            await _connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS SiteTexts (
                    PageKey VARCHAR(64) NOT NULL,
                    Lang VARCHAR(5) NOT NULL,
                    TextKey VARCHAR(128) NOT NULL,
                    Value TEXT NOT NULL,
                    UpdatedAt DATETIME NOT NULL,
                    PRIMARY KEY (PageKey, Lang, TextKey)
                ) DEFAULT CHARSET=utf8mb4");
            _tableReady = true;
        }

        // правки сторінки мовою lang: ключ -> текст (для сторінки сайту; кеш 5 хв, скидається при збереженні)
        public async Task<Dictionary<string, string>> GetOverrides(string page, string lang)
        {
            try
            {
                if (_cache == null || DateTime.UtcNow - _cacheAt > TimeSpan.FromMinutes(5))
                {
                    await Ensure();
                    _cache = (await _connection.QueryAsync<(string PageKey, string Lang, string TextKey, string Value)>(
                            "SELECT PageKey, Lang, TextKey, Value FROM SiteTexts"))
                        .ToDictionary(x => $"{x.PageKey}|{x.Lang}|{x.TextKey}", x => x.Value);
                    _cacheAt = DateTime.UtcNow;
                }
                var prefix = $"{page}|{lang}|";
                return _cache.Where(x => x.Key.StartsWith(prefix)).ToDictionary(x => x.Key.Substring(prefix.Length), x => x.Value);
            }
            catch (Exception)
            {
                return new Dictionary<string, string>(); // база недоступна - тексти з .resx
            }
        }

        // тексти з файлу перекладу (.resx) мовою lang
        public Dictionary<string, string> GetDefaults(SitePageInfo page, string lang)
        {
            var assembly = typeof(SiteTextService).Assembly.GetName().Name;
            var localizer = _localizers.Create($"{assembly}.{page.ResourceView}", assembly).WithCulture(new CultureInfo(lang));
            try
            {
                return localizer.GetAllStrings(false).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().Value);
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        // зберегти правки: порожньо або як у .resx - правку прибрати
        public async Task Save(SitePageInfo page, string lang, Dictionary<string, string> values)
        {
            await Ensure();
            var defaults = GetDefaults(page, lang);
            foreach (var (key, raw) in values.Select(x => (x.Key, x.Value)))
            {
                var value = (raw ?? "").Replace("\r\n", "\n").Trim();
                defaults.TryGetValue(key, out var def);
                if (value == "" || value == (def ?? "").Replace("\r\n", "\n").Trim())
                    await _connection.ExecuteAsync("DELETE FROM SiteTexts WHERE PageKey = @p AND Lang = @lang AND TextKey = @key",
                        new { p = page.Key, lang, key });
                else
                    await _connection.ExecuteAsync(@"
                        INSERT INTO SiteTexts (PageKey, Lang, TextKey, Value, UpdatedAt) VALUES (@p, @lang, @key, @value, UTC_TIMESTAMP())
                        ON DUPLICATE KEY UPDATE Value = @value, UpdatedAt = UTC_TIMESTAMP()",
                        new { p = page.Key, lang, key, value });
            }
            _cache = null;
        }
    }
}

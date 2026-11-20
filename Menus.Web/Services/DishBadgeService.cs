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
    // Мітки страв у меню (сторінка закладу й iframe): «Хіт», «Новинка», «Веган», «Гостре».
    // Автоматично, кодом: «Хіт» - найкраще оцінені гостями страви закладу, «Веган» / «Гостре» - однозначні слова
    // в назві чи описі. Власник може задати мітки страви вручну (кабінет, редагування страви) - тоді авто не діє.
    public class DishBadgeService
    {
        public static readonly Dictionary<string, (string Uk, string Ru, string En, string Icon)> All =
            new Dictionary<string, (string, string, string, string)>
            {
                ["hit"] = ("Хіт", "Хит", "Hit", "🔥"),
                ["new"] = ("Новинка", "Новинка", "New", "✨"),
                ["vegan"] = ("Веган", "Веган", "Vegan", "🌱"),
                ["spicy"] = ("Гостре", "Острое", "Spicy", "🌶"),
            };

        private static readonly Regex Vegan = new Regex(@"(?<![\p{L}])(веган|vegan)", RegexOptions.IgnoreCase);
        private static readonly Regex Spicy = new Regex(
            @"(?<![\p{L}])(гостр(ий|а|е|і|ого|ою|им|ими)|остр(ый|ая|ое|ые|ого|ым)|spicy|чилі|чили|chili|chilli|халапеньо|jalape[nñ]o)(?![\p{L}])",
            RegexOptions.IgnoreCase);

        public const int HitMinRatings = 3;
        public const double HitMinRating = 4.5;
        public const int HitsPerRestaurant = 3;

        public static string Name(string badge, string culture) =>
            All.TryGetValue(badge, out var b) ? (culture == "en" ? b.En : culture == "ru" ? b.Ru : b.Uk) : badge;

        public static string Icon(string badge) => All.TryGetValue(badge, out var b) ? b.Icon : "";

        // id найкраще оцінених страв закладу (кандидати в «Хіт»)
        public static HashSet<string> Hits(IEnumerable<(string Id, double Rating, int Count)> dishes) =>
            new HashSet<string>(dishes.Where(d => d.Count >= HitMinRatings && d.Rating >= HitMinRating)
                .OrderByDescending(d => d.Rating).ThenByDescending(d => d.Count).Take(HitsPerRestaurant).Select(d => d.Id));

        // мітки страви: ручні (manual != null) або автоматичні з тексту й рейтингу
        public static List<string> Resolve(string manual, bool hit, params string[] texts)
        {
            if (manual != null) return manual.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(All.ContainsKey).ToList();
            var text = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
            var result = new List<string>();
            if (hit) result.Add("hit");
            if (Vegan.IsMatch(text)) result.Add("vegan");
            if (Spicy.IsMatch(text)) result.Add("spicy");
            return result;
        }

        private readonly IDbConnection _connection;
        private static bool _ready;

        public DishBadgeService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS DishBadges (
                DishId VARCHAR(255) NOT NULL PRIMARY KEY,
                Badges VARCHAR(100) NOT NULL,
                UpdatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4");
            _ready = true;
        }

        // ручні мітки страв: id -> "hit,new" ("" - власник прибрав усі мітки)
        public async Task<Dictionary<string, string>> GetManual(IEnumerable<string> dishIds)
        {
            var ids = dishIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (!ids.Any()) return new Dictionary<string, string>();
            try
            {
                await Ensure();
                return (await _connection.QueryAsync<(string DishId, string Badges)>(
                        "SELECT DishId, Badges FROM DishBadges WHERE DishId IN @ids", new { ids }))
                    .ToDictionary(x => x.DishId, x => x.Badges);
            }
            catch (Exception)
            {
                return new Dictionary<string, string>();
            }
        }

        // badges = null - повернути автоматичні мітки
        public async Task Set(string dishId, IEnumerable<string> badges)
        {
            await Ensure();
            if (badges == null)
            {
                await _connection.ExecuteAsync("DELETE FROM DishBadges WHERE DishId = @dishId", new { dishId });
                return;
            }
            var value = string.Join(",", badges.Where(All.ContainsKey).Distinct());
            await _connection.ExecuteAsync(@"INSERT INTO DishBadges (DishId, Badges, UpdatedAt) VALUES (@dishId, @value, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE Badges = @value, UpdatedAt = UTC_TIMESTAMP()", new { dishId, value });
        }
    }
}

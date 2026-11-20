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
    // Щомісячний звіт власнику «Ваш заклад у Google»: покази, переходи, позиція й перегляди за попередній місяць.
    // Hangfire (Startup, «OwnerMonthlyReport») 4-го числа - коли seo_daily.py уже має дані за весь місяць.
    // Листи йдуть через чергу «Розсилки» (журнал OutreachLog, відписка - як у розсилці);
    // вимкнути звіт - галочка в кабінеті «Про заклад» (OwnerReportOptOut).
    public class OwnerReportService
    {
        public const int TemplateId = -1;       // позначка звіту в OutreachLog (не шаблон розсилки)
        public const int MinImpressions = 10;   // менше - не пишемо про нулі

        private readonly IDbConnection _connection;
        private readonly OutreachService _outreach;
        private readonly RestaurantStatsService _stats;

        public OwnerReportService(EfDbContext dbContext, OutreachService outreach, RestaurantStatsService stats)
        {
            _connection = dbContext.Database.GetDbConnection();
            _outreach = outreach;
            _stats = stats;
        }

        public async Task<bool> IsEnabled(string restaurantId)
        {
            await _outreach.GetTemplates(); // створює таблиці розсилки, зокрема OwnerReportOptOut
            return await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM OwnerReportOptOut WHERE RestaurantId = @restaurantId", new { restaurantId }) == 0;
        }

        public async Task SetEnabled(string restaurantId, bool enabled)
        {
            await _outreach.GetTemplates();
            await _connection.ExecuteAsync(enabled
                ? "DELETE FROM OwnerReportOptOut WHERE RestaurantId = @restaurantId"
                : "INSERT IGNORE INTO OwnerReportOptOut (RestaurantId, CreatedAt) VALUES (@restaurantId, NOW())", new { restaurantId });
        }

        private class OwnerRow
        {
            public string RestaurantId { get; set; }
            public string Name { get; set; }
            public string Slug { get; set; }
            public string Email { get; set; }
        }

        // Hangfire: звіт за попередній календарний місяць усім власникам активних закладів
        public async Task SendMonthly()
        {
            var today = DateTime.UtcNow.Date;
            var to = new DateTime(today.Year, today.Month, 1).AddDays(-1);
            var from = new DateTime(to.Year, to.Month, 1);
            var stats = await _stats.GetRange(from, to);
            await _outreach.GetTemplates();
            var owners = (await _connection.QueryAsync<OwnerRow>(@"
                SELECT CONCAT(r.Id, '') AS RestaurantId, r.Name, rs.Url AS Slug, u.Email
                FROM RestaurantRoles rr
                JOIN Restaurants r ON r.Id = rr.RestaurantId
                JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                JOIN AspNetUsers u ON u.Id = rr.UserId
                LEFT JOIN OwnerReportOptOut o ON o.RestaurantId = r.Id
                WHERE rr.Role = 1 AND r.Status = 0 AND o.RestaurantId IS NULL AND u.Email LIKE '%@%'")).ToList();
            var month = Months[from.Month - 1] + " " + from.Year;
            var delay = TimeSpan.Zero;
            foreach (var o in owners.GroupBy(x => (x.RestaurantId, x.Email.Trim().ToLowerInvariant())).Select(g => g.First()))
            {
                if (string.IsNullOrEmpty(o.Slug) || !stats.TryGetValue(o.Slug.ToLowerInvariant(), out var s) || s.Impressions < MinImpressions) continue;
                if (await _outreach.SentSince(o.RestaurantId, TemplateId, new DateTime(today.Year, today.Month, 1))) continue; // уже надіслано цього місяця
                var token = Guid.NewGuid().ToString("N");
                s.Queries = await _stats.TopQueries(o.Slug, from, to, 5);
                var (subject, body) = Text(o.Name, month, s, token);
                await _outreach.QueueSystemEmail(o.RestaurantId, TemplateId, o.Email.Trim(), subject, body, token, delay);
                delay += TimeSpan.FromSeconds(OutreachService.IntervalSeconds);
            }
        }

        private static readonly string[] Months =
            { "січень", "лютий", "березень", "квітень", "травень", "червень", "липень", "серпень", "вересень", "жовтень", "листопад", "грудень" };

        public static (string Subject, string Body) Text(string name, string month, RestaurantStats s, string token)
        {
            const string site = "https://tipmymenu.com";
            var lines = new List<string>
            {
                "Добрий день!",
                "",
                $"«{name}» у Google за {month}:",
                $"- {s.Impressions:N0} показів у пошуку",
                $"- {s.Clicks:N0} переходів на сторінку закладу",
            };
            if (s.Position > 0) lines.Add($"- середня позиція {s.Position:0.#}");
            if (s.Views > 0) lines.Add($"- {s.Views:N0} переглядів сторінки");
            if (s.Queries.Any())
            {
                lines.Add("");
                lines.Add("Вас знаходили за запитами:");
                lines.AddRange(s.Queries.Select(q => $"- «{q.Query}» - {q.Impressions:N0} показів"));
            }
            lines.AddRange(new[]
            {
                "",
                "Більше фото страв, актуальні ціни й години роботи - вище позиція і більше гостей.",
                "Оновити меню, роздрукувати QR-код на стіл чи вставити меню на сайт:",
                $"{site}/uk/restaurants/about",
                "",
                "Команда TipMyMenu",
                "",
                $"Не хочете отримувати листи від TipMyMenu? Відписатися: {site}/uk/outreach/unsubscribe/{token}"
            });
            return ($"«{name}» у Google: {s.Impressions:N0} показів за {month}", string.Join("\n", lines));
        }
    }
}

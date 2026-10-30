using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Hangfire;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class OutreachTemplate
    {
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    // Ресторан у списку розсилки: контакти з RestaurantContacts (content-bot/collect_contacts.py) і поля Email
    public class OutreachRecipient
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Slug { get; set; }
        public int Dishes { get; set; }
        public string Email { get; set; }
        public List<string> Emails { get; set; } = new List<string>();
        public List<string> Telegram { get; set; } = new List<string>();
        public List<string> Instagram { get; set; } = new List<string>();
        public List<OutreachLogRow> Log { get; set; } = new List<OutreachLogRow>();
    }

    public class OutreachLogRow
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string Channel { get; set; } // email | telegram | instagram
        public string Recipient { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string Status { get; set; } // queued | sent | failed | skipped | manual
        public string Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    // Лист власникам ресторанів: шаблон з підстановками, відправка email по одному з інтервалом (Hangfire),
    // журнал (кожному адресату - один раз), відписка. Telegram/Instagram бот першим не напише -
    // адмін копіює текст і позначає «надіслано вручну»
    public class OutreachService
    {
        public const int IntervalSeconds = 60; // ~60 листів на годину: масова відправка з одного домену йде в спам

        private const string CreateTablesSql = @"
            CREATE TABLE IF NOT EXISTS OutreachTemplate (
                Id INT NOT NULL PRIMARY KEY,
                Subject TEXT NOT NULL,
                Body MEDIUMTEXT NOT NULL,
                UpdatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4;
            CREATE TABLE IF NOT EXISTS OutreachLog (
                Id CHAR(32) NOT NULL PRIMARY KEY,
                RestaurantId VARCHAR(255) NOT NULL,
                Channel VARCHAR(16) NOT NULL,
                Recipient VARCHAR(255) NOT NULL,
                Subject TEXT NULL,
                Body MEDIUMTEXT NULL,
                Status VARCHAR(16) NOT NULL,
                Error TEXT NULL,
                CreatedAt DATETIME NOT NULL,
                SentAt DATETIME NULL,
                KEY IX_OutreachLog_Restaurant (RestaurantId),
                KEY IX_OutreachLog_Recipient (Recipient)
            ) DEFAULT CHARSET=utf8mb4;
            CREATE TABLE IF NOT EXISTS OutreachOptOut (
                Recipient VARCHAR(255) NOT NULL PRIMARY KEY,
                CreatedAt DATETIME NOT NULL
            ) DEFAULT CHARSET=utf8mb4;
            CREATE TABLE IF NOT EXISTS RestaurantContacts (
                Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                RestaurantId VARCHAR(255) NOT NULL,
                Kind VARCHAR(16) NOT NULL,
                Value VARCHAR(255) NOT NULL,
                SourceUrl TEXT NULL,
                CreatedAt DATETIME NOT NULL,
                UNIQUE KEY UX_RestaurantContacts (RestaurantId, Kind, Value)
            ) DEFAULT CHARSET=utf8mb4;";

        // Чернетка; адмін редагує її на сторінці «Розсилка»
        public static readonly OutreachTemplate DefaultTemplate = new OutreachTemplate
        {
            Subject = "«{Назва}» на TipMyMenu: перевірте, будь ласка, ваше меню",
            Body = @"Добрий день!

Ми - TipMyMenu (tipmymenu.com), сервіс, де гості шукають меню й ціни закладів. Сторінка «{Назва}» ({Місто}) вже є на сайті, у меню - {Страв} страв:
{Сторінка}

Будь ласка, перегляньте її: чи все правильно - страви, ціни, фото, години роботи. Якщо щось змінилося, напишіть у відповідь на цей лист - виправимо.

Що ще можна безкоштовно:
- стати власником сторінки й самостійно оновлювати меню, фото та контакти: {ДляВласників}
- вбудувати меню з TipMyMenu на свій сайт: {Iframe}

Меню на TipMyMenu оновлюється з вашого сайту або QR-меню (ChoiceQR, Expirenza), тож ціни лишаються актуальними.

З повагою,
команда TipMyMenu
https://tipmymenu.com

{Відписатися}"
        };

        public static readonly string[] Placeholders = { "{Назва}", "{Місто}", "{Сторінка}", "{Страв}", "{ДляВласників}", "{Iframe}", "{Відписатися}" };

        private readonly IDbConnection _connection;
        private readonly EmailService _email;

        public OutreachService(EfDbContext dbContext, EmailService email)
        {
            _connection = dbContext.Database.GetDbConnection();
            _email = email;
        }

        public async Task<OutreachTemplate> GetTemplate()
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            return await _connection.QueryFirstOrDefaultAsync<OutreachTemplate>("SELECT Subject, Body FROM OutreachTemplate WHERE Id = 1")
                   ?? DefaultTemplate;
        }

        public async Task SaveTemplate(string subject, string body)
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            await _connection.ExecuteAsync(@"INSERT INTO OutreachTemplate (Id, Subject, Body, UpdatedAt) VALUES (1, @subject, @body, NOW())
                ON DUPLICATE KEY UPDATE Subject = VALUES(Subject), Body = VALUES(Body), UpdatedAt = NOW()",
                new { subject = subject ?? "", body = body ?? "" });
        }

        public async Task<List<OutreachRecipient>> GetRecipients()
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            var restaurants = (await _connection.QueryAsync<OutreachRecipient>(@"
                SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, rs.Url AS Slug, r.Email,
                       (SELECT COUNT(*) FROM Dishes d WHERE d.RestaurantId = r.Id AND d.Status = 0) AS Dishes
                FROM Restaurants r LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                WHERE r.Status = 0")).ToDictionary(x => x.Id);
            foreach (var c in await _connection.QueryAsync<ContactRow>(
                         "SELECT CONCAT(RestaurantId, '') AS RestaurantId, Kind, Value FROM RestaurantContacts ORDER BY Id"))
            {
                if (!restaurants.TryGetValue(c.RestaurantId, out var r)) continue;
                var list = c.Kind == "email" ? r.Emails : c.Kind == "telegram" ? r.Telegram : c.Kind == "instagram" ? r.Instagram : null;
                if (list != null && !list.Contains(c.Value)) list.Add(c.Value);
            }
            foreach (var r in restaurants.Values)
            {
                var own = (r.Email ?? "").Trim().ToLowerInvariant();
                if (own.Contains("@") && !r.Emails.Contains(own)) r.Emails.Insert(0, own);
            }
            foreach (var row in await _connection.QueryAsync<OutreachLogRow>(
                         "SELECT Id, CONCAT(RestaurantId, '') AS RestaurantId, Channel, Recipient, Status, Error, CreatedAt, SentAt FROM OutreachLog ORDER BY CreatedAt"))
            {
                if (restaurants.TryGetValue(row.RestaurantId, out var r)) r.Log.Add(row);
            }
            return restaurants.Values
                .Where(x => x.Emails.Any() || x.Telegram.Any() || x.Instagram.Any())
                .OrderBy(x => x.Name).ToList();
        }

        private class ContactRow
        {
            public string RestaurantId { get; set; }
            public string Kind { get; set; }
            public string Value { get; set; }
        }

        public async Task<HashSet<string>> GetOptOuts()
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            return new HashSet<string>(await _connection.QueryAsync<string>("SELECT Recipient FROM OutreachOptOut"), StringComparer.OrdinalIgnoreCase);
        }

        // Підстановки в шаблон; unsubscribeUrl порожній - рядок «відписатися» прибирається (Telegram/Instagram)
        public static (string Subject, string Body) Render(OutreachTemplate template, OutreachRecipient r, string cityName,
            string citySlug, string baseUrl, string unsubscribeUrl)
        {
            var page = $"{baseUrl}/uk/{citySlug}/restaurant/{r.Slug}";
            var values = new Dictionary<string, string>
            {
                ["{Назва}"] = r.Name,
                ["{Місто}"] = cityName,
                ["{Сторінка}"] = page,
                ["{Страв}"] = r.Dishes.ToString(),
                ["{ДляВласників}"] = $"{baseUrl}/uk/articles/forowners",
                ["{Iframe}"] = $"{baseUrl}/uk/{citySlug}/iframe/{r.Slug}",
                ["{Відписатися}"] = string.IsNullOrEmpty(unsubscribeUrl) ? "" : $"Не хочете отримувати листи від TipMyMenu? Відписатися: {unsubscribeUrl}"
            };
            string Fill(string text) => values.Aggregate(text ?? "", (acc, kv) => acc.Replace(kv.Key, kv.Value));
            return (Fill(template.Subject).Trim(), Fill(template.Body).Trim());
        }

        // Текст -> HTML листа: екранування, посилання клікабельні, переноси рядків
        public static string ToHtml(string text)
        {
            var encoded = WebUtility.HtmlEncode(text ?? "");
            encoded = Regex.Replace(encoded, @"https?://[^\s<]+", m => $"<a href=\"{m.Value}\">{m.Value}</a>");
            return "<div style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.5\">" + encoded.Replace("\n", "<br>") + "</div>";
        }

        // Ставить листи в чергу: кожному адресату один раз (крім невдалих), відписані пропускаються
        public async Task<int> QueueEmails(IEnumerable<(OutreachRecipient Restaurant, string Email, string Subject, string Body, string Token)> letters)
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            var optOuts = await GetOptOuts();
            var done = new HashSet<string>(await _connection.QueryAsync<string>(
                "SELECT Recipient FROM OutreachLog WHERE Channel = 'email' AND Status IN ('queued', 'sent')"), StringComparer.OrdinalIgnoreCase);
            var delay = 0;
            foreach (var l in letters)
            {
                if (optOuts.Contains(l.Email) || !done.Add(l.Email)) continue;
                await _connection.ExecuteAsync(@"INSERT INTO OutreachLog (Id, RestaurantId, Channel, Recipient, Subject, Body, Status, CreatedAt)
                    VALUES (@Token, @RestaurantId, 'email', @Email, @Subject, @Body, 'queued', NOW())",
                    new { l.Token, RestaurantId = l.Restaurant.Id, l.Email, l.Subject, l.Body });
                var id = l.Token;
                BackgroundJob.Schedule<OutreachService>(x => x.SendQueued(id), TimeSpan.FromSeconds(delay));
                delay += IntervalSeconds;
            }
            return delay / IntervalSeconds;
        }

        // Hangfire: один лист із журналу
        public async Task SendQueued(string id)
        {
            var row = await _connection.QueryFirstOrDefaultAsync<OutreachLogRow>("SELECT * FROM OutreachLog WHERE Id = @id", new { id });
            if (row == null || row.Status != "queued") return;
            if ((await GetOptOuts()).Contains(row.Recipient))
            {
                await _connection.ExecuteAsync("UPDATE OutreachLog SET Status = 'skipped', Error = 'відписався' WHERE Id = @id", new { id });
                return;
            }
            var error = _email.Send(row.Recipient, row.Subject, ToHtml(row.Body), UnsubscribeLink(row.Body));
            await _connection.ExecuteAsync("UPDATE OutreachLog SET Status = @status, Error = @error, SentAt = NOW() WHERE Id = @id",
                new { id, status = error == null ? "sent" : "failed", error });
        }

        private static string UnsubscribeLink(string body) => Regex.Match(body ?? "", @"https?://\S+/outreach/unsubscribe/\S+", RegexOptions.IgnoreCase).Value;

        // Telegram/Instagram: адмін надіслав текст сам
        public async Task MarkManual(string restaurantId, string channel, string recipient, string body)
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            await _connection.ExecuteAsync(@"INSERT INTO OutreachLog (Id, RestaurantId, Channel, Recipient, Body, Status, CreatedAt, SentAt)
                VALUES (@id, @restaurantId, @channel, @recipient, @body, 'manual', NOW(), NOW())",
                new { id = Guid.NewGuid().ToString("N"), restaurantId, channel, recipient, body });
        }

        public async Task<string> Unsubscribe(string token)
        {
            await _connection.ExecuteAsync(CreateTablesSql);
            var recipient = await _connection.QueryFirstOrDefaultAsync<string>(
                "SELECT Recipient FROM OutreachLog WHERE Id = @token AND Channel = 'email'", new { token });
            if (recipient == null) return null;
            await _connection.ExecuteAsync("INSERT IGNORE INTO OutreachOptOut (Recipient, CreatedAt) VALUES (@recipient, NOW())", new { recipient });
            return recipient;
        }
    }
}

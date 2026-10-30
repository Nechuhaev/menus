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
        public int Id { get; set; }
        public string Name { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }

    public class OutreachContact
    {
        public int Id { get; set; } // 0 - поле Email картки ресторану (тут не редагується)
        public string RestaurantId { get; set; }
        public string Kind { get; set; } // email | telegram | instagram | phone
        public string Value { get; set; }
        public string SourceUrl { get; set; }
    }

    // Ресторан у розсилці: контакти з RestaurantContacts (collect_contacts.py і адмін) та поле Email картки
    public class OutreachRecipient
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Slug { get; set; }
        public int Dishes { get; set; }
        public string Email { get; set; }
        public List<OutreachContact> Contacts { get; set; } = new List<OutreachContact>();
        public List<OutreachLogRow> Log { get; set; } = new List<OutreachLogRow>();
        public IEnumerable<string> Values(string kind) => Contacts.Where(x => x.Kind == kind).Select(x => x.Value);
        public List<string> Emails => Values("email").ToList();
        public DateTime? LastSent => Log.Where(x => x.Status == "sent" || x.Status == "manual").Select(x => x.SentAt ?? x.CreatedAt)
            .DefaultIfEmpty().Max() is DateTime d && d != default ? d : (DateTime?)null;
    }

    public class OutreachLogRow
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public int? TemplateId { get; set; }
        public string Channel { get; set; } // email | telegram | instagram
        public string Recipient { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string Status { get; set; } // queued | sent | failed | skipped | manual
        public string Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    // Розсилка власникам ресторанів: шаблони з підстановками, контакти (редагує адмін), відправка email -
    // одразу з картки ресторану або чергою зі списку (Hangfire, по одному з інтервалом), журнал, відписка.
    // Instagram - адмін копіює текст і позначає «надіслано вручну»
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
            ) DEFAULT CHARSET=utf8mb4;
            ALTER TABLE OutreachTemplate ADD COLUMN IF NOT EXISTS Name VARCHAR(255) NULL;
            ALTER TABLE OutreachLog ADD COLUMN IF NOT EXISTS TemplateId INT NULL;
            ALTER TABLE RestaurantContacts ADD COLUMN IF NOT EXISTS Hidden TINYINT(1) NOT NULL DEFAULT 0;";

        // Перший шаблон, якщо в базі ще немає жодного
        public static readonly OutreachTemplate DefaultTemplate = new OutreachTemplate
        {
            Name = "Знайомство: перевірте меню",
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

        public static readonly string[] Placeholders = { "{Назва}", "{Місто}", "{Сторінка}", "{Страв}", "{ДляВласників}", "{Iframe}", "{Telegram}", "{Відписатися}" };

        public static readonly string[] Kinds = { "email", "telegram", "instagram", "phone" };
        public const int PageSize = 30;

        private readonly IDbConnection _connection;
        private readonly EmailService _email;
        private bool _tablesReady;

        public OutreachService(EfDbContext dbContext, EmailService email)
        {
            _connection = dbContext.Database.GetDbConnection();
            _email = email;
        }

        private async Task Ensure()
        {
            if (_tablesReady) return;
            await _connection.ExecuteAsync(CreateTablesSql);
            _tablesReady = true;
        }

        // --- шаблони ---

        public async Task<List<OutreachTemplate>> GetTemplates()
        {
            await Ensure();
            var list = (await _connection.QueryAsync<OutreachTemplate>(
                "SELECT Id, IFNULL(Name, CONCAT('Шаблон ', Id)) AS Name, Subject, Body FROM OutreachTemplate ORDER BY Id")).ToList();
            if (list.Any()) return list;
            await SaveTemplate(null, DefaultTemplate.Name, DefaultTemplate.Subject, DefaultTemplate.Body);
            return await GetTemplates();
        }

        public async Task<OutreachTemplate> GetTemplate(int? id)
        {
            var all = await GetTemplates();
            return all.FirstOrDefault(x => x.Id == id) ?? all.First();
        }

        public async Task<int> SaveTemplate(int? id, string name, string subject, string body)
        {
            await Ensure();
            if (id == null) id = await _connection.ExecuteScalarAsync<int>("SELECT IFNULL(MAX(Id), 0) + 1 FROM OutreachTemplate");
            await _connection.ExecuteAsync(@"INSERT INTO OutreachTemplate (Id, Name, Subject, Body, UpdatedAt) VALUES (@id, @name, @subject, @body, NOW())
                ON DUPLICATE KEY UPDATE Name = VALUES(Name), Subject = VALUES(Subject), Body = VALUES(Body), UpdatedAt = NOW()",
                new { id, name = string.IsNullOrWhiteSpace(name) ? $"Шаблон {id}" : name.Trim(), subject = subject ?? "", body = body ?? "" });
            return id.Value;
        }

        public async Task DeleteTemplate(int id)
        {
            await Ensure();
            await _connection.ExecuteAsync("DELETE FROM OutreachTemplate WHERE Id = @id", new { id });
        }

        // --- ресторани й контакти ---

        // Усі активні ресторани з контактами та історією (≈ тисяча рядків - фільтр і сторінки рахуються в пам'яті)
        public async Task<List<OutreachRecipient>> GetRestaurants()
        {
            await Ensure();
            var restaurants = (await _connection.QueryAsync<OutreachRecipient>(@"
                SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, rs.Url AS Slug, r.Email,
                       (SELECT COUNT(*) FROM Dishes d WHERE d.RestaurantId = r.Id AND d.Status = 0) AS Dishes
                FROM Restaurants r LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                WHERE r.Status = 0")).ToDictionary(x => x.Id);
            await Fill(restaurants, null);
            return restaurants.Values.OrderBy(x => x.Name).ToList();
        }

        public async Task<OutreachRecipient> GetRestaurant(string id)
        {
            await Ensure();
            var r = await _connection.QueryFirstOrDefaultAsync<OutreachRecipient>(@"
                SELECT CONCAT(r.Id, '') AS Id, r.Name, r.City, rs.Url AS Slug, r.Email,
                       (SELECT COUNT(*) FROM Dishes d WHERE d.RestaurantId = r.Id AND d.Status = 0) AS Dishes
                FROM Restaurants r LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                WHERE r.Id = @id", new { id });
            if (r == null) return null;
            await Fill(new Dictionary<string, OutreachRecipient> { [r.Id] = r }, id);
            return r;
        }

        private async Task Fill(Dictionary<string, OutreachRecipient> restaurants, string onlyId)
        {
            var where = onlyId == null ? "" : " AND RestaurantId = @onlyId";
            foreach (var c in await _connection.QueryAsync<OutreachContact>(
                         "SELECT Id, CONCAT(RestaurantId, '') AS RestaurantId, Kind, Value, SourceUrl FROM RestaurantContacts WHERE Hidden = 0" + where + " ORDER BY Id",
                         new { onlyId }))
            {
                if (restaurants.TryGetValue(c.RestaurantId, out var r) && !r.Contacts.Any(x => x.Kind == c.Kind && x.Value == c.Value)) r.Contacts.Add(c);
            }
            foreach (var r in restaurants.Values)
            {
                var own = (r.Email ?? "").Trim().ToLowerInvariant();
                if (own.Contains("@") && !r.Emails.Contains(own))
                    r.Contacts.Insert(0, new OutreachContact { Id = 0, RestaurantId = r.Id, Kind = "email", Value = own, SourceUrl = "картка ресторану" });
            }
            foreach (var row in await _connection.QueryAsync<OutreachLogRow>(
                         "SELECT Id, CONCAT(RestaurantId, '') AS RestaurantId, TemplateId, Channel, Recipient, " +
                         (onlyId == null ? "NULL AS Subject, NULL AS Body" : "Subject, Body") +
                         ", Status, Error, CreatedAt, SentAt FROM OutreachLog WHERE 1 = 1" + where + " ORDER BY CreatedAt", new { onlyId }))
            {
                if (restaurants.TryGetValue(row.RestaurantId, out var r)) r.Log.Add(row);
            }
        }

        public async Task<string> AddContact(string restaurantId, string kind, string value)
        {
            await Ensure();
            value = NormalizeContact(kind, value);
            if (value == null) return "Невірний контакт";
            // прихований раніше (видалений адміном) - повертаємо
            var changed = await _connection.ExecuteAsync(
                "UPDATE RestaurantContacts SET Hidden = 0, SourceUrl = 'admin' WHERE RestaurantId = @restaurantId AND Kind = @kind AND Value = @value",
                new { restaurantId, kind, value });
            if (changed == 0)
                await _connection.ExecuteAsync(@"INSERT INTO RestaurantContacts (RestaurantId, Kind, Value, SourceUrl, CreatedAt)
                    VALUES (@restaurantId, @kind, @value, 'admin', NOW())", new { restaurantId, kind, value });
            return null;
        }

        public async Task<string> UpdateContact(string restaurantId, int contactId, string value)
        {
            await Ensure();
            var kind = await _connection.QueryFirstOrDefaultAsync<string>(
                "SELECT Kind FROM RestaurantContacts WHERE Id = @contactId AND RestaurantId = @restaurantId", new { contactId, restaurantId });
            if (kind == null) return "Контакт не знайдено";
            // стару адресу ховаємо (щоб collect_contacts.py не повернув), нову додаємо
            await HideContact(restaurantId, contactId);
            return await AddContact(restaurantId, kind, value);
        }

        // Видалення - прихування: collect_contacts.py пише через INSERT IGNORE і прихований контакт не повертає
        public async Task HideContact(string restaurantId, int contactId)
        {
            await Ensure();
            await _connection.ExecuteAsync("UPDATE RestaurantContacts SET Hidden = 1 WHERE Id = @contactId AND RestaurantId = @restaurantId",
                new { contactId, restaurantId });
        }

        public static string NormalizeContact(string kind, string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Length > 255) return null;
            switch (kind)
            {
                case "email":
                    value = value.ToLowerInvariant();
                    return Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[a-z]{2,}$") ? value : null;
                case "telegram":
                    var tg = Regex.Match(value, @"^(?:https?://)?(?:t\.me/|telegram\.me/|@)?([A-Za-z0-9_]{5,32})/?$");
                    return tg.Success ? "t.me/" + tg.Groups[1].Value : null;
                case "instagram":
                    var ig = Regex.Match(value, @"^(?:https?://)?(?:www\.)?(?:instagram\.com/|@)?([A-Za-z0-9_.]{2,30})/?$");
                    return ig.Success ? "instagram.com/" + ig.Groups[1].Value.ToLowerInvariant() : null;
                case "phone":
                    var digits = Regex.Replace(value, @"[^\d+]", "");
                    return digits.Length >= 9 ? digits : null;
                default:
                    return null;
            }
        }

        public async Task<HashSet<string>> GetOptOuts()
        {
            await Ensure();
            return new HashSet<string>(await _connection.QueryAsync<string>("SELECT Recipient FROM OutreachOptOut"), StringComparer.OrdinalIgnoreCase);
        }

        // --- текст ---

        // Підстановки в шаблон; unsubscribeUrl порожній - рядок «відписатися» прибирається (Telegram/Instagram)
        public static (string Subject, string Body) Render(OutreachTemplate template, OutreachRecipient r, string cityName,
            string citySlug, string baseUrl, string unsubscribeUrl, string telegramLink = "")
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
                ["{Telegram}"] = telegramLink ?? "",
                ["{Відписатися}"] = string.IsNullOrEmpty(unsubscribeUrl) ? "" : $"Не хочете отримувати листи від TipMyMenu? Відписатися: {unsubscribeUrl}"
            };
            string FillText(string text) => values.Aggregate(text ?? "", (acc, kv) => acc.Replace(kv.Key, kv.Value));
            return (FillText(template.Subject).Trim(), FillText(template.Body).Trim());
        }

        // Текст -> HTML листа: екранування, посилання клікабельні, переноси рядків
        public static string ToHtml(string text)
        {
            var encoded = WebUtility.HtmlEncode(text ?? "");
            encoded = Regex.Replace(encoded, @"https?://[^\s<]+", m => $"<a href=\"{m.Value}\">{m.Value}</a>");
            return "<div style=\"font-family:Arial,sans-serif;font-size:15px;line-height:1.5\">" + encoded.Replace("\n", "<br>") + "</div>";
        }

        // --- відправка ---

        // Масово зі списку: у чергу по одному листу раз на IntervalSeconds; кожній адресі один раз, відписані пропускаються
        public async Task<int> QueueEmails(IEnumerable<(OutreachRecipient Restaurant, string Email, string Subject, string Body, string Token)> letters, int templateId)
        {
            await Ensure();
            var optOuts = await GetOptOuts();
            var done = new HashSet<string>(await _connection.QueryAsync<string>(
                "SELECT Recipient FROM OutreachLog WHERE Channel = 'email' AND Status IN ('queued', 'sent')"), StringComparer.OrdinalIgnoreCase);
            var delay = 0;
            foreach (var l in letters)
            {
                if (optOuts.Contains(l.Email) || !done.Add(l.Email)) continue;
                await InsertLog(l.Token, l.Restaurant.Id, templateId, "email", l.Email, l.Subject, l.Body, "queued");
                var id = l.Token;
                BackgroundJob.Schedule<OutreachService>(x => x.SendQueued(id), TimeSpan.FromSeconds(delay));
                delay += IntervalSeconds;
            }
            return delay / IntervalSeconds;
        }

        // З картки ресторану: одразу; повторно тій самій адресі можна (адмін вирішує сам), відписаним - ні
        public async Task<string> SendEmailNow(string restaurantId, int templateId, string email, string subject, string body, string token)
        {
            await Ensure();
            if ((await GetOptOuts()).Contains(email)) return "адреса відписалась від розсилки";
            await InsertLog(token, restaurantId, templateId, "email", email, subject, body, "queued");
            await SendQueued(token);
            return await _connection.QueryFirstOrDefaultAsync<string>("SELECT Error FROM OutreachLog WHERE Id = @token", new { token });
        }

        private Task InsertLog(string id, string restaurantId, int? templateId, string channel, string recipient, string subject, string body, string status) =>
            _connection.ExecuteAsync(@"INSERT INTO OutreachLog (Id, RestaurantId, TemplateId, Channel, Recipient, Subject, Body, Status, CreatedAt, SentAt)
                VALUES (@id, @restaurantId, @templateId, @channel, @recipient, @subject, @body, @status, NOW(), IF(@status = 'queued', NULL, NOW()))",
                new { id, restaurantId, templateId, channel, recipient, subject, body, status });

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

        // Instagram (і Telegram без бота): адмін надіслав текст сам
        public async Task MarkManual(string restaurantId, int templateId, string channel, string recipient, string body)
        {
            await Ensure();
            await InsertLog(Guid.NewGuid().ToString("N"), restaurantId, templateId, channel, recipient, null, body, "manual");
        }

        // Telegram-бот власників: надіслане з адмінки (sent/failed) і відповіді власника (received)
        public async Task LogTelegram(string restaurantId, string recipient, string body, string status, int? templateId = null, string error = null)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"INSERT INTO OutreachLog (Id, RestaurantId, TemplateId, Channel, Recipient, Body, Status, Error, CreatedAt, SentAt)
                VALUES (@id, @restaurantId, @templateId, 'telegram', @recipient, @body, @status, @error, NOW(), NOW())",
                new { id = Guid.NewGuid().ToString("N"), restaurantId, templateId, recipient, body, status, error });
        }

        public async Task<string> Unsubscribe(string token)
        {
            await Ensure();
            var recipient = await _connection.QueryFirstOrDefaultAsync<string>(
                "SELECT Recipient FROM OutreachLog WHERE Id = @token AND Channel = 'email'", new { token });
            if (recipient == null) return null;
            await _connection.ExecuteAsync("INSERT IGNORE INTO OutreachOptOut (Recipient, CreatedAt) VALUES (@recipient, NOW())", new { recipient });
            return recipient;
        }
    }
}

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
    public class NewVenueCandidate
    {
        public int Id { get; set; }
        public string Keyword { get; set; }
        public int Volume { get; set; }
        public string Name { get; set; }
        public string CityId { get; set; }
        public string Address { get; set; }
        public string Lat { get; set; }
        public string Lon { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string OpenHours { get; set; }
        public string Cuisine { get; set; }
        public string OsmKey { get; set; }
        public string SourceUrl { get; set; }
        public string Status { get; set; }
        public string RestaurantId { get; set; }
        public string RestaurantSlug { get; set; }
        public string Note { get; set; }
        public DateTime CreatedAt { get; set; }
        public string DuplicateOf { get; set; }      // наявний заклад, на який схожий (new_venues.py --duplicates)
        public string DuplicateSlug { get; set; }
        public string DuplicateCity { get; set; }
        public string PrevStatus { get; set; }
    }

    // Кандидати нових закладів (content-bot/new_venues.py: запити конкурентів + OpenStreetMap).
    // Адмін у «Ресторанах» -> «Нові заклади»: Додати (approved - бот створить заклад уночі), Не додавати, Дубль.
    public class NewVenueService
    {
        public static readonly Dictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["new"] = "Нові", ["approved"] = "Схвалені (бот створить уночі)", ["added"] = "Додані",
            ["rejected"] = "Не додавати", ["suspect"] = "Можливі дублі", ["duplicate"] = "Дублі",
        };

        private readonly IDbConnection _connection;

        public NewVenueService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            await EnsureTable();
            await _connection.ExecuteAsync(@"
                ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS DuplicateOf VARCHAR(255) NULL;
                ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS PrevStatus VARCHAR(16) NULL;
                ALTER TABLE NewVenueCandidates ADD COLUMN IF NOT EXISTS NotDuplicate TINYINT(1) NOT NULL DEFAULT 0;");
        }

        private Task EnsureTable() => _connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS NewVenueCandidates (
                Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                OsmKey VARCHAR(64) NOT NULL,
                Keyword VARCHAR(255) NOT NULL,
                Volume INT NOT NULL DEFAULT 0,
                Name VARCHAR(255) NOT NULL,
                CityId VARCHAR(255) NOT NULL,
                Address VARCHAR(500) NULL,
                Lat VARCHAR(32) NULL,
                Lon VARCHAR(32) NULL,
                Website VARCHAR(500) NULL,
                Phone VARCHAR(100) NULL,
                OpenHours VARCHAR(500) NULL,
                Cuisine VARCHAR(255) NULL,
                SourceUrl VARCHAR(500) NULL,
                Status VARCHAR(16) NOT NULL DEFAULT 'new',
                RestaurantId VARCHAR(255) NULL,
                Note VARCHAR(255) NULL,
                CreatedAt DATETIME NOT NULL,
                UpdatedAt DATETIME NOT NULL,
                UNIQUE KEY UX_OsmKey (OsmKey)
            ) DEFAULT CHARSET=utf8mb4");

        public async Task<Dictionary<string, int>> Counts()
        {
            try
            {
                await Ensure();
                return (await _connection.QueryAsync<(string Status, long Count)>(
                        "SELECT Status, COUNT(*) FROM NewVenueCandidates GROUP BY Status"))
                    .ToDictionary(x => x.Status, x => (int)x.Count);
            }
            catch (Exception)
            {
                return new Dictionary<string, int>();
            }
        }

        public async Task<List<NewVenueCandidate>> List(string status, string cityId)
        {
            await Ensure();
            return (await _connection.QueryAsync<NewVenueCandidate>(@"
                SELECT c.*, rs.Url AS RestaurantSlug, ds.Url AS DuplicateSlug, CONCAT(d.City, '') AS DuplicateCity
                FROM NewVenueCandidates c LEFT JOIN RestaurantSeo rs ON rs.ItemId = c.RestaurantId
                LEFT JOIN Restaurants d ON d.Id = c.DuplicateOf LEFT JOIN RestaurantSeo ds ON ds.ItemId = c.DuplicateOf
                WHERE c.Status = @status AND (@cityId IS NULL OR c.CityId = @cityId)
                ORDER BY c.Volume DESC, c.Name", new { status, cityId })).ToList();
        }

        public async Task SetStatus(int id, string status, string note)
        {
            if (!Statuses.ContainsKey(status) || status == "added" || status == "suspect") return; // їх ставить лише бот
            await Ensure();
            await _connection.ExecuteAsync(@"
                UPDATE NewVenueCandidates SET Status = @status, Note = @note, UpdatedAt = UTC_TIMESTAMP()
                WHERE Id = @id AND Status <> 'added'", new { id, status, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
        }

        // «Можливий дубль» -> дубль: кандидат у «Дублі»; якщо заклад уже створено - він закривається (зникає з сайту й каталогу)
        public async Task ConfirmDuplicate(int id)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"
                UPDATE Restaurants r JOIN NewVenueCandidates c ON c.RestaurantId = r.Id SET r.Status = 3
                WHERE c.Id = @id AND c.Status = 'suspect';
                UPDATE NewVenueCandidates SET Status = 'duplicate', UpdatedAt = UTC_TIMESTAMP() WHERE Id = @id AND Status = 'suspect';", new { id });
        }

        // «Можливий дубль» -> не дубль: попередній статус, бот більше не позначає
        public async Task NotDuplicate(int id)
        {
            await Ensure();
            await _connection.ExecuteAsync(@"
                UPDATE NewVenueCandidates SET Status = IFNULL(PrevStatus, 'new'), NotDuplicate = 1, DuplicateOf = NULL,
                       Note = NULL, UpdatedAt = UTC_TIMESTAMP()
                WHERE Id = @id AND Status = 'suspect'", new { id });
        }
    }
}

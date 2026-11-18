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
    }

    // Кандидати нових закладів (content-bot/new_venues.py: запити конкурентів + OpenStreetMap).
    // Адмін у «Ресторанах» -> «Нові заклади»: Додати (approved - бот створить заклад уночі), Не додавати, Дубль.
    public class NewVenueService
    {
        public static readonly Dictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["new"] = "Нові", ["approved"] = "Схвалені (бот створить уночі)", ["added"] = "Додані",
            ["rejected"] = "Не додавати", ["duplicate"] = "Дублі",
        };

        private readonly IDbConnection _connection;

        public NewVenueService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private Task Ensure() => _connection.ExecuteAsync(@"
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
                SELECT c.*, rs.Url AS RestaurantSlug
                FROM NewVenueCandidates c LEFT JOIN RestaurantSeo rs ON rs.ItemId = c.RestaurantId
                WHERE c.Status = @status AND (@cityId IS NULL OR c.CityId = @cityId)
                ORDER BY c.Volume DESC, c.Name", new { status, cityId })).ToList();
        }

        public async Task SetStatus(int id, string status, string note)
        {
            if (!Statuses.ContainsKey(status) || status == "added") return; // «додані» ставить лише бот
            await Ensure();
            await _connection.ExecuteAsync(@"
                UPDATE NewVenueCandidates SET Status = @status, Note = @note, UpdatedAt = UTC_TIMESTAMP()
                WHERE Id = @id AND Status <> 'added'", new { id, status, note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() });
        }
    }
}

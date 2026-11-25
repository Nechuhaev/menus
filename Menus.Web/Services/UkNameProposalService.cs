using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Services
{
    public class UkNameProposal
    {
        public string RestaurantId { get; set; }
        public string Name { get; set; }
        public string Proposed { get; set; }
        public string Source { get; set; }
        public string Slug { get; set; }
        public string CityId { get; set; }
        public string CityEn { get; set; }   // заповнює контролер (адреса сторінки закладу)
        public int Clicks { get; set; }
    }

    // «Звіти» -> «Українські назви»: заклади з російською назвою (content-bot/uk_names.py пропонує українську).
    // Підтверджена назва пишеться в українську версію заголовка сторінки (RestaurantSeo.Header, uk) - звідти її беруть
    // H1 і title (SeoText.TitleName). Назва закладу (Restaurants.Name) і російська сторінка не змінюються.
    public class UkNameProposalService
    {
        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS UkNameProposals (
                RestaurantId VARCHAR(255) NOT NULL PRIMARY KEY,
                Name VARCHAR(255) NOT NULL,
                Proposed VARCHAR(255) NULL,
                Source VARCHAR(32) NULL,
                Clicks INT NOT NULL DEFAULT 0,
                Status TINYINT NOT NULL DEFAULT 0,
                OldHeader TEXT NULL,
                CreatedAt DATETIME NOT NULL,
                DecidedAt DATETIME NULL
            ) DEFAULT CHARSET=utf8mb4";

        private static bool _ready;
        private readonly IDbConnection _connection;

        public UkNameProposalService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_ready) return;
            await _connection.ExecuteAsync(CreateTableSql);
            _ready = true;
        }

        public async Task<int> GetPendingCount()
        {
            try
            {
                await Ensure();
                return await _connection.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM UkNameProposals p JOIN Restaurants r ON r.Id = p.RestaurantId
                    WHERE p.Status = 0 AND r.Status = 0");
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // спершу заклади з більшою кількістю кліків з Google
        public async Task<List<UkNameProposal>> GetPending()
        {
            await Ensure();
            return (await _connection.QueryAsync<UkNameProposal>(@"
                SELECT CONCAT(p.RestaurantId, '') AS RestaurantId, p.Name, p.Proposed, p.Source, p.Clicks, rs.Url AS Slug, CONCAT(r.City, '') AS CityId
                FROM UkNameProposals p
                JOIN Restaurants r ON r.Id = p.RestaurantId
                LEFT JOIN RestaurantSeo rs ON rs.ItemId = r.Id
                WHERE p.Status = 0 AND r.Status = 0
                ORDER BY p.Clicks DESC, p.Name")).ToList();
        }

        // назва - в українську версію заголовка; попередній заголовок - у OldHeader (для відкату)
        public async Task<bool> Approve(string restaurantId, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 255) return false;
            await Ensure();
            var header = await _connection.ExecuteScalarAsync<string>("SELECT Header FROM RestaurantSeo WHERE ItemId = @restaurantId", new { restaurantId });
            JObject json;
            try { json = string.IsNullOrWhiteSpace(header) ? new JObject() : JObject.Parse(header); }
            catch (Exception) { json = new JObject(); }
            var values = json["Values"] as JArray ?? new JArray();
            var uk = values.OfType<JObject>().FirstOrDefault(v => v.Value<string>("Locale") == "uk");
            if (uk == null) values.Add(new JObject { ["Locale"] = "uk", ["Value"] = name });
            else uk["Value"] = name;
            json["Values"] = values;
            var updated = await _connection.ExecuteAsync("UPDATE RestaurantSeo SET Header = @value WHERE ItemId = @restaurantId",
                new { restaurantId, value = json.ToString(Newtonsoft.Json.Formatting.None) });
            if (updated == 0) return false;
            await _connection.ExecuteAsync(@"UPDATE UkNameProposals SET Status = 1, Proposed = @name, OldHeader = @header, DecidedAt = NOW()
                WHERE RestaurantId = @restaurantId", new { restaurantId, name, header });
            return true;
        }

        public async Task Reject(string restaurantId)
        {
            await Ensure();
            await _connection.ExecuteAsync("UPDATE UkNameProposals SET Status = 2, DecidedAt = NOW() WHERE RestaurantId = @restaurantId", new { restaurantId });
        }
    }
}

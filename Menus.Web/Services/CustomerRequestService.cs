using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class CustomerRequestRow
    {
        public string Id { get; set; }
        public string RestaurantName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Package { get; set; }
        public int Status { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime UpdateDate { get; set; }
        public string Ip { get; set; }
        // «Request for Access»: у RestaurantName лежить Id ресторану, тут - його назва
        public string RestaurantTitle { get; set; }
        public bool IsAccessRequest => (Package ?? "").IndexOf("Request for Access", StringComparison.Ordinal) > -1;
    }

    public class CustomerRequestFilter
    {
        public string Restaurant { get; set; }
        public string Package { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Ip { get; set; }
        public int? Status { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    // «Запити» в адмінці: список із фільтрами по стовпцях, редагування, видалення; IP заявки (таблиця CustomerRequestIps,
    // пишеться для нових заявок - у старих IP немає)
    public class CustomerRequestService
    {
        public const int PageSize = 30;

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS CustomerRequestIps (
                RequestId VARCHAR(255) NOT NULL PRIMARY KEY,
                Ip VARCHAR(64) NULL,
                UserAgent VARCHAR(512) NULL
            ) DEFAULT CHARSET=utf8mb4";

        private static bool _tableReady;
        private readonly IDbConnection _connection;

        public CustomerRequestService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_tableReady) return;
            await _connection.ExecuteAsync(CreateTableSql);
            _tableReady = true;
        }

        public async Task SaveIp(string requestId, HttpRequest request)
        {
            try
            {
                await Ensure();
                var ip = Menus.Web.Controllers.HomeController.GetIpAddress(request);
                ip = string.IsNullOrEmpty(ip) ? null : ip.Length > 64 ? ip.Substring(0, 64) : ip;
                string agent = request.Headers["User-Agent"];
                agent = string.IsNullOrEmpty(agent) ? null : agent.Length > 512 ? agent.Substring(0, 512) : agent;
                await _connection.ExecuteAsync(
                    "INSERT INTO CustomerRequestIps (RequestId, Ip, UserAgent) VALUES (@requestId, @ip, @agent) ON DUPLICATE KEY UPDATE Ip = @ip, UserAgent = @agent",
                    new { requestId, ip, agent });
            }
            catch (Exception)
            {
                // IP - довідкова інформація, заявка вже збережена
            }
        }

        public async Task<(List<CustomerRequestRow> Rows, int Total)> List(CustomerRequestFilter f, int page)
        {
            await Ensure();
            var where = new List<string>();
            string Like(string v) => "%" + (v ?? "").Trim() + "%";
            if (!string.IsNullOrWhiteSpace(f.Restaurant)) where.Add("(c.RestaurantName LIKE @restaurant OR r.Name LIKE @restaurant)");
            if (!string.IsNullOrWhiteSpace(f.Package)) where.Add("c.Package LIKE @package");
            if (!string.IsNullOrWhiteSpace(f.Email)) where.Add("c.Email LIKE @email");
            if (!string.IsNullOrWhiteSpace(f.Phone)) where.Add("c.Phone LIKE @phone");
            if (!string.IsNullOrWhiteSpace(f.Ip)) where.Add("i.Ip LIKE @ip");
            if (f.Status.HasValue) where.Add("c.Status = @status");
            if (f.From.HasValue) where.Add("c.CreateDate >= @from");
            if (f.To.HasValue) where.Add("c.CreateDate < @to");
            var sql = @"FROM CustomerRequests c
                LEFT JOIN CustomerRequestIps i ON i.RequestId = c.Id
                LEFT JOIN Restaurants r ON r.Id = c.RestaurantName" +
                      (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "");
            var args = new
            {
                restaurant = Like(f.Restaurant), package = Like(f.Package), email = Like(f.Email), phone = Like(f.Phone), ip = Like(f.Ip),
                status = f.Status, from = f.From?.Date, to = f.To?.Date.AddDays(1),
                skip = Math.Max(0, page) * PageSize, take = PageSize
            };
            var total = await _connection.ExecuteScalarAsync<int>("SELECT COUNT(*) " + sql, args);
            var rows = (await _connection.QueryAsync<CustomerRequestRow>(
                "SELECT c.Id, c.RestaurantName, c.Email, c.Phone, c.Package, c.Status, c.CreateDate, c.UpdateDate, i.Ip, r.Name AS RestaurantTitle " +
                sql + " ORDER BY c.CreateDate DESC LIMIT @skip, @take", args)).ToList();
            return (rows, total);
        }

        public async Task<CustomerRequestRow> Get(string id)
        {
            await Ensure();
            return await _connection.QueryFirstOrDefaultAsync<CustomerRequestRow>(@"
                SELECT c.Id, c.RestaurantName, c.Email, c.Phone, c.Package, c.Status, c.CreateDate, c.UpdateDate, i.Ip, r.Name AS RestaurantTitle
                FROM CustomerRequests c LEFT JOIN CustomerRequestIps i ON i.RequestId = c.Id LEFT JOIN Restaurants r ON r.Id = c.RestaurantName
                WHERE c.Id = @id", new { id });
        }

        public async Task Update(string id, string restaurantName, string package, string email, string phone, int status)
        {
            await _connection.ExecuteAsync(@"
                UPDATE CustomerRequests SET RestaurantName = @restaurantName, Package = @package, Email = @email, Phone = @phone,
                       Status = @status, UpdateDate = NOW()
                WHERE Id = @id", new { id, restaurantName, package, email, phone, status });
        }

        public async Task SetStatus(string id, int status)
        {
            await _connection.ExecuteAsync("UPDATE CustomerRequests SET Status = @status, UpdateDate = NOW() WHERE Id = @id", new { id, status });
        }

        public async Task Delete(IEnumerable<string> ids)
        {
            var list = (ids ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (list.Count == 0) return;
            await Ensure();
            await _connection.ExecuteAsync("DELETE FROM CustomerRequests WHERE Id IN @list", new { list });
            await _connection.ExecuteAsync("DELETE FROM CustomerRequestIps WHERE RequestId IN @list", new { list });
        }
    }
}

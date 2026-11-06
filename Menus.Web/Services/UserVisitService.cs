using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    public class UserVisit
    {
        public string UserId { get; set; }
        public DateTime FirstSeenAt { get; set; }
        public string FirstIp { get; set; }
        public DateTime LastSeenAt { get; set; }
        public string LastIp { get; set; }
    }

    // Перший і останній візит залогіненого користувача з IP - для адмінки «Пользователи».
    // Пише middleware в Startup на кожен запит користувача, що увійшов, але не частіше ніж раз на Throttle
    // (один короткий запит до бази на користувача, без впливу на швидкість сайту)
    public class UserVisitService
    {
        private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(15);
        private static readonly ConcurrentDictionary<string, DateTime> LastWritten = new ConcurrentDictionary<string, DateTime>();
        private static bool _tableReady;

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS UserVisits (
                UserId VARCHAR(255) NOT NULL PRIMARY KEY,
                FirstSeenAt DATETIME NOT NULL,
                FirstIp VARCHAR(64) NULL,
                LastSeenAt DATETIME NOT NULL,
                LastIp VARCHAR(64) NULL
            ) DEFAULT CHARSET=utf8mb4";

        private readonly IDbConnection _connection;

        public UserVisitService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        private async Task Ensure()
        {
            if (_tableReady) return;
            await _connection.ExecuteAsync(CreateTableSql);
            _tableReady = true;
        }

        public static bool ShouldTrack(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;
            var now = DateTime.UtcNow;
            return !LastWritten.TryGetValue(userId, out var last) || now - last > Throttle;
        }

        public async Task Track(string userId, string ip)
        {
            LastWritten[userId] = DateTime.UtcNow;
            ip = string.IsNullOrEmpty(ip) ? null : ip.Length > 64 ? ip.Substring(0, 64) : ip;
            await Ensure();
            await _connection.ExecuteAsync(@"
                INSERT INTO UserVisits (UserId, FirstSeenAt, FirstIp, LastSeenAt, LastIp) VALUES (@userId, UTC_TIMESTAMP(), @ip, UTC_TIMESTAMP(), @ip)
                ON DUPLICATE KEY UPDATE LastSeenAt = UTC_TIMESTAMP(), LastIp = @ip", new { userId, ip });
        }

        public async Task<Dictionary<string, UserVisit>> Get(IEnumerable<string> userIds)
        {
            var ids = userIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<string, UserVisit>();
            await Ensure();
            return (await _connection.QueryAsync<UserVisit>("SELECT * FROM UserVisits WHERE UserId IN @ids", new { ids }))
                .ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);
        }
    }
}

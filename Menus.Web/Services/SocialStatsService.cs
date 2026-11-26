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
    // «Соцмережі»: пости Facebook / Instagram / TikTok з переглядами й активністю.
    // Дані заливаються JSON-імпортом в адмінці (/admin/social): збираються з API соцмереж раз на тиждень.
    // Повторний імпорт того самого поста оновлює цифри (ключ - мережа + id поста)
    public class SocialStatsService
    {
        public static readonly Dictionary<string, string> Networks = new Dictionary<string, string>
        {
            ["facebook"] = "Facebook",
            ["instagram"] = "Instagram",
            ["tiktok"] = "TikTok",
        };

        // guests - гості (пошук страв і закладів), venues - заклади (QR-меню, Poster, Google)
        public static readonly Dictionary<string, string> Audiences = new Dictionary<string, string>
        {
            ["guests"] = "Гості",
            ["venues"] = "Заклади",
        };

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS SocialPosts (
                Network VARCHAR(20) NOT NULL,
                PostId VARCHAR(100) NOT NULL,
                Url VARCHAR(500) NULL,
                PublishedAt DATETIME NOT NULL,
                Format VARCHAR(20) NULL,
                Audience VARCHAR(20) NULL,
                Rubric VARCHAR(100) NULL,
                Text TEXT NULL,
                Views INT NOT NULL DEFAULT 0,
                Reach INT NOT NULL DEFAULT 0,
                Likes INT NOT NULL DEFAULT 0,
                Comments INT NOT NULL DEFAULT 0,
                Shares INT NOT NULL DEFAULT 0,
                Saves INT NOT NULL DEFAULT 0,
                SiteClicks INT NOT NULL DEFAULT 0,
                UpdatedAt DATETIME NOT NULL,
                PRIMARY KEY (Network, PostId)
            ) DEFAULT CHARSET=utf8mb4";

        private readonly IDbConnection _connection;

        public SocialStatsService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<List<SocialPost>> List(DateTime from, string network, string audience)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            var sql = "SELECT * FROM SocialPosts WHERE PublishedAt >= @from";
            if (!string.IsNullOrEmpty(network)) sql += " AND Network = @network";
            if (!string.IsNullOrEmpty(audience)) sql += " AND Audience = @audience";
            sql += " ORDER BY PublishedAt DESC";
            return (await _connection.QueryAsync<SocialPost>(sql, new { from, network, audience })).ToList();
        }

        public async Task<DateTime?> LastUpdate()
        {
            await _connection.ExecuteAsync(CreateTableSql);
            return await _connection.ExecuteScalarAsync<DateTime?>("SELECT MAX(UpdatedAt) FROM SocialPosts");
        }

        // повертає кількість записаних постів; невідомі мережі й пости без id/дати пропускає
        public async Task<int> Upsert(IEnumerable<SocialPost> posts)
        {
            await _connection.ExecuteAsync(CreateTableSql);
            var valid = posts.Where(p => p != null && Networks.ContainsKey(p.Network ?? "")
                                         && !string.IsNullOrWhiteSpace(p.PostId) && p.PublishedAt > DateTime.MinValue).ToList();
            foreach (var p in valid)
            {
                p.UpdatedAt = DateTime.UtcNow;
                if (p.Audience != null && !Audiences.ContainsKey(p.Audience)) p.Audience = null;
                await _connection.ExecuteAsync(@"
                    INSERT INTO SocialPosts (Network, PostId, Url, PublishedAt, Format, Audience, Rubric, Text,
                        Views, Reach, Likes, Comments, Shares, Saves, SiteClicks, UpdatedAt)
                    VALUES (@Network, @PostId, @Url, @PublishedAt, @Format, @Audience, @Rubric, @Text,
                        @Views, @Reach, @Likes, @Comments, @Shares, @Saves, @SiteClicks, @UpdatedAt)
                    ON DUPLICATE KEY UPDATE Url = VALUES(Url), PublishedAt = VALUES(PublishedAt),
                        Format = COALESCE(VALUES(Format), Format), Audience = COALESCE(VALUES(Audience), Audience),
                        Rubric = COALESCE(VALUES(Rubric), Rubric), Text = COALESCE(VALUES(Text), Text),
                        Views = VALUES(Views), Reach = VALUES(Reach), Likes = VALUES(Likes), Comments = VALUES(Comments),
                        Shares = VALUES(Shares), Saves = VALUES(Saves),
                        SiteClicks = GREATEST(VALUES(SiteClicks), SiteClicks), UpdatedAt = VALUES(UpdatedAt)", p);
            }
            return valid.Count;
        }
    }

    public class SocialPost
    {
        public string Network { get; set; }
        public string PostId { get; set; }
        public string Url { get; set; }
        public DateTime PublishedAt { get; set; }
        public string Format { get; set; }
        public string Audience { get; set; }
        public string Rubric { get; set; }
        public string Text { get; set; }
        public int Views { get; set; }
        public int Reach { get; set; }
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int Shares { get; set; }
        public int Saves { get; set; }
        public int SiteClicks { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int Engagement => Likes + Comments + Shares + Saves;

        // ER від охоплення (а якщо його немає - від переглядів)
        public double EngagementRate => (Reach > 0 ? Reach : Views) == 0 ? 0 : 100.0 * Engagement / (Reach > 0 ? Reach : Views);
    }
}

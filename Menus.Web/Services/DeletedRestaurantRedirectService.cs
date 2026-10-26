using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    // Ресторан видалено з бази, а його SEO-запис (адреса) лишився: стара сторінка в Google віддавала 404.
    // Якщо в тому ж місті є активна філія тієї ж мережі (спільний початок адреси: «il-molino-antonovycha» ->
    // «il-molino-...»), сторінка віддає 301 на неї - відвідувач і посилання не губляться
    public class DeletedRestaurantRedirectService
    {
        // перше слово адреси, яке не називає мережу
        private static readonly HashSet<string> GenericWords = new HashSet<string>
        {
            "the", "cafe", "kafe", "kafe-bar", "bar", "pub", "pab", "restoran", "restaurant", "restoratsiia", "restoratsyia",
            "pizza", "pitstsa", "sushi", "grill", "hryl", "bistro", "kava", "coffee", "kofe", "bufet", "pyvnaia", "pyvna"
        };
        private const int MinPrefixLength = 5;

        private readonly IDbConnection _connection;

        public DeletedRestaurantRedirectService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        // Адреса активної філії або null (адреси ніколи не було, ресторан не видалено чи філії немає)
        public async Task<string> FindSibling(string slug, string cityId)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrEmpty(cityId)) return null;
            var deleted = await _connection.ExecuteScalarAsync<int>(
                @"SELECT COUNT(*) FROM RestaurantSeo rs LEFT JOIN Restaurants r ON r.Id = rs.ItemId
                  WHERE rs.Url = @slug AND r.Id IS NULL", new { slug });
            if (deleted == 0) return null;

            var words = slug.ToLowerInvariant().Split('-');
            for (var n = words.Length - 1; n >= 1; n--)
            {
                var prefix = string.Join("-", words.Take(n));
                if (prefix.Length < MinPrefixLength || (n == 1 && GenericWords.Contains(prefix))) continue;
                var sibling = await _connection.QueryFirstOrDefaultAsync<string>(
                    @"SELECT rs.Url FROM RestaurantSeo rs JOIN Restaurants r ON r.Id = rs.ItemId
                      WHERE r.Status = 0 AND r.City = @cityId AND rs.Url LIKE @like
                      ORDER BY LENGTH(rs.Url), rs.Url LIMIT 1", new { cityId, like = prefix + "-%" });
                if (sibling != null) return sibling;
            }
            return null;
        }
    }
}

using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Services
{
    // Коли content-bot востаннє впевнено звірив меню ресторану з меню закладу (сайт, Expirenza, ChoiceQR):
    // RestaurantSyncLog.MenuVerifiedAt пише sync_restaurants.py. Сторінка показує «Ціни звірено ...» -
    // свіжість цін для відвідувача й пошуку. Давню дату не показуємо: вона радше відлякує
    public class MenuVerifiedService
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);

        private readonly IDbConnection _connection;

        public MenuVerifiedService(EfDbContext dbContext)
        {
            _connection = dbContext.Database.GetDbConnection();
        }

        public async Task<DateTime?> Get(string restaurantId)
        {
            if (string.IsNullOrEmpty(restaurantId)) return null;
            DateTime? verified;
            try
            {
                verified = await _connection.ExecuteScalarAsync<DateTime?>(
                    "SELECT MenuVerifiedAt FROM RestaurantSyncLog WHERE RestaurantId = @restaurantId", new { restaurantId });
            }
            catch (Exception)
            {
                return null; // таблицю чи колонку ще не створив бот - сторінка без дати
            }
            return verified.HasValue && DateTime.Now - verified.Value < MaxAge ? verified : null;
        }
    }
}

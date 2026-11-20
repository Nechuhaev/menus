using System;
using System.Threading.Tasks;
using Dapper;
using Menus.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Controllers
{
    // Скани QR-коду на стіл: iframe-меню, відкрите з ?src=qr, надсилає сюди POST (сторінку кешує Cloudflare,
    // тому рахуємо не завантаження сторінки, а цей запит). QrScanDays: день x заклад -> кількість.
    public class QrController : Controller
    {
        private readonly EfDbContext _db;

        public QrController(EfDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Hit(string id)
        {
            if (!Guid.TryParse(id, out var guid)) return NoContent();
            try
            {
                var connection = _db.Database.GetDbConnection();
                await connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS QrScanDays (
                        Day DATE NOT NULL,
                        RestaurantId VARCHAR(255) NOT NULL,
                        Scans INT NOT NULL,
                        PRIMARY KEY (Day, RestaurantId)
                    ) DEFAULT CHARSET=utf8mb4;
                    INSERT INTO QrScanDays (Day, RestaurantId, Scans)
                    SELECT UTC_DATE(), CONCAT(r.Id, ''), 1 FROM Restaurants r WHERE r.Id = @id
                    ON DUPLICATE KEY UPDATE Scans = Scans + 1", new { id = guid.ToString() });
            }
            catch (Exception)
            {
                // лічильник не має ламати меню
            }
            return NoContent();
        }
    }
}

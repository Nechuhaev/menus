using System;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Web.Models;
using Menus.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Menus.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Produces("application/json")]
    public class PromoController : ControllerBase
    {
        private readonly IDbContext _db;

        public PromoController(IDbContext db)
        {
            _db = db;
        }

        [HttpGet("{id}", Name = "Get")]
        public async Task<object> Get(string id, string locale)
        {
            var activeRestaurantPromos = await _db.Promos
                .Where(p => p.RestaurantId == id)
                .Where(p => DateTime.Now > p.StartDate && DateTime.Now < p.EndDate)
                .ToListAsync();

            if (activeRestaurantPromos.Count == 0)
            {
                return null;
            }

            var randomNumberGenerator = new Random();
            var randomPromoIndex = randomNumberGenerator.Next(0, activeRestaurantPromos.Count);
            var promo = activeRestaurantPromos[randomPromoIndex];

            return new { image = Img.Url(promo.Image.Get(locale), Quality.High), action = promo.Action };
        }
    }
}

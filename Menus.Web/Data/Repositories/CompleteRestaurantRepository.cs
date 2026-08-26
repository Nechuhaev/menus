using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class CompleteRestaurantRepository : EfSqlRepository<CompleteRestaurant, ICompleteRestaurant>
    {
        public CompleteRestaurantRepository(DbContext context) : base(context)
        {
        }
    }
}

using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class AnonymousRatingRepository : EfRepository<AnonymousRating, IAnonymousRating>
    {
        public AnonymousRatingRepository(DbContext context) : base(context)
        {
            
        }
    }
}
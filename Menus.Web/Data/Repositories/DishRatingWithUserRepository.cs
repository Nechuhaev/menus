// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class DishRatingWithUserRepository : EfProcedureRepository<DishRatingWithUser, IDishRatingWithUser>
    {
        public DishRatingWithUserRepository(DbContext context, string sql, string singleSql = "") : base(context, sql, singleSql)
        {
        }
    }
}

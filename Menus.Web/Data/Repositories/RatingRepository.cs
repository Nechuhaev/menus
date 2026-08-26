// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class RatingRepository : EfRepository<Rating, IRating>, IEnumerable
    {
        public RatingRepository(DbContext context) : base(context)
        {
            Set = _context.Set<Rating>().Include(x => x.ChildRatings);
        }

        private IQueryable<Rating> Set { get; }

        public override Expression Expression => Set.Cast<IRating>().Expression;

        public override IQueryProvider Provider => Set.Cast<IRating>().Provider;

        public override IEnumerator<IRating> GetEnumerator()
        {
            return Set.Cast<IRating>().GetEnumerator();
        }

        public override IEnumerator GetDefaultEnumerator()
        {
            return Set.Cast<IRating>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetDefaultEnumerator();
        }
    }
}

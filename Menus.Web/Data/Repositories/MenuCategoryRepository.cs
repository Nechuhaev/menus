// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Menus.Abstract;
using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class MenuCategoryRepository : EfRepositoryWithRestaurantCacheUpdate<MenuCategory, IMenuCategory>, IEnumerable
    {
        public MenuCategoryRepository(DbContext context, ITaskRunner taskRunner) : base(context, taskRunner)
        {
            Set = _context.Set<MenuCategory>()
                .Include(x => x.ChildCategoriesRelations)
                .Include(x => x.ParentCategoriesRelations)
                .Include("ChildCategoriesRelations.Child")
                .Include("ParentCategoriesRelations.Parent");
        }

        private IQueryable<MenuCategory> Set { get; }

        public override Expression Expression => Set.Cast<IMenuCategory>().Expression;

        public override IQueryProvider Provider => Set.Cast<IMenuCategory>().Provider;

        public override IEnumerator<IMenuCategory> GetEnumerator()
        {
            return Set.Cast<IMenuCategory>().GetEnumerator();
        }

        public override IEnumerator GetDefaultEnumerator()
        {
            return Set.Cast<IMenuCategory>().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetDefaultEnumerator();
        }
    }
}

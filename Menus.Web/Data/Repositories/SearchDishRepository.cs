// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class SearchDishRepository : EfRepository<SearchDish, ISearchDish>
    {
        public SearchDishRepository(DbContext context) : base(context)
        {
        }

        public override Task Add(ISearchDish entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Add(IEnumerable<ISearchDish> entities)
        {
            throw new InvalidOperationException();
        }

        public override Task Remove(ISearchDish entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Remove(IEnumerable<ISearchDish> entities)
        {
            throw new InvalidOperationException();
        }

        public override Task Update(ISearchDish entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Update(IEnumerable<ISearchDish> entities)
        {
            throw new InvalidOperationException();
        }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Data.Repositories
{
    public class DishPhotoExtendedRepository : EfRepository<DishPhotoExtended, IDishPhotoExtended>
    {
        public DishPhotoExtendedRepository(DbContext context) : base(context)
        {
        }

        public override Task Add(IDishPhotoExtended entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Add(IEnumerable<IDishPhotoExtended> entities)
        {
            throw new InvalidOperationException();
        }

        public override Task Remove(IDishPhotoExtended entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Remove(IEnumerable<IDishPhotoExtended> entities)
        {
            throw new InvalidOperationException();
        }

        public override Task Update(IDishPhotoExtended entity)
        {
            throw new InvalidOperationException();
        }

        public override Task Update(IEnumerable<IDishPhotoExtended> entities)
        {
            throw new InvalidOperationException();
        }
    }
}

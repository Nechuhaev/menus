using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Menus.Web.Data.Repositories
{
    public class MenuPhotoRepository : EfRepository<MenuPhoto, IMenuPhoto>
    {
        public MenuPhotoRepository(DbContext context) : base(context)
        {
        }
    }
}

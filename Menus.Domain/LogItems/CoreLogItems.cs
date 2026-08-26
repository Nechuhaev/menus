// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;

namespace Menus.Domain.LogItems
{
    public class CoreLogItems
    {
        private IDbContext _db;
        private Core _core;
        public CoreLogItems(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(ILogItem item)
        {
            await _db.LogItems.Add(item);
        }
    }
}

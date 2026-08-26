// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;

namespace Menus.Domain.Typos
{
    public class CoreTypos
    {
        private IDbContext _db;
        private Core _core;
        public CoreTypos(IDbContext db, Core core)
        {
            _db = db;
            _core = core;
        }

        public async Task Create(ITypoRequest typo)
        {
            await _db.TypoRequests.Add(typo);
        }

        public async Task<IEnumerable<ITypoRequest>> GetAll()
        {
            return await _db.TypoRequests.FromSql("SELECT * FROM TypoRequests ORDER BY CreateDate DESC", new Dictionary<string, object>(), false);
        }

    }
}

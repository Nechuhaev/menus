// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract;
using Menus.Abstract.Models;
using System.Collections.Generic;
using System.Linq;

namespace Menus.Web.Services
{
    public class LocaleService
    {
        private readonly IEnumerable<ILocale> _locales;
        public LocaleService(IDbContext db)
        {
            _locales = db.Locales.OrderByDescending(x => x.Default).ToList();
        }

        public IEnumerable<ILocale> GetLocales() {
            return _locales;
        }
    }
}

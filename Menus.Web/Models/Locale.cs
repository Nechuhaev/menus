// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class Locale : ILocale
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ShortName { get; set; }
        public bool Default { get; set; }
    }
}

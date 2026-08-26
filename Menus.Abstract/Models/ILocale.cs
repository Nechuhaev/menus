// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

namespace Menus.Abstract.Models
{
    public interface ILocale
    {
        string Id { get; set; }
        string Name { get; set; }
        string ShortName { get; set; }
        bool Default { get; set; }
    }
}

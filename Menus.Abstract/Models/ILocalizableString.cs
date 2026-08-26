// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface ILocalizableString
    {
        IEnumerable<ILocalizableStringValue> Values { get; }
        void Add(ILocalizableStringValue value);
        void CopyLocale(string from, string to);
        string Get(string locale, bool returnDefaultValue = false);
        string Get();
        string GetAll();
        string Find(string search);
        bool Contains(string search);
        bool IsEmpty();
        void Remove(string locale);
        void Replace(IEnumerable<ILocalizableStringValue> newValues);
    }
}

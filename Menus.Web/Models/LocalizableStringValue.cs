// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class LocalizableStringValue : ILocalizableStringValue
    {
        public string Locale { get; set; }
        public string Value { get; set; }

        public override bool Equals(object obj)
        {
            if (!(obj is LocalizableStringValue otherObj)) return false;
            return Locale == otherObj.Locale;
        }

        public override int GetHashCode()
        {
            return Locale.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }
    }
}

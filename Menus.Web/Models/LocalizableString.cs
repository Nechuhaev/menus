// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections;
using Menus.Abstract.Models;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class LocalizableString : ILocalizableString
    {
        private static readonly string defaultLocale = "ru";

        public List<LocalizableStringValue> Values { get; set; } = new List<LocalizableStringValue>();
        IEnumerable<ILocalizableStringValue> ILocalizableString.Values => Values;

        public void Add(ILocalizableStringValue value)
        {
            if (Values.Count(x => x.Locale == value.Locale) == 0) Values.Add(value as LocalizableStringValue);
        }

        public void CopyLocale(string from, string to)
        {
            var val = Get(from);
            if (string.IsNullOrEmpty(val)) return;

            Remove(to);
            Add(new LocalizableStringValue { Value = val, Locale = to });
        }

        public string Get()
        {
            return Get(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }

        public string GetAll()
        {
            string result = "";
            if (Values.Count > 0) result = Values[0].Value;
            if (Values.Count > 1)
            {
                foreach (var value in Values.Skip(1))
                {
                    result += $", {value.Value}";
                }
            }

            return result;
        }

        public string Find(string search)
        {
            search = search.ToLowerInvariant();
            var result = Values.FirstOrDefault(x => x.Value.ToLowerInvariant().Contains(search))?.Value;
            return result ?? "";
        }

        public bool Contains(string search)
        {
            return !string.IsNullOrEmpty(Find(search));
        }

        public bool IsEmpty()
        {
            return Values == null || Values.Count == 0;
        }

        public string Get(string locale, bool returnDefaultValue = false)
        {
            if (Values == null) return "";
            var value = Values.FirstOrDefault(x => x.Locale == locale);
            if (value == null && !returnDefaultValue) value = Values.FirstOrDefault(x => x.Locale == defaultLocale);
            if (value == null && !returnDefaultValue) value = Values.FirstOrDefault();

            return value != null ? value.Value : "";
        }

        public void Remove(string locale)
        {
            var value = Values.FirstOrDefault(x => x.Locale == locale);
            if (value != null) Values.Remove(value);
        }

        public void Replace(IEnumerable<ILocalizableStringValue> newValues)
        {
            Values = newValues.Where(x => !string.IsNullOrEmpty(x.Value)).Select(x => x as LocalizableStringValue).ToList();
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}

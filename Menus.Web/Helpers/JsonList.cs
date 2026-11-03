using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Menus.Web.Helpers
{
    // Список рядків з JSON (фото ресторану, cookie останніх ресторанів). Битий JSON у базі чи cookie
    // (звичайний текст замість ["..."]) давав JsonReaderException і 500 на сторінці - тепер порожній список
    // або одне значення, якщо це схоже на адресу файлу
    public static class JsonList
    {
        public static List<string> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                return (JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
            catch (JsonException)
            {
                var value = json.Trim().Trim('"');
                var looksLikeFile = !value.StartsWith("[") && !value.Contains(" ") && (value.Contains("/") || value.Contains("."));
                return looksLikeFile ? new List<string> { value } : new List<string>();
            }
        }
    }
}

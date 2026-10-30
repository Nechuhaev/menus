using System.Linq;
using Menus.Abstract.Models;

namespace Menus.Web.Helpers
{
    // Назва страви з великої літери. Немає назви мовою сторінки (страва лише з однією мовою) - береться
    // uk/ru/будь-яка інша, а не порожній рядок: раніше [0] від порожньої назви валив сторінку з 500
    public static class DishName
    {
        public static string Title(ILocalizableString name, string culture)
        {
            var text = name?.Get(culture);
            if (string.IsNullOrWhiteSpace(text)) text = name?.Get("uk");
            if (string.IsNullOrWhiteSpace(text)) text = name?.Get("ru");
            if (string.IsNullOrWhiteSpace(text)) text = name?.Values?.Select(x => x.Value).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            text = (text ?? "").Trim();
            return text.Length == 0 ? "" : char.ToUpper(text[0]) + text.Substring(1);
        }
    }
}

using System.Linq;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc.Localization;

namespace Menus.Web.Helpers
{
    // Довжина title і description для видачі: Google показує ~60 і ~160 символів, решту обрізає сам
    public static class SeoText
    {
        public const int TitleMax = 60;
        public const int DescriptionMax = 160;

        // Перший варіант, що вміщається; якщо жоден - останній, обрізаний по слову
        public static string Fit(int max, params string[] candidates)
        {
            var filled = candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            return filled.FirstOrDefault(x => x.Length <= max) ?? Truncate(filled.LastOrDefault(), max);
        }

        // Множина для укр/рос: forms = «страва|страви|страв» (1, 2-4, 5+)
        public static string Plural(int n, string forms)
        {
            var f = (forms ?? "").Split('|');
            if (f.Length < 3) return forms;
            int mod10 = n % 10, mod100 = n % 100;
            return mod10 == 1 && mod100 != 11 ? f[0] : mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14) ? f[1] : f[2];
        }

        // Шаблонний title/description, згенерований ботом для всіх ресторанів однаково (і з «?» замість емодзі,
        // бо база не зберігає 4-байтові символи) - замість нього сторінка будує текст із даних меню
        private static readonly System.Text.RegularExpressions.Regex GenericSeo = new System.Text.RegularExpressions.Regex(
            "відгуки клієнтів|отзывы клиентов|ТіпМайМеню|ТипМайМеню|електронне меню закладу|электронное меню заведения|\\?\uFE0F",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static bool IsGeneric(string text) => string.IsNullOrWhiteSpace(text) || GenericSeo.IsMatch(text);

        public static string Truncate(string text, int max)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length <= max) return text;

            var cut = text.Substring(0, max - 1);
            var space = cut.LastIndexOf(' ');
            if (space > max / 2) cut = cut.Substring(0, space);
            return cut.TrimEnd(' ', ',', ':', ';', '—', '-', '|', '(') + "…";
        }

        // ViewData може містити рядок або локалізований рядок з resx
        public static string AsText(object value)
        {
            switch (value)
            {
                case LocalizedHtmlString html: return html.Value;
                case LocalizedString localized: return localized.Value;
                default: return value?.ToString();
            }
        }
    }
}

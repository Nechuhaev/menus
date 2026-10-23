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

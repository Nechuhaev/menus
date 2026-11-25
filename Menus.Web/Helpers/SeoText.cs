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

        // російська назва закладу («Итальянская редакция №2») на українській сторінці: ознаки - ы/э/ъ/ё і закінчення -ая/-ое/-ый
        private static readonly System.Text.RegularExpressions.Regex RussianName = new System.Text.RegularExpressions.Regex(
            "[ыэъё]|\\w(ая|ое|ый)\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        private static readonly System.Text.RegularExpressions.Regex MenuPrefix = new System.Text.RegularExpressions.Regex(
            "^(меню|menu)\\s+((ресторану|кафе|бару|кав'ярні|піцерії|ресторана|бара|кофейни|пиццерии)\\s+)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // назва для title українською: якщо назва закладу російська, а H1 (seo.Header) - українська
        // («Меню ресторану Італійська редакція №2»), береться назва з H1 - саме так її шукають («італійська редакція меню»)
        public static string TitleName(string name, string header, string culture)
        {
            if (culture != "uk" || string.IsNullOrWhiteSpace(header) || !RussianName.IsMatch(name ?? "")) return name;
            var fromHeader = MenuPrefix.Replace(header.Trim(), "").Trim();
            // H1 - назва, а не речення: не довша за назву більш ніж на 2 слова («Царське село», «Улюблений Дядько» з «Звітів»)
            var words = fromHeader.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            var nameWords = (name ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
            return fromHeader.Length > 0 && fromHeader.Length <= 50 && words <= nameWords + 2 && !RussianName.IsMatch(fromHeader)
                ? fromHeader : name;
        }

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

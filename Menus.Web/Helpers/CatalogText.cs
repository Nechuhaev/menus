using System.Collections.Generic;

namespace Menus.Web.Helpers
{
    // Тексти каталогу міста (uk / ru / en) - з даних сторінки, без AI
    public static class CatalogText
    {
        // родовий відмінок міста: «Ресторани Києва», «Бары Одессы»; en - «in Kyiv»
        private static readonly Dictionary<string, (string Uk, string Ru)> Genitive = new Dictionary<string, (string, string)>
        {
            ["kyiv"] = ("Києва", "Киева"), ["kiev"] = ("Києва", "Киева"), ["odesa"] = ("Одеси", "Одессы"), ["odessa"] = ("Одеси", "Одессы"),
            ["lviv"] = ("Львова", "Львова"), ["kharkiv"] = ("Харкова", "Харькова"), ["dnipro"] = ("Дніпра", "Днепра"),
            ["zaporizhzhia"] = ("Запоріжжя", "Запорожья"), ["vinnytsia"] = ("Вінниці", "Винницы"), ["poltava"] = ("Полтави", "Полтавы"),
            ["chernihiv"] = ("Чернігова", "Чернигова"), ["ivano-frankivsk"] = ("Івано-Франківська", "Ивано-Франковска"),
            ["uzhhorod"] = ("Ужгорода", "Ужгорода"), ["lutsk"] = ("Луцька", "Луцка"), ["rivne"] = ("Рівного", "Ровно"),
            ["ternopil"] = ("Тернополя", "Тернополя"), ["chernivtsi"] = ("Чернівців", "Черновцов"), ["mykolaiv"] = ("Миколаєва", "Николаева"),
            ["kherson"] = ("Херсона", "Херсона"), ["sumy"] = ("Сум", "Сум"), ["zhytomyr"] = ("Житомира", "Житомира"),
            ["cherkasy"] = ("Черкас", "Черкасс"), ["bukovel"] = ("Буковеля", "Буковеля"),
        };

        // «Бари Києва» / «Бары Киева» / «Bars in Kyiv»; невідоме місто - «Бари: Київ»
        public static string Heading(string typeName, string citySlug, string cityName, string culture)
        {
            if (culture == "en") return $"{typeName} in {cityName}";
            if (Genitive.TryGetValue(citySlug ?? "", out var g)) return $"{typeName} {(culture == "ru" ? g.Ru : g.Uk)}";
            return $"{typeName}: {cityName}";
        }

        public static string T(string culture, string uk, string ru, string en) => culture == "en" ? en : culture == "ru" ? ru : uk;

        // 1 заклад, 2 заклади, 5 закладів
        public static string Venues(int n, string culture)
        {
            if (culture == "en") return n == 1 ? "1 place" : $"{n} places";
            var forms = culture == "ru" ? new[] { "заведение", "заведения", "заведений" } : new[] { "заклад", "заклади", "закладів" };
            return $"{n} {forms[Plural(n)]}";
        }

        public static string Dishes(int n, string culture)
        {
            if (culture == "en") return n == 1 ? "1 dish" : $"{n} dishes";
            var forms = culture == "ru" ? new[] { "блюдо", "блюда", "блюд" } : new[] { "страва", "страви", "страв" };
            return $"{n} {forms[Plural(n)]}";
        }

        private static int Plural(int n)
        {
            n = System.Math.Abs(n) % 100;
            if (n >= 11 && n <= 14) return 2;
            switch (n % 10)
            {
                case 1: return 0;
                case 2: case 3: case 4: return 1;
                default: return 2;
            }
        }
    }
}

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

        // місцевий відмінок: «в Києві», «во Львове»
        private static readonly Dictionary<string, (string Uk, string Ru)> Locative = new Dictionary<string, (string, string)>
        {
            ["kyiv"] = ("в Києві", "в Киеве"), ["kiev"] = ("в Києві", "в Киеве"), ["odesa"] = ("в Одесі", "в Одессе"), ["odessa"] = ("в Одесі", "в Одессе"),
            ["lviv"] = ("у Львові", "во Львове"), ["kharkiv"] = ("в Харкові", "в Харькове"), ["dnipro"] = ("в Дніпрі", "в Днепре"),
            ["zaporizhzhia"] = ("в Запоріжжі", "в Запорожье"), ["vinnytsia"] = ("у Вінниці", "в Виннице"), ["poltava"] = ("в Полтаві", "в Полтаве"),
            ["chernihiv"] = ("в Чернігові", "в Чернигове"), ["ivano-frankivsk"] = ("в Івано-Франківську", "в Ивано-Франковске"),
            ["uzhhorod"] = ("в Ужгороді", "в Ужгороде"), ["lutsk"] = ("в Луцьку", "в Луцке"), ["rivne"] = ("в Рівному", "в Ровно"),
            ["ternopil"] = ("в Тернополі", "в Тернополе"), ["chernivtsi"] = ("у Чернівцях", "в Черновцах"), ["mykolaiv"] = ("в Миколаєві", "в Николаеве"),
            ["kherson"] = ("в Херсоні", "в Херсоне"), ["sumy"] = ("в Сумах", "в Сумах"), ["zhytomyr"] = ("в Житомирі", "в Житомире"),
            ["cherkasy"] = ("в Черкасах", "в Черкассах"), ["bukovel"] = ("в Буковелі", "в Буковеле"),
        };

        // «Грузинська кухня в Києві» / «Грузинская кухня в Киеве» / «Georgian restaurants in Kyiv»
        public static string CuisineHeading(string cuisineName, string citySlug, string cityName, string culture)
        {
            if (culture == "en") return $"{cuisineName} restaurants in {cityName}";
            if (Locative.TryGetValue(citySlug ?? "", out var l)) return $"{cuisineName} {(culture == "ru" ? l.Ru : l.Uk)}";
            return $"{cuisineName}: {cityName}";
        }

        // «il Molino в Києві: меню всіх закладів» / «… во Львове: меню всех заведений» / «il Molino in Kyiv: menus of all locations»
        // (H1 сторінки мережі й посилання на неї з філій)
        public static string ChainHeading(string chainName, string citySlug, string cityName, string culture)
        {
            if (culture == "en") return $"{chainName} in {cityName}: menus of all locations";
            var loc = Locative.TryGetValue(citySlug ?? "", out var l) ? (culture == "ru" ? l.Ru : l.Uk) : null;
            var tail = culture == "ru" ? "меню всех заведений" : "меню всіх закладів";
            return loc != null ? $"{chainName} {loc}: {tail}" : $"{chainName}, {cityName}: {tail}";
        }

        // <title> сторінки мережі під запит «<мережа> меню»: «Черноморка меню й ціни 2026: 3 заклади в Києві»
        public static string ChainTitle(string chainName, int venues, string citySlug, string cityName, string culture, int year)
        {
            if (culture == "en") return $"{chainName} menu & prices {year}: {Venues(venues, "en")} in {cityName}";
            var loc = Locative.TryGetValue(citySlug ?? "", out var l) ? (culture == "ru" ? l.Ru : l.Uk) : ", " + cityName;
            return culture == "ru"
                ? $"{chainName} меню и цены {year}: {Venues(venues, "ru")} {loc}".Replace(" , ", ", ")
                : $"{chainName} меню й ціни {year}: {Venues(venues, "uk")} {loc}".Replace(" , ", ", ");
        }

        // заголовок особливості з шаблону: {gen} - «Києва», {loc} - «в Києві», {city} - назва міста
        public static string FeatureHeading(string template, string citySlug, string cityName, string culture)
        {
            var gen = Genitive.TryGetValue(citySlug ?? "", out var g) ? (culture == "ru" ? g.Ru : g.Uk) : null;
            var loc = Locative.TryGetValue(citySlug ?? "", out var l) ? (culture == "ru" ? l.Ru : l.Uk) : null;
            return (template ?? "")
                .Replace("{gen}", gen ?? ": " + cityName).Replace(" : ", ": ")
                .Replace("{loc}", loc ?? "- " + cityName)
                .Replace("{city}", cityName);
        }

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

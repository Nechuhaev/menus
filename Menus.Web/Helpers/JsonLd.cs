using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Html;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Menus.Web.Helpers
{
    // Розмітка schema.org у форматі JSON-LD. Порожні поля не виводяться,
    // щоб Google не бачив неповних чи хибних даних.
    public static class JsonLd
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Екранує <, >, &, ' - вміст із бази не може закрити тег <script>
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        };

        public static IHtmlContent Script(params object[] items)
        {
            var html = string.Concat(items.Where(x => x != null).Select(x =>
                $"<script type=\"application/ld+json\">{JsonConvert.SerializeObject(x, Settings)}</script>"));
            return new HtmlString(html);
        }

        public static object Restaurant(string name, string url, string image, string phone, string cuisine,
            string street, string city, string latitude, string longitude, string website,
            object openingHours = null, string priceRange = null)
        {
            var address = Obj(("@type", "PostalAddress"), ("streetAddress", street), ("addressLocality", city), ("addressCountry", "UA"));
            var hasAddress = !string.IsNullOrWhiteSpace(street) || !string.IsNullOrWhiteSpace(city);

            object geo = null;
            if (TryParseCoordinate(latitude, out var lat) && TryParseCoordinate(longitude, out var lon) && (lat != 0 || lon != 0))
                geo = Obj(("@type", "GeoCoordinates"), ("latitude", lat), ("longitude", lon));

            return Obj(
                ("@context", "https://schema.org"),
                ("@type", "Restaurant"),
                ("name", name),
                ("url", url),
                ("image", image),
                ("telephone", phone),
                ("servesCuisine", cuisine),
                ("address", hasAddress ? address : null),
                ("geo", geo),
                ("hasMenu", url),
                ("openingHoursSpecification", openingHours),
                ("priceRange", priceRange),
                ("sameAs", IsHttpUrl(website) ? website.Trim() : null));
        }

        private static readonly string[] Weekdays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        // Графік із адмінки - JSON-масив 7 днів з понеділка: {active, from, to}. Дні з однаковими годинами
        // об'єднуються в один запис; некоректний графік не виводиться зовсім
        public static object OpeningHours(string scheduleJson)
        {
            JArray days;
            try { days = JArray.Parse(scheduleJson ?? ""); }
            catch (JsonException) { return null; }
            if (days.Count != 7) return null;

            var groups = new List<(string Opens, string Closes, List<string> Days)>();
            for (var i = 0; i < 7; i++)
            {
                var day = days[i];
                if (day?["active"]?.Type != JTokenType.Boolean || !day["active"].Value<bool>()) continue;
                var opens = day["from"]?.ToString();
                var closes = day["to"]?.ToString();
                if (!IsTime(opens) || !IsTime(closes)) return null;

                var group = groups.FirstOrDefault(g => g.Opens == opens && g.Closes == closes);
                if (group.Days == null) groups.Add((opens, closes, new List<string> { Weekdays[i] }));
                else group.Days.Add(Weekdays[i]);
            }
            if (groups.Count == 0) return null;

            return groups.Select(g => Obj(("@type", "OpeningHoursSpecification"),
                ("dayOfWeek", g.Days.ToArray()), ("opens", g.Opens), ("closes", g.Closes))).ToList();
        }

        // Діапазон типових цін меню (25-й - 75-й перцентиль, округлено до 10): «150–450 UAH»
        public static string PriceRange(IEnumerable<decimal> prices, string currency)
        {
            var code = CurrencyCode(currency);
            return code == null ? null : TypicalPrices(prices, code);
        }

        // Те саме для тексту на сторінці: «150–450 грн»; null, якщо цін менше 5
        public static string TypicalPrices(IEnumerable<decimal> prices, string unit)
        {
            var list = prices.Where(x => x > 0).OrderBy(x => x).ToList();
            if (list.Count < 5) return null;

            decimal Round10(decimal x) => Math.Max(10, Math.Round(x / 10) * 10);
            var low = Round10(list[list.Count / 4]);
            var high = Round10(list[list.Count * 3 / 4]);
            return (low == high ? $"{low:0} {unit}" : $"{low:0}–{high:0} {unit}").Trim();
        }

        // Графік для тексту на сторінці: «Пн–Пт 10:00–22:00; Сб–Нд 11:00–23:00».
        // Сусідні дні з однаковими годинами об'єднуються; некоректний графік - null
        public static string ScheduleText(string scheduleJson, string[] dayNames)
        {
            JArray days;
            try { days = JArray.Parse(scheduleJson ?? ""); }
            catch (JsonException) { return null; }
            if (days.Count != 7 || dayNames.Length != 7) return null;

            var hours = new string[7];
            for (var i = 0; i < 7; i++)
            {
                var day = days[i];
                if (day?["active"]?.Type != JTokenType.Boolean || !day["active"].Value<bool>()) continue;
                var opens = day["from"]?.ToString();
                var closes = day["to"]?.ToString();
                if (!IsTime(opens) || !IsTime(closes)) return null;
                hours[i] = $"{opens}–{closes}";
            }

            var parts = new List<string>();
            for (var i = 0; i < 7; i++)
            {
                if (hours[i] == null) continue;
                var end = i;
                while (end < 6 && hours[end + 1] == hours[i]) end++;
                parts.Add($"{(end == i ? dayNames[i] : dayNames[i] + "–" + dayNames[end])} {hours[i]}");
                i = end;
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        private static bool IsTime(string value) =>
            !string.IsNullOrEmpty(value) && System.Text.RegularExpressions.Regex.IsMatch(value, @"^([01]?\d|2[0-4]):[0-5]\d$");

        // Google вимагає для Product хоча б offers або aggregateRating - без них розмітку не виводимо
        public static object Product(string name, string url, string image, string description, string brand,
            decimal price, string currency, float rating, int ratingCount)
        {
            var currencyCode = CurrencyCode(currency);
            var offers = price > 0 && currencyCode != null
                ? Obj(("@type", "Offer"), ("price", price.ToString("0.##", CultureInfo.InvariantCulture)),
                      ("priceCurrency", currencyCode), ("availability", "https://schema.org/InStock"), ("url", url))
                : null;
            var aggregateRating = ratingCount > 0 && rating >= 1 && rating <= 5
                ? Obj(("@type", "AggregateRating"), ("ratingValue", Math.Round(rating, 1)), ("ratingCount", ratingCount),
                      ("bestRating", 5), ("worstRating", 1))
                : null;

            if (offers == null && aggregateRating == null) return null;

            return Obj(
                ("@context", "https://schema.org"),
                ("@type", "Product"),
                ("name", name),
                ("url", url),
                ("image", image),
                ("description", description),
                ("brand", string.IsNullOrWhiteSpace(brand) ? null : Obj(("@type", "Brand"), ("name", brand))),
                ("offers", offers),
                ("aggregateRating", aggregateRating));
        }

        // Дані компанії: однакові на головній і в «Про нас»
        public const string LegalName = "ТОВ «АДДНЬЮ»";
        public const string FoundingYear = "2012";
        public const string ContactEmail = "info@tipmymenu.com";

        // Головна: сайт і організація пов'язані через @id, щоб пошуковики й AI бачили одну сутність
        public static object Organization(string siteUrl, string logo, params string[] sameAs) =>
            Obj(
                ("@context", "https://schema.org"),
                ("@type", "Organization"),
                ("@id", siteUrl + "#organization"),
                ("name", "TipMyMenu"),
                ("legalName", LegalName),
                ("foundingDate", FoundingYear),
                ("email", ContactEmail),
                ("url", siteUrl),
                ("logo", logo),
                ("contactPoint", Obj(("@type", "ContactPoint"), ("contactType", "customer support"), ("email", ContactEmail))),
                ("sameAs", sameAs.Where(IsHttpUrl).ToArray()));

        // «Про нас»: сторінка про організацію, та сама сутність через @id
        public static object AboutPage(string siteUrl, string pageUrl, string name) =>
            Obj(
                ("@context", "https://schema.org"),
                ("@type", "AboutPage"),
                ("url", pageUrl),
                ("name", name),
                ("about", Obj(("@id", siteUrl + "#organization"))));

        public static object WebSite(string siteUrl, string homeUrl, string language, string searchUrl) =>
            Obj(
                ("@context", "https://schema.org"),
                ("@type", "WebSite"),
                ("@id", siteUrl + "#website"),
                ("name", "TipMyMenu"),
                ("url", homeUrl),
                ("inLanguage", language),
                ("publisher", Obj(("@id", siteUrl + "#organization"))),
                ("potentialAction", Obj(
                    ("@type", "SearchAction"),
                    ("target", Obj(("@type", "EntryPoint"), ("urlTemplate", searchUrl + "?q={search_term_string}"))),
                    ("query-input", "required name=search_term_string"))));

        public static object FaqPage(params (string Question, string Answer)[] items)
        {
            var list = items.Where(x => !string.IsNullOrWhiteSpace(x.Question) && !string.IsNullOrWhiteSpace(x.Answer))
                .Select(x => Obj(("@type", "Question"), ("name", x.Question),
                    ("acceptedAnswer", Obj(("@type", "Answer"), ("text", x.Answer)))))
                .ToList();
            if (list.Count == 0) return null;

            return Obj(("@context", "https://schema.org"), ("@type", "FAQPage"), ("mainEntity", list));
        }

        // Список сторінок (заклади зі стравою на сторінці «страва + місто»)
        public static object ItemList(string name, IEnumerable<(string Name, string Url)> items)
        {
            var list = items.Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Url))
                .Select((x, i) => Obj(("@type", "ListItem"), ("position", i + 1), ("name", x.Name.Trim()), ("url", x.Url)))
                .ToList();
            if (list.Count == 0) return null;

            return Obj(("@context", "https://schema.org"), ("@type", "ItemList"), ("name", name),
                ("numberOfItems", list.Count), ("itemListElement", list));
        }

        public static object Breadcrumbs(params (string Name, string Url)[] items)
        {
            var list = items.Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Url))
                .Select((x, i) => Obj(("@type", "ListItem"), ("position", i + 1), ("name", x.Name.Trim()), ("item", x.Url)))
                .ToList();
            if (list.Count < 2) return null;

            return Obj(("@context", "https://schema.org"), ("@type", "BreadcrumbList"), ("itemListElement", list));
        }

        public static string CurrencyCode(string currency)
        {
            switch ((currency ?? "").Trim().TrimEnd('.').ToLowerInvariant())
            {
                case "грн":
                case "uah":
                case "₴":
                    return "UAH";
                default:
                    return null;
            }
        }

        private static Dictionary<string, object> Obj(params (string Key, object Value)[] pairs)
        {
            var result = new Dictionary<string, object>();
            foreach (var (key, value) in pairs)
            {
                if (value == null) continue;
                if (value is string s)
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    result[key] = s.Trim();
                    continue;
                }
                result[key] = value;
            }
            return result;
        }

        private static bool TryParseCoordinate(string value, out double result) =>
            double.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        private static bool IsHttpUrl(string value) =>
            Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
    }
}

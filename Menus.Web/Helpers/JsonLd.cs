using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Html;
using Newtonsoft.Json;

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
            string street, string city, string latitude, string longitude, string website)
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
                ("sameAs", IsHttpUrl(website) ? website.Trim() : null));
        }

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

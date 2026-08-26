using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Menus.Abstract;
using Menus.Abstract.Models;

namespace Menus.Web.Services
{
    public class UrlGenerateService
    {
        private readonly IDbContext _db;
        private static IEnumerable<ICity> _cities;

        public UrlGenerateService(IDbContext db)
        {
            _db = db;
            if (_cities == null)
            {
                _cities = _db.Cities.ToList();
            }
        }

        public static string TranslitWithCaseSave(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            input = input.Trim();
            var letters = new Dictionary<string, string>
            {
                {"і" + '\x0308', "i"} ,
                {"и" + '\x0306', "i"} ,
                {"а", "a"} ,
                {"б", "b"} ,
                {"в", "v"} ,
                {"г", "h"} ,
                {"ґ", "g"} ,
                {"д", "d"} ,
                {"е", "e"} ,
                {"ё", "yo"} ,
                {"є", "ie"} ,
                {"ж", "zh"} ,
                {"з", "z"} ,
                {"и", "y"} ,
                {"і", "i"} ,
                {"ї", "i"} ,
                {"й", "y"} ,
                {"к", "k"} ,
                {"л", "l"} ,
                {"м", "m"} ,
                {"н", "n"} ,
                {"о", "o"} ,
                {"п", "p"} ,
                {"р", "r"} ,
                {"с", "s"} ,
                {"т", "t"} ,
                {"у", "u"} ,
                {"ф", "f"} ,
                {"х", "kh"} ,
                {"ц", "ts"} ,
                {"ч", "ch"} ,
                {"ш", "sh"} ,
                {"щ", "shch"} ,
                {"ь", ""} ,
                {"ъ", ""} ,
                {"ы", "y"} ,
                {"э", "e"} ,
                {"ю", "iu"} ,
                {"я", "ia"} ,
                {"’", ""} ,
            };

            //input = input.ToLower();
            //input = letters.Aggregate(input, (current, value) => current.Replace(value.Key, value.Value));

            var result = input.Select(x =>
            {
                var isUpper = char.IsUpper(x);
                var lc = char.ToLower(x);
                if (!letters.Any(l => l.Key.First() == lc)) return x.ToString();

                var replacement = letters.First(l => l.Key.First() == lc);

                return isUpper ? replacement.Value.FirstCharToUpper() : replacement.Value;
            });

            var builder = new StringBuilder();

            return builder.AppendJoin("", result).ToString();
        }

        public static string Translit(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            input = input.Trim();
            var letters = new Dictionary<string, string>
            {
                {"і" + '\x0308', "i"} ,
                {"и" + '\x0306', "i"} ,
                {"а", "a"} ,
                {"б", "b"} ,
                {"в", "v"} ,
                {"г", "h"} ,
                {"ґ", "g"} ,
                {"д", "d"} ,
                {"е", "e"} ,
                {"ё", "yo"} ,
                {"є", "ie"} ,
                {"ж", "zh"} ,
                {"з", "z"} ,
                {"и", "y"} ,
                {"і", "i"} ,
                {"ї", "i"} ,
                {"й", "y"} ,
                {"к", "k"} ,
                {"л", "l"} ,
                {"м", "m"} ,
                {"н", "n"} ,
                {"о", "o"} ,
                {"п", "p"} ,
                {"р", "r"} ,
                {"с", "s"} ,
                {"т", "t"} ,
                {"у", "u"} ,
                {"ф", "f"} ,
                {"х", "kh"} ,
                {"ц", "ts"} ,
                {"ч", "ch"} ,
                {"ш", "sh"} ,
                {"щ", "shch"} ,
                {"ь", ""} ,
                {"ъ", ""} ,
                {"ы", "y"} ,
                {"э", "e"} ,
                {"ю", "iu"} ,
                {"я", "ia"} ,
                {"’", ""} ,
            };

            input = input.ToLower();
            //input = letters.Aggregate(input, (current, value) => current.Replace(value.Key, value.Value));
            foreach (var letter in letters)
            {
                input = input.Replace(letter.Key, letter.Value);
            }

            input = new Regex(@"[^\w\s]").Replace(input, "");
            input = new Regex(@"[\s]{2,}").Replace(input, " ");
            input = new Regex(@"[\s]").Replace(input, "-");

            return input;
        }

        public async Task<string> GetNewUrlForDish(IDishSeo dishSeo, IDishBase dish)
        {
            var parameters = new Dictionary<string, object> {
                { "@restaurantId", dish.RestaurantId }
            };
            
            var dishSeos = await _db.DishSeo.FromSql(@"SELECT
                  Id,
                  ItemId,
                  Url,
                  Header,
                  Title,
                  Description,
                  Keywords,
                  SeoText,
                  RestaurantId,
                  SearchTerms
                FROM DishSeo Where RestaurantId = @restaurantId", parameters, false) as List<IDishSeo>;

            return GetNewUrlForDish(dishSeo, dish, dishSeos);
        }

        public string GetNewUrlForDish(IDishSeo dishSeo, IDishBase dish, List<IDishSeo> dishSeos)
        {
            dishSeo.Url = Translit(dish.Name.Get());
            string uniqueUrl = dishSeo.Url.Replace("-copy", "").Replace("copy", "").Replace("numbers", "");
            uniqueUrl = Regex.Replace(uniqueUrl, "-*[0-9]+", "");
            uniqueUrl = uniqueUrl.TrimEnd('-');

            bool nameIsUnique = dishSeos.Find(x => x.Url == dishSeo.Url && x.RestaurantId == dishSeo.RestaurantId && x.Id != dishSeo.Id) == null;
            int attemptNumber = 0;

            while (!nameIsUnique)
            {
                uniqueUrl = GenerateUniqueUrlForDish(dishSeo, dish, attemptNumber);
                nameIsUnique = dishSeos.Find(x => x.Url == uniqueUrl && x.RestaurantId == dishSeo.RestaurantId && x.Id != dishSeo.Id) == null;
                attemptNumber++;

            }

            return uniqueUrl;
        }

        public string GetNewUrlForRestaurant(IRestaurantSeo restaurantSeo, IRestaurant restaurant)
        {
            var existingSeos = _db.RestaurantSeo.Select(x => new {x.Id, x.ItemId, x.Url}).ToList();
            restaurantSeo.Url = Translit(restaurant.Name);
            string uniqueUrl = restaurantSeo.Url.Replace("-copy", "").Replace("copy", "").Replace("numbers", "");
            uniqueUrl = Regex.Replace(uniqueUrl, "-*[0-9]+", "");
            uniqueUrl = uniqueUrl.TrimEnd('-');

            bool nameIsUnique = !existingSeos.Any(x => x.Url == uniqueUrl && x.Id != restaurantSeo.Id);
            int attemptNumber = 0;

            while (!nameIsUnique)
            {
                uniqueUrl = GenerateUniqueUrlForRestaurant(restaurantSeo, restaurant, attemptNumber);
                nameIsUnique = !existingSeos.Any(x => x.Url == uniqueUrl && x.Id != restaurantSeo.Id);
                attemptNumber++;
            }

            return uniqueUrl;
        }

        private static string GenerateUniqueUrlForDish(IDishSeo dishSeo, IDishBase dish, int attemptNumber)
        {

            string url = dishSeo.Url;
            var possibleAddOnString = Translit(dish.ShortDescription.Get());
            var addOnSubstrings =
                !string.IsNullOrEmpty(possibleAddOnString) ? possibleAddOnString.Split("-") : new string[0];

            if (attemptNumber < addOnSubstrings.Length)
            {
                url += $"-{addOnSubstrings[attemptNumber]}";
            }
            else
            {
                string addOn = GetTextNumber(attemptNumber - addOnSubstrings.Length + 1);
                url += $"-{addOn}";
            }

            return url;
        }

        private string GenerateUniqueUrlForRestaurant(IRestaurantSeo restaurantSeo, IRestaurant restaurant, int attemptNumber)
        {

            string url = restaurantSeo.Url;
            var city = _cities.First(x => x.Id == restaurant.City);
            var possibleAddOnString = city.Name.Get("en").ToLower() + "-" + Translit(restaurant.Address);
            var addOnSubstrings =
                !string.IsNullOrEmpty(possibleAddOnString) ? possibleAddOnString.Split("-") : new string[0];

            if (attemptNumber < addOnSubstrings.Length)
            {
                url += $"-{addOnSubstrings[attemptNumber]}";
            }
            else
            {
                string addOn = GetTextNumber(attemptNumber - addOnSubstrings.Length + 1);
                url += $"-{addOn}";
            }

            return url;
        }

        private static string GetTextNumber(int number)
        {
            string name = "1";
            switch (number)
            {

                case 0:
                    name = "zero";
                    break;
                case 1:
                    name = "one";
                    break;
                case 2:
                    name = "two";
                    break;
                case 3:
                    name = "three";
                    break;
                case 4:
                    name = "four";
                    break;
                case 5:
                    name = "five";
                    break;
                case 6:
                    name = "six";
                    break;
                case 7:
                    name = "seven";
                    break;
                case 8:
                    name = "eight";
                    break;
                case 9:
                    name = "nine";
                    break;
            }
            return name;
        }
    }

    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input)
        {
            switch (input)
            {
                case null: return "";
                case "": return "";
                default: return input.First().ToString().ToUpper() + input.Substring(1);
            }
        }
    }
}

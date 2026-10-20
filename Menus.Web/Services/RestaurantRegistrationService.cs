using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Menus.Abstract.Models;
using Menus.Domain;
using Menus.Web.Models;
using Microsoft.Extensions.Configuration;

namespace Menus.Web.Services
{
    // Реєстрація ресторану користувачем: сам ресторан, ролі власника й автора, початкова інформація та SEO
    public class RestaurantRegistrationService
    {
        private const string DefaultOpenHours = "{\"perDay\":false,\"data\":[{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true},{\"from\":\"11:00\",\"to\":\"23:00\",\"active\":true}]}";

        private readonly Core _core;
        private readonly IConfiguration _config;
        private readonly UrlGenerateService _urls;

        public RestaurantRegistrationService(Core core, IConfiguration config, UrlGenerateService urls)
        {
            _core = core;
            _config = config;
            _urls = urls;
        }

        // Повертає Id нового ресторану
        public async Task<string> Register(Restaurant model, string cuisine, string culture, string userName)
        {
            var autoApproveUserRestaurants = _config["AutoApproveUserRestaurants"] == "true";

            model.Id = Guid.NewGuid().ToString();
            model.Status = autoApproveUserRestaurants ? RestaurantStatus.Active : RestaurantStatus.Pending;
            model.CreateDate = DateTime.Now;

            await _core.Restaurants.Create(model, new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                Role = RestaurantUserRole.Owner,
                UserId = userName
            });

            await _core.Restaurants.CreateRestaurantRole(new RestaurantRole
            {
                Id = Guid.NewGuid().ToString(),
                RestaurantId = model.Id,
                UserId = userName,
                Role = RestaurantUserRole.Creator
            });

            var restaurantInfo = new RestaurantInfo() as IRestaurantInfo;
            restaurantInfo.Id = Guid.NewGuid().ToString();
            restaurantInfo.Currency = "₴";
            restaurantInfo.Cuisine = string.IsNullOrEmpty(cuisine) ? new LocalizableString() : new LocalizableString{Values = new List<LocalizableStringValue>{ new LocalizableStringValue{ Value = cuisine, Locale = culture } }};
            restaurantInfo.FullInfo = new LocalizableString();
            restaurantInfo.ShortInfo = new LocalizableString();
            restaurantInfo.DisplayAddress = new LocalizableString { Values = new List<LocalizableStringValue> { new LocalizableStringValue { Value = model.Address, Locale = culture } } };
            restaurantInfo.RestaurantId = model.Id;
            restaurantInfo.OpenHours = DefaultOpenHours;
            restaurantInfo.UserAllowedAddDishes = true;
            restaurantInfo.UserAllowedAddMenu = true;
            restaurantInfo.UserAllowedOpenMenu = true;
            await _core.Restaurants.UpdateRestaurantInfo(restaurantInfo);

            var seo = new RestaurantSeo() as IRestaurantSeo;
            seo.Id = Guid.NewGuid().ToString();
            seo.Title = new LocalizableString();
            seo.Header = new LocalizableString();
            seo.Description = new LocalizableString();
            seo.ItemId = model.Id;
            seo.Url = _urls.GetNewUrlForRestaurant(seo, model);
            seo.SearchTerms = model.Name + " " + seo.Url;
            await _core.Restaurants.CreateSeo(seo);

            return model.Id;
        }
    }
}

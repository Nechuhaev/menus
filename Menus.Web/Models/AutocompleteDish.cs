// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace Menus.Web.Models
{
    public class AutocompleteDish : IAutocompleteDish
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string RestaurantName { get; set; }
        public string RestaurantUrl { get; set; }
        public string RestaurantAddress { get; set; }
        public string RestaurantCity { get; set; }
        public string SearchTerms { get; set; }
        public string RestaurantDisplayAddress { get; set; }
        public double Distance { get; set; }
        public int Rating { get; set; }
        public float RatingFloat { get; set; }

        [NotMapped]
        private LocalizableString _name
        {
            get
            {
                if (_nameCached != null) return _nameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Name);
                _nameCached = data;
                return data;
            }
            set
            {
                _nameCached = value;
                Name = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _nameCached;
        [NotMapped]
        ILocalizableString IAutocompleteDish.Name
        {
            get => _name; set => _name = value as LocalizableString;
        }

        [NotMapped]
        private LocalizableString _restaurantDisplayAddress
        {
            get
            {
                if (_restaurantDisplayAddressCached != null) return _restaurantDisplayAddressCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(RestaurantDisplayAddress);
                _restaurantDisplayAddressCached = data;
                return data;
            }
            set
            {
                _restaurantDisplayAddressCached = value;
                RestaurantDisplayAddress = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _restaurantDisplayAddressCached;
        [NotMapped]
        ILocalizableString IAutocompleteDish.RestaurantDisplayAddress
        {
            get => _restaurantDisplayAddress; set => _restaurantDisplayAddress = value as LocalizableString;
        }
    }
}

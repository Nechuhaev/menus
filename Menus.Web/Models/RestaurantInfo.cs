// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace Menus.Web.Models
{
    public class RestaurantInfo : IRestaurantInfo
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string Photos { get; set; }
        public string Logo { get; set; }
        public string Website { get; set; }
        public string OpenHours { get; set; }
        public string Contacts { get; set; }
        public string GpsLatitude { get; set; }
        public string GpsLongitude { get; set; }

        public bool UserAllowedAddMenu { get; set; }
        public bool UserAllowedOpenMenu { get; set; }
        public bool UserAllowedAddDishes { get; set; }
        public bool IsFeatured { get; set; }
        public bool ShowOnIndex { get; set; }

        public string Currency { get; set; }


        public string ShortInfo { get; set; }
        public string FullInfo { get; set; }
        public string Cuisine { get; set; }
        public string DisplayAddress { get; set; }



        [NotMapped]
        private LocalizableString _shortInfo
        {
            get
            {
                if (_shortInfoCached != null) return _shortInfoCached;
                var data = LocalizableString.FromJson(ShortInfo);
                _shortInfoCached = data;
                return data;
            }
            set
            {
                _shortInfoCached = value;
                ShortInfo = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _shortInfoCached = null;
        [NotMapped]
        ILocalizableString IRestaurantInfo.ShortInfo { get => _shortInfo; set => _shortInfo = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _fullInfo
        {
            get
            {
                if (_fullInfoCached != null) return _fullInfoCached;
                var data = LocalizableString.FromJson(FullInfo);
                _fullInfoCached = data;
                return data;
            }
            set
            {
                _fullInfoCached = value;
                FullInfo = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _fullInfoCached = null;
        [NotMapped]
        ILocalizableString IRestaurantInfo.FullInfo { get => _fullInfo; set => _fullInfo = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _cuisine
        {
            get
            {
                if (_cuisineCached != null) return _cuisineCached;
                var data = LocalizableString.FromJson(Cuisine);
                _cuisineCached = data;
                return data;
            }
            set
            {
                _cuisineCached = value;
                Cuisine = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _cuisineCached = null;
        [NotMapped]
        ILocalizableString IRestaurantInfo.Cuisine { get => _cuisine; set => _cuisine = value as LocalizableString;
        }

        [NotMapped]
        private LocalizableString _displayAddress
        {
            get
            {
                if (_displayAddressCached != null) return _displayAddressCached;
                var data = LocalizableString.FromJson(DisplayAddress);
                _displayAddressCached = data;
                return data;
            }
            set
            {
                _displayAddressCached = value;
                DisplayAddress = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _displayAddressCached;
        [NotMapped]
        ILocalizableString IRestaurantInfo.DisplayAddress
        {
            get => _displayAddress; set => _displayAddress = value as LocalizableString;
        }

        public object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<RestaurantInfo>(serialized);
        }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class CompleteRestaurant : ICompleteRestaurant
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public RestaurantStatus Status { get; set; }
        public string Currency { get; set; }
        public string ShortInfo { get; set; }
        public string FullInfo { get; set; }
        public string Cuisine { get; set; }
        public string DisplayAddress { get; set; }
        public string Photos { get; set; }
        public string Website { get; set; }
        public string OpenHours { get; set; }
        public string Contacts { get; set; }
        public decimal? GpsLatitude { get; set; }
        public decimal? GpsLongitude { get; set; }
        public double? Distance { get; set; }
        public int RatingsCount { get; set; }
        public bool UserAllowedAddMenu { get; set; }
        public bool UserAllowedOpenMenu { get; set; }
        public bool UserAllowedAddDishes { get; set; }
        public bool IsFeatured { get; set; }
        public bool ShowOnIndex { get; set; }
        public string Url { get; set; }
        public DateTime UpdateDate { get; set; }
        public DateTime CreateDate { get; set; }

        [NotMapped]
        private LocalizableString _shortInfo
        {
            get
            {
                if (_shortInfoCachedCached != null) return _shortInfoCachedCached;
                var data = LocalizableString.FromJson(ShortInfo);
                _shortInfoCachedCached = data;
                return data;
            }
            set
            {
                _shortInfoCachedCached = value;
                ShortInfo = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _shortInfoCachedCached = null;
        [NotMapped]
        ILocalizableString ICompleteRestaurant.ShortInfo
        {
            get => _shortInfo; set => _shortInfo = value as LocalizableString;
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
        ILocalizableString ICompleteRestaurant.FullInfo
        {
            get => _fullInfo; set => _fullInfo = value as LocalizableString;
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
        ILocalizableString ICompleteRestaurant.DisplayAddress
        {
            get => _displayAddress; set => _displayAddress = value as LocalizableString;
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
        ILocalizableString ICompleteRestaurant.Cuisine
        {
            get => _cuisine; set => _cuisine = value as LocalizableString;
        }
    }
}

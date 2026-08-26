// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class CompleteDish : ICompleteDish
    {
        public string Id { get; set; }
        public string PhotoLink { get; set; }
        public decimal Price { get; set; }
        public string Weight { get; set; }
        public string WeightType { get; set; }
        public bool IsPriceMultiple { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public string ShortDescription { get; set; }
        public string RestaurantId { get; set; }
        public string RestaurantName { get; set; }
        public string RestaurantCity { get; set; }
        public string RestaurantAddress { get; set; }
        public string RestaurantDisplayAddress { get; set; }
        public string RestaurantCurrency { get; set; }
        public decimal? RestaurantGpsLatitude { get; set; }
        public decimal? RestaurantGpsLongitude { get; set; }
        public double? RestaurantDistance { get; set; }
        public int RatingsCount { get; set; }
        public int Rating { get; set; }
        public float RatingFloat { get; set; }
        public string DishUrl { get; set; }
        public string RestaurantUrl { get; set; }
        public string SearchTerms { get; set; }

        [NotMapped]
        private IEnumerable<string> _photos;
        [NotMapped]
        public IEnumerable<string> Photos
        {
            get
            {
                if (_photos != null) return _photos;
                try
                {
                    _photos = (string.IsNullOrEmpty(PhotoLink)
                        ? new List<string>()
                        : JsonConvert.DeserializeObject<IEnumerable<string>>(PhotoLink));
                }
                catch (JsonReaderException)
                {
                    _photos = new List<string> { PhotoLink };
                }

                return _photos;
            }
            set
            {
                _photos = value;
                PhotoLink = JsonConvert.SerializeObject(value);
            }
        }

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
        ILocalizableString ICompleteDish.Name
        {
            get => _name; set => _name = value as LocalizableString;
        }

        [NotMapped]
        private LocalizableString _categoryName
        {
            get
            {
                if (_categoryNameCached != null) return _categoryNameCached;
                if (!string.IsNullOrEmpty(CategoryName))
                {
                    var data = JsonConvert.DeserializeObject<LocalizableString>(CategoryName);
                    _categoryNameCached = data;
                    return data;
                } else
                {
                    return new LocalizableString();
                }              
            }
            set
            {
                _categoryNameCached = value;
                Name = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _categoryNameCached;
        [NotMapped]
        ILocalizableString ICompleteDish.CategoryName
        {
            get => _categoryName; set => _categoryName = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _shortDescription
        {
            get
            {
                if (_shortDescriptionCached != null) return _shortDescriptionCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(ShortDescription);
                _shortDescriptionCached = data;
                return data;
            }
            set
            {
                _shortDescriptionCached = value;
                ShortDescription = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _shortDescriptionCached;
        [NotMapped]
        ILocalizableString ICompleteDish.ShortDescription
        {
            get => _shortDescription; set => _shortDescription = value as LocalizableString;
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
        ILocalizableString ICompleteDish.RestaurantDisplayAddress
        {
            get => _restaurantDisplayAddress; set => _restaurantDisplayAddress = value as LocalizableString;
        }
    }
}

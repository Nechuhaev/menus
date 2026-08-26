// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class Dish : IDish
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string PhotoLink { get; set; }
        public string Name { get; set; }
        public string ShortDescription { get; set; }
        public string Ingredients { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public string Weight { get; set; }
        public string WeightType { get; set; }
        public string Tags { get; set; }
        public bool IsPriceMultiple { get; set; }
        public DishStatus Status { get; set; }

        [NotMapped]
        public int Rating
        {
            get => Convert.ToInt32(Math.Ceiling(RatingFloat));
        }

        public int RatingsCount { get; set; }
        public float RatingFloat { get; set; }
        public DateTime CreateDate { get; set; }
        public bool ShowOnIndex { get; set; }


        [NotMapped]
        IEnumerable<string> IDishBase.Tags => string.IsNullOrEmpty(Tags) ? new string[0] : JsonConvert.DeserializeObject<List<DishTagValue>>(Tags).Select(x => x.Value);


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
        ILocalizableString IDishBase.Name { get => _name; set => _name = value as LocalizableString;
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
        ILocalizableString IDishBase.ShortDescription { get => _shortDescription; set => _shortDescription = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _ingredients
        {
            get
            {
                if (_ingredientsCached != null) return _ingredientsCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Ingredients);
                _ingredientsCached = data;
                return data;
            }
            set
            {
                _ingredientsCached = value;
                Ingredients = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _ingredientsCached;
        [NotMapped]
        ILocalizableString IDishBase.Ingredients { get => _ingredients; set => _ingredients = value as LocalizableString;
        }


        [NotMapped]
        private LocalizableString _description
        {
            get
            {
                if (_descriptionCached != null) return _descriptionCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Description);
                _descriptionCached = data;
                return data;
            }
            set
            {
                _descriptionCached = value;
                Description = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _descriptionCached;
        [NotMapped]
        ILocalizableString IDishBase.Description { get => _description; set => _description = value as LocalizableString;
        }

        public object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<Dish>(serialized);
        }
    }

    public class DishTagValue
    {
        public string Value { get; set; }
    }
}

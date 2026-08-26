// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class UserRating : IUserRating
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string DishId { get; set; }
        public string UserId { get; set; }
        public int Rate { get; set; }
        public string Text { get; set; }
        public DateTime UpdateDate { get; set; }
        public string UserFirstName { get; set; }
        public string UserLastName { get; set; }
        public string DishName { get; set; }
        public string RestaurantName { get; set; }
        public string RestaurantAddress { get; set; }
        public string DishImageUrl { get; set; }
        public RatingStatus Status { get; set; }
        [NotMapped]
        private LocalizableString _name
        {
            get
            {
                if (_nameCached != null) return _nameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(DishName);
                _nameCached = data;
                return data;
            }
            set
            {
                _nameCached = value;
                DishName = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _nameCached;
        [NotMapped]
        ILocalizableString IUserRating.DishName { get => _name; set => _name = value as LocalizableString; }
    }
}

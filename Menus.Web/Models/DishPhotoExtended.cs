// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Models
{
    public class DishPhotoExtended: IDishPhotoExtended
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string DishId { get; set; }
        public string ImageUrl { get; set; }
        public string RestaurantName { get; set; }
        public string UserFirstName { get; set; }
        public string UserLastName { get; set; }
        public int Index { get; set; }
        public DateTime Date { get; set; }
        public DishPhotoStatus Status { get; set; }
        public string DishName { get; set; }

        [NotMapped]
        private LocalizableString _dishname
        {
            get
            {
                if (_dishNameCached != null) return _dishNameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(DishName);
                _dishNameCached = data;
                return data;
            }
            set
            {
                _dishNameCached = value;
                DishName = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _dishNameCached;
        [NotMapped]
        ILocalizableString IDishPhotoExtended.DishName
        {
            get => _dishname; set => _dishname = value as LocalizableString;
        }
    }
}

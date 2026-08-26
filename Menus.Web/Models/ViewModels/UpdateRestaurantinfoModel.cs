// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;

namespace Menus.Web.Models.ViewModels
{
    public class UpdateRestaurantinfoModel : RestaurantInfo
    {
        public new List<LocalizableStringValue> ShortInfo { get; set; }
        public new List<LocalizableStringValue> FullInfo { get; set; }
        public new List<LocalizableStringValue> Cuisine { get; set; }
        public new List<LocalizableStringValue> DisplayAddress { get; set; }

        public RestaurantInfo GetRestaurantInfo()
        {
            var result = new RestaurantInfo();
            var properties = result.GetType().GetProperties();
            foreach (var prop in properties)
            {
                prop.SetValue(result, prop.GetValue(this));
            }

            return result;
        }
    }
}

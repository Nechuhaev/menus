// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Linq;

namespace Menus.Web.Models.ViewModels
{
    public class CreateDishModel : Dish
    {
        public IFormFile PhotoFile { get; set; }
        public string Categories { get; set; }
        public string Prices { get; set; }
        public new List<LocalizableStringValue> Name { get; set; }
        public new List<LocalizableStringValue> ShortDescription { get; set; }
        public new List<LocalizableStringValue> Ingredients { get; set; }
        public new List<LocalizableStringValue> Description { get; set; }

        public Dish GetDish()
        {
            var result = new Dish();
            var properties = result.GetType().GetProperties();
            foreach (var prop in properties.Where(x => x.CanWrite))
            {
                prop.SetValue(result, prop.GetValue(this));
            }

            return result;
        }
    }
}

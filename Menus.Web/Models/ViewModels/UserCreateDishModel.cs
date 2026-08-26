using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Menus.Web.Models.ViewModels
{
    public class UserCreateDishViewModel
    {
        public string RestaurantId { get; set; }
        public string Name { get; set; }
        public string Price { get; set; }
        public string Locale { get; set; }
        public string Rating { get; set; }
        public string Photo { get; set; }
        public string Unit { get; set; }
        public int Weight { get; set; }
    }
}

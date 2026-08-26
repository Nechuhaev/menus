// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using System.Collections.Generic;

namespace Menus.Web.Models.ViewModels
{
    public class RestaurantViewModel
    {
        public IRestaurant Restaurant { get; set; }
        public IRestaurantInfo RestaurantInfo { get; set; }
        public IRestaurantRole UserRole { get; set; }  
        public IEnumerable<IMenuPhoto> MenuPhotos { get; set; }
        public DishesListViewModel Menu { get; set; }
        public List<string> Photos { get; set; }
        public string OpenHours { get; set; }
        public bool SchedulePerDay { get; set; }
    }
}

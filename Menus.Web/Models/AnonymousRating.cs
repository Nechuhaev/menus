using System;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class AnonymousRating : IAnonymousRating
    {
        public string Id { get; set; }        
        public string RestaurantId { get; set; }
        public string DishId { get; set; }
        public int Rate { get; set; }
        public string UserIp { get; set; }
        public string DeviceId { get; set; }
        public DateTime UpdateDate { get; set; }
    }
}

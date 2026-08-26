using System;

namespace Menus.Abstract.Models
{
    public interface IAnonymousRating
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string DishId { get; set; }
        int Rate { get; set; }
        string UserIp { get; set; }
        string DeviceId { get; set; }
        DateTime UpdateDate { get; set; }
    }
}

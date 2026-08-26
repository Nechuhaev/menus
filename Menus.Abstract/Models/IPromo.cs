using System;

namespace Menus.Abstract.Models
{
    public interface IPromo
    {
        string Id  { get; set; }
        string RestaurantId { get; set; }
        DateTime StartDate { get; set; }
        DateTime EndDate { get; set; }
        ILocalizableString Image { get; set; }
        string Name { get; set; }
        string Action { get; set; }
    }
}
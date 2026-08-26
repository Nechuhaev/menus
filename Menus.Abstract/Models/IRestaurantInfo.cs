// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface IRestaurantInfo : ICloneable
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string Currency { get; set; }
        ILocalizableString ShortInfo { get; set; }
        ILocalizableString FullInfo { get; set; }
        ILocalizableString Cuisine { get; set; }
        ILocalizableString DisplayAddress { get; set; }
        string Photos { get; set; }
        string Logo { get; set; }
        string Website { get; set; }
        string OpenHours { get; set; }
        string Contacts { get; set; }
        string GpsLatitude { get; set; }
        string GpsLongitude { get; set; }

        bool UserAllowedAddMenu { get; set; }
        bool UserAllowedOpenMenu { get; set; }
        bool UserAllowedAddDishes { get; set; }
        bool IsFeatured { get; set; }
        bool ShowOnIndex { get; set; }
    }
}

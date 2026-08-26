// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface ICompleteRestaurant
    {
        string Id { get; set; }
        string Name { get; set; }
        string Email { get; set; }
        string Phone { get; set; }
        string Address { get; set; }
        string City { get; set; }
        RestaurantStatus Status { get; set; }
        string Currency { get; set; }
        ILocalizableString ShortInfo { get; set; }
        ILocalizableString FullInfo { get; set; }
        ILocalizableString Cuisine { get; set; }
        ILocalizableString DisplayAddress { get; set; }
        string Photos { get; set; }
        string Website { get; set; }
        string OpenHours { get; set; }
        string Contacts { get; set; }
        decimal? GpsLatitude { get; set; }
        decimal? GpsLongitude { get; set; }
        double? Distance { get; set; }
        int RatingsCount { get; set; }
        bool UserAllowedAddMenu { get; set; }
        bool UserAllowedOpenMenu { get; set; }
        bool UserAllowedAddDishes { get; set; }
        bool IsFeatured { get; set; }
        bool ShowOnIndex { get; set; }
        string Url { get; set; }
        DateTime UpdateDate { get; set; }
        DateTime CreateDate { get; set; }
    }
}

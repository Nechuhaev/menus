// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface ISearchDish
    {
        string Id { get; set; }
        IEnumerable<string> Photos { get; set; }
        decimal Price { get; set; }
        string Weight { get; set; }
        string WeightType { get; set; }
        bool IsPriceMultiple { get; set; }
        ILocalizableString Name { get; set; }
        ILocalizableString ShortDescription { get; set; }

        string RestaurantName { get; set; }
        string RestaurantCity { get; set; }
        string RestaurantAddress { get; set; }
        ILocalizableString RestaurantDisplayAddress { get; set; }
        string RestaurantCurrency { get; set; }
        string RestaurantGpsLatitude { get; set; }
        string RestaurantGpsLongitude { get; set; }
        double? RestaurantDistance { get; set; }

        int RatingsCount { get; set; }
        int Rating { get; set; }
        float RatingFloat { get; set; }

        string DishUrl { get; set; }
        string RestaurantUrl { get; set; }
    }
}

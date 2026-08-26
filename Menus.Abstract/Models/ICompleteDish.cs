using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface ICompleteDish
    {
        string Id { get; set; }
        IEnumerable<string> Photos { get; set; }
        decimal Price { get; set; }
        string Weight { get; set; }
        string WeightType { get; set; }
        bool IsPriceMultiple { get; set; }
        ILocalizableString Name { get; set; }
        ILocalizableString ShortDescription { get; set; }

        string RestaurantId { get; set; }
        string RestaurantName { get; set; }
        string RestaurantCity { get; set; }
        string RestaurantAddress { get; set; }
        ILocalizableString RestaurantDisplayAddress { get; set; }
        string RestaurantCurrency { get; set; }
        decimal? RestaurantGpsLatitude { get; set; }
        decimal? RestaurantGpsLongitude { get; set; }
        double? RestaurantDistance { get; set; }

        int RatingsCount { get; set; }
        int Rating { get; set; }
        float RatingFloat { get; set; }

        string DishUrl { get; set; }
        string RestaurantUrl { get; set; }
        
        string SearchTerms { get; set; }
        ILocalizableString CategoryName { get; set; }
    }
}

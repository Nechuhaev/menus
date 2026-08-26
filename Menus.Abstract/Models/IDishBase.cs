using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IDishBase
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        IEnumerable<string> Photos { get; set; }
        decimal Price { get; set; }
        string Weight { get; set; }
        string WeightType { get; set; }
        bool IsPriceMultiple { get; set; }
        ILocalizableString Name { get; set; }
        ILocalizableString ShortDescription { get; set; }
        ILocalizableString Ingredients { get; set; }
        ILocalizableString Description { get; set; }
        int Rating { get; }
        float RatingFloat { get; set; }
        bool ShowOnIndex { get; set; }
        IEnumerable<string> Tags { get; }
    }
}

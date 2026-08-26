// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com


namespace Menus.Abstract.Models
{
    public interface IAutocompleteDish
    {
        string Id { get; set; }
        ILocalizableString Name { get; set; }
        string Url { get; set; }
        string RestaurantName { get; set; }
        string RestaurantUrl { get; set; }
        string RestaurantAddress { get; set; }
        ILocalizableString RestaurantDisplayAddress { get; set; }
        string RestaurantCity { get; set; }
        string SearchTerms { get; set; }
        double Distance { get; set; }
        int Rating { get; set; }
        float RatingFloat { get; set; }
    }
}

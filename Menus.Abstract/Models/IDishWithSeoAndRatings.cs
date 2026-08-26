// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

namespace Menus.Abstract.Models
{
    public interface IDishWithSeoAndRatings : IDishBase
    {
        string Url { get; set; }
        string Title { get; set; }
        string SeoDescription { get; set; }
        string Keywords { get; set; }
        string SeoText { get; set; }
        int RatingCount { get; set; }
        int UserRating { get; set; }
    }
}

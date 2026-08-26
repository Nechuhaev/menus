// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface IDishRatingWithUser
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string DishId { get; set; }
        string UserId { get; set; }
        int Rate { get; set; }
        string Text { get; set; }
        DateTime UpdateDate { get; set; }
        string UserFirstName { get; set; }
        string UserLastName { get; set; }
        string UserPhoto { get; set; }
        int UserRatingsCount { get; set; }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface IUserRating
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string DishId { get; set; }
        string UserId { get; set; }
        int Rate { get; set; }
        string Text { get; set; }
        DateTime UpdateDate { get; set; }
        ILocalizableString DishName { get; set; }
        string RestaurantName { get; set; }
        string RestaurantAddress { get; set; }
        string UserFirstName { get; set; }
        string UserLastName { get; set; }
        string DishImageUrl { get; set; }
        RatingStatus Status { get; set; }
    }
}

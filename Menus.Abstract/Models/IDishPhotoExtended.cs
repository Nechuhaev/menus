// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface IDishPhotoExtended
    {
        string Id { get; set; }
        string UserId { get; set; }
        string DishId { get; set; }
        string ImageUrl { get; set; }
        string RestaurantName { get; set; }
        string UserFirstName { get; set; }
        string UserLastName { get; set; }
        int Index { get; set; }
        DateTime Date { get; set; }
        DishPhotoStatus Status { get; set; }
        ILocalizableString DishName { get; set; }
    }
}

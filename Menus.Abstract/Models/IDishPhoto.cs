// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface IDishPhoto
    {
        string Id { get; set; }
        string UserId { get; set; }
        string DishId { get; set; }
        string ImageUrl { get; set; }
        int Index { get; set; }
        DishPhotoStatus Status { get; set; }
        DateTime Timestamp { get; set; }
    }

    public enum DishPhotoStatus
    {
        New = 0,
        Approved = 1,
        Blocked = 2
    }
}

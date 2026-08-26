// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface IRating
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string DishId { get; set; }
        string UserId { get; set; }
        string ParentId { get; set; }
        IRating Parent { get; set; }
        int Rate { get; set; }
        string Text { get; set; }
        DateTime UpdateDate { get; set; }
        RatingStatus Status { get; set; }
        IEnumerable<IRating> ChildRatings { get; set; }
    }

    public enum RatingStatus
    {
        NotApproved = 0,
        Approved = 1,
        Declined = 2
    }

}

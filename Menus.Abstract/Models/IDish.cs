// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IDish : ICloneable, IDishBase
    {
        DishStatus Status { get; set; }
        int RatingsCount { get; set; }
        DateTime CreateDate { get; set;  }
    }

    public enum DishStatus
    {
        Active = 0,
        Pending = 1,
        Blocked = 2
    }
}

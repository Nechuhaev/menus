// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IMenuPhoto
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string UserId { get; set; }
        int Index { get; set; }
        DateTime Timestamp { get; set; }
        IEnumerable<string> Photos { get; set; }
    }
}

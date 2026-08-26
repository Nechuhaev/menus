// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface IDishCategory : ICloneable
    {
        string Id { get; set; }
        string MenuCategoryId { get; set; }
        string DishId { get; set; }
        int Index { get; set; }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;

namespace Menus.Abstract.Models
{
    public interface IDishVariation : ICloneable
    {
        string Id { get; set; }
        string DishId { get; set; }
        decimal Price { get; set; }
        int Weight { get; set; }
        string Type { get; set; }
    }
}

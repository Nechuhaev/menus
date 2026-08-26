// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

//TODO: add type

using System;
using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IMenuCategory : ICloneable
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string CategoryType { get; set; }
        int Index { get; set; }
        ILocalizableString Name { get; set; }
        ILocalizableString Description { get; set; }
        bool Active { get; set; }
        bool IsTopLevelCategory { get; set; }
        IEnumerable<IMenuCategory> ChildCategories { get; }
        IEnumerable<IMenuCategory> ParentCategories { get; }
        string CurrentParentCategoryId { get; set; }
    }
}

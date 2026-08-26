// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using System.Collections.Generic;

namespace Menus.Web.Models.ViewModels
{
    public class DishesListModel
    {
        public List<DishesListModelCategory> Categories { get; set; }
    }

    public class DishesListModelCategory : MenuCategory
    {
        public List<IDish> Dishes { get; set; }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using System.Collections.Generic;

namespace Menus.Web.Models.ViewModels
{
    public class DishesListViewModel
    {
        public List<DishesListViewModelCategory> Categories { get; set; }
    }

    public class DishesListViewModelCategory : MenuCategory
    {
        public List<IDishWithSeoAndRatings> Dishes { get; set; }
    }
}

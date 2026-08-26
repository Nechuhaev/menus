// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class DishRating : IDishRating
    {
        public int GeneralRating { get; set; }
        public int GeneralRatingCount { get; set; }
        public int UserRating { get; set; }
    }
}

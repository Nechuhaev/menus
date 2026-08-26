// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class DishRatingWithUser : IDishRatingWithUser
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string DishId { get; set; }
        public string UserId { get; set; }
        public int Rate { get; set; }
        public string Text { get; set; }
        public DateTime UpdateDate { get; set; }
        public string UserFirstName { get; set; }
        public string UserLastName { get; set; }
        public string UserPhoto { get; set; }
        public int UserRatingsCount { get; set; }
    }
}

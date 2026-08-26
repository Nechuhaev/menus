// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class RestaurantRole : IRestaurantRole
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string UserId { get; set; }
        public RestaurantUserRole Role { get; set; }
    }
}

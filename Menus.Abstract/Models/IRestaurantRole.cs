// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

namespace Menus.Abstract.Models
{
    public interface IRestaurantRole
    {
        string Id { get; set; }
        string RestaurantId { get; set; }
        string UserId { get; set; }
        RestaurantUserRole Role { get; set; }
    }

    public enum RestaurantUserRole
    {
        Manager = 0,
        Owner = 1,
        Creator = 2
    }
}

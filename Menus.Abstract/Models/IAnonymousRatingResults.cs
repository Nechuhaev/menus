using System;

namespace Menus.Abstract.Models
{
    public interface IAnonymousRatingResults
    {
        int AlreadyVoted { get; set; }
        int VotesForDishFromOneIp { get; set; }
        int VotesForRestaurantFromOneIp { get; set; }
        int VotesForRestaurantFromOneDevice { get; set; }
    }
}

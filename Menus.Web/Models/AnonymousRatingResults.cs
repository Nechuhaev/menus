using System;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class AnonymousRatingResults : IAnonymousRatingResults
    {
        public int AlreadyVoted { get; set; }
        public int VotesForDishFromOneIp { get; set; }
        public int VotesForRestaurantFromOneIp { get; set; }
        public int VotesForRestaurantFromOneDevice { get; set; }
    }
}

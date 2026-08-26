// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Menus.Web.Models
{
    public class Rating : IRating
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string DishId { get; set; }
        public string UserId { get; set; }
        public string ParentId { get; set; }
        public Rating Parent { get; set; }
        public int Rate { get; set; }
        public string Text { get; set; }
        public DateTime UpdateDate { get; set; }
        public RatingStatus Status { get; set; }
        public ICollection<Rating> ChildRatings { get; set; }
        IEnumerable<IRating> IRating.ChildRatings { get => ChildRatings; set => ChildRatings = value.Cast<Rating>().ToList(); }
        IRating IRating.Parent { get => Parent; set => Parent = value as Rating; }
    }
}

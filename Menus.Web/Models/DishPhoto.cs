// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class DishPhoto : IDishPhoto
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string DishId { get; set; }
        public string ImageUrl { get; set; }
        public int Index { get; set; }
        public DishPhotoStatus Status { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

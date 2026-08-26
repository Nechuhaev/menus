// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class DishCategory : IDishCategory
    {
        public string Id { get; set; }
        public string MenuCategoryId { get; set; }
        public string DishId { get; set; }
        public int Index { get; set; }

        public object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<DishCategory>(serialized);
        }
    }
}

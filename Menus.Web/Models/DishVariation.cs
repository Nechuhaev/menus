// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class DishVariation : IDishVariation
    {
        public string Id { get; set; }
        public string DishId { get; set; }
        public decimal Price { get; set; }
        public int Weight { get; set; }
        public string Type { get; set; }

        public object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<DishVariation>(serialized);
        }
    }
}

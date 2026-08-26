// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class RestaurantSeo : Seo, IRestaurantSeo
    {
        public override object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<RestaurantSeo>(serialized);
        }

        public string Tags { get; set; }

        [NotMapped]
        IEnumerable<string> IRestaurantSeo.Tags => string.IsNullOrEmpty(Tags) ? new string[0] : JsonConvert.DeserializeObject<List<DishTagValue>>(Tags).Select(x => x.Value);

        public string CustomCss { get; set; }
        public string CustomIframeSettings { get; set; }
        public string CustomScript { get; set; }
        public string CustomLanguages { get; set; }
    }
}

using Menus.Web.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Menus.Web.Services
{
    public class DishTagsService
    {
        private static List<DishTag> _tags;

        public DishTagsService()
        {
            if (_tags == null)
            {
                var config = File.ReadAllText("Data/Static/DishTags.json");
                _tags = JsonConvert.DeserializeObject<List<DishTag>>(config);
            }
        }

        public IEnumerable<DishTag> Tags => _tags;
        public string Autocomplete => JsonConvert.SerializeObject(Tags.Select(x => x.Tag));
    }
}

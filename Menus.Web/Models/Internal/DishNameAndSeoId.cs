using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models.Internal
{
    public class DishNameAndSeoId
    {
        public string NameString { get; set; }
        public string SeoId { get; set; }


        private LocalizableString _name
        {
            get
            {
                if (_nameCached != null) return _nameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(NameString);
                _nameCached = data;
                return data;
            }
            set
            {
                _nameCached = value;
                NameString = JsonConvert.SerializeObject(value);
            }
        }
        private LocalizableString _nameCached;
        public ILocalizableString Name
        {
            get => _name; set => _name = value as LocalizableString;
        }
    }
}

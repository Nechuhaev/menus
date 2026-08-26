// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.ComponentModel.DataAnnotations.Schema;
using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models
{
    public class MenuCategoryType : IMenuCategoryType
    {
        public string Id { get; set; }
        public string Name { get; set; }

        [NotMapped]
        private LocalizableString _name
        {
            get
            {
                if (_nameCached != null) return _nameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Name);
                _nameCached = data;
                return data;
            }
            set
            {
                _nameCached = value;
                Name = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _nameCached;
        [NotMapped]
        ILocalizableString IMenuCategoryType.Name
        {
            get => _name; set => _name = value as LocalizableString;
        }
    }
}

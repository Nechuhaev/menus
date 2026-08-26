// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;
using Menus.Abstract.Models;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Menus.Web.Models
{
    public class MenuCategory : IMenuCategory
    {
        public string Id { get; set; }
        public string RestaurantId { get; set; }
        public string CategoryType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Index { get; set; }
        public bool Active { get; set; }
        public bool IsTopLevelCategory { get; set; }

        [NotMapped]
        public IEnumerable<MenuCategory> ChildCategories => ChildCategoriesRelations.Select(x => x.Child);
        [NotMapped]
        public IEnumerable<MenuCategory> ParentCategories => ParentCategoriesRelations.Select(x => x.Parent);

        public ICollection<MenuCategoryRelation> ChildCategoriesRelations { get; set; } = new List<MenuCategoryRelation>();
        public ICollection<MenuCategoryRelation> ParentCategoriesRelations { get; set; } = new List<MenuCategoryRelation>();

        [NotMapped]
        private LocalizableString _name {
            get {
                if (_nameCached != null) return _nameCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Name);
                _nameCached = data;
                return data;
            }
            set {
                _nameCached = value;
                Name = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _nameCached = null;
        [NotMapped]
        ILocalizableString IMenuCategory.Name { get => _name; set => _name = value as LocalizableString;
        }

        [NotMapped]
        private LocalizableString _description
        {
            get
            {
                if (_descriptionCached != null) return _descriptionCached;
                var data = JsonConvert.DeserializeObject<LocalizableString>(Description);
                _descriptionCached = data;
                return data;
            }
            set
            {
                _descriptionCached = value;
                Description = JsonConvert.SerializeObject(value);
            }
        }
        [NotMapped]
        private LocalizableString _descriptionCached = null;
        [NotMapped]
        ILocalizableString IMenuCategory.Description
        {
            get => _description; set => _description = value as LocalizableString;
        }

        [NotMapped]
        IEnumerable<IMenuCategory> IMenuCategory.ChildCategories => ChildCategories;
        IEnumerable<IMenuCategory> IMenuCategory.ParentCategories => ParentCategories;

        [NotMapped]
        public string CurrentParentCategoryId { get; set; }

        public object Clone()
        {
            var serialized = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<MenuCategory>(serialized);
        }
    }
}

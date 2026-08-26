using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class MenuCategoryRelation : IMenuCategoryRelation
    {
        public string ParentId { get; set; }
        public MenuCategory Parent { get; set; }
        public string ChildId { get; set; }
        public MenuCategory Child { get; set; }
    }
}

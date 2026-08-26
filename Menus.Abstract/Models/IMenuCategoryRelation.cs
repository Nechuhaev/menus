namespace Menus.Abstract.Models
{
    public interface IMenuCategoryRelation
    {
        string ParentId { get; set; }
        string ChildId { get; set; }
    }
}

namespace Menus.Abstract.Models
{
    public interface IAbTestParameter
    {
        string Id { get; set; }
        string TestId { get; set; }
        string Name { get; set; }
        int TimesFired { get; set; }

        IAbTest Test { get; }
    }
}
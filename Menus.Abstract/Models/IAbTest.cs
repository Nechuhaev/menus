using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IAbTest
    {
        string Id { get; set; }
        string Name { get; set; }
        bool Active { get; set; }

        IEnumerable<IAbTestParameter> Parameters { get; }
    }
}
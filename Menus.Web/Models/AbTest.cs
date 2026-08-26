using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Menus.Abstract.Models;

namespace Menus.Web.Models
{
    public class AbTest : IAbTest
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }

        public ICollection<AbTestParameter> Parameters { get; set; }
        //public IAbTestParameter FireParameter()
        //{
        //    var smallestParameterFired = Parameters.Min(x => x.TimesFired);
        //    var smallestParameter = Parameters.FirstOrDefault(x => x.TimesFired == smallestParameterFired);

        //    if (smallestParameter == null) return null;

        //    smallestParameter.TimesFired++;
        //    return smallestParameter;
        //}

        [NotMapped]
        IEnumerable<IAbTestParameter> IAbTest.Parameters => Parameters;
    }
}
using Menus.Abstract.Models;
using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace Menus.Web.Models
{
    public class AbTestParameter : IAbTestParameter
    {
        public string Id { get; set; }
        public string TestId { get; set; }
        public string Name { get; set; }
        public int TimesFired { get; set; }

        [JsonIgnore]
        [IgnoreDataMember]
        public AbTest Test { get; set; }

        IAbTest IAbTestParameter.Test => Test;
    }
}
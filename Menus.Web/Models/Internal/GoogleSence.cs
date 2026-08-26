using Menus.Abstract.Models;
using Newtonsoft.Json;

namespace Menus.Web.Models.Internal
{
    public enum GoogleSenceTypes {
        Wide = 0,
        SqueareAdaptive = 1,
        UltraWide = 2,
        Vertical = 3
    }

    public class GoogleSence
    {
        public GoogleSenceTypes GoogleSenceType { get; set; }
        public int CurrentRecords { get; set; }
    }
}

using System;

namespace Menus.Abstract.Models
{
    public interface ISeo : ICloneable
    {
        string Id { get; set; }
        string ItemId { get; set; }
        string Url { get; set; }
        ILocalizableString Header { get; set; }
        ILocalizableString Title { get; set; }
        ILocalizableString Description { get; set; }
        string Keywords { get; set; }
        string SeoText { get; set; }
        string SearchTerms { get; set; }
    }
}

// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;

namespace Menus.Abstract.Models
{
    public interface IRestaurantSeo : ISeo
    {
        IEnumerable<string> Tags { get; }
        string CustomCss { get; set; }
        string CustomIframeSettings { get; set; }
        string CustomScript { get; set; }
        string CustomLanguages { get; set; }
    }
}

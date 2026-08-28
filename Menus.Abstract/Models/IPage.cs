// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Text;

namespace Menus.Abstract.Models
{
    public interface IPage
    {
        string Id { get; set; }
        string Photo { get; set; }
        ILocalizableString Title { get; set; }
        ILocalizableString ShortContent { get; set; }
        ILocalizableString Content { get; set; }
        PageCategory Category { get; set; }
        PageStatus Status { get; set; }
        DateTime Timestamp { get; set; }

        string Url { get; set; }
        ILocalizableString SeoTitle { get; set; }
        ILocalizableString SeoDescription { get; set; }
        ILocalizableString SeoKeywords { get; set; }
        ILocalizableString SeoText { get; set; }
        int Views { get; set; }
    }

    public enum PageCategory
    {
        Content,
        News
    }

    public enum PageStatus
    {
        Hidden,
        Published
    }
}

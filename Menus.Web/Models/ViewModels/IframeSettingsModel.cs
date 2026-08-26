using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Menus.Abstract.Models;

namespace Menus.Web.Models.ViewModels
{
    public class IframeSettingsModel
    {
        public bool ShowHeader { get; set; }
        public bool ShowInfo { get; set; }
        public bool ShowBreadcrump { get; set; }
        public bool ShowTop { get; set; }
        public bool ShowRating { get; set; }
        public bool ShowError { get; set; }
        public bool ShowSocial { get; set; }
        public bool ShowFavourites { get; set; }
        public bool ShowSimilar { get; set; }
        public bool ShowFooter { get; set; }
        public bool ShowCategories { get; set; }
        public bool ShowFooterDescription { get; set; }
        public bool ShowLogo { get; set; }
        public bool ShowLanguages { get; set; }
        public bool ShowPoweredBy { get; set; }
        public bool ShowExternalUrl { get; set; }
        public bool ShowDishImages { get; set; }
        public bool ShowDishImagesPreview { get; set; }
        public bool ShowDishImagesIcons { get; set; }
        public bool ShowTheardLevelCategories { get; set; }
        public bool ShowTags { get; set; }
        public bool ShowOnlyCategories { get; set; }
        public bool ShowPhone { get; set; }
        public bool ShowFirstCategoryOnStart { get; set; }
        public bool ShowOnlyIframeLogo { get; set; }
        public IEnumerable<ILocale>  Languages { get; set; }

    }
}

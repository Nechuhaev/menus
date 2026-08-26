// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System.Collections.Generic;

namespace Menus.Web.Models.ViewModels
{
    public class CreatePageModel : Page
    {
        public new List<LocalizableStringValue> Title { get; set; }
        public new List<LocalizableStringValue> ShortContent { get; set; }
        public new List<LocalizableStringValue> Content { get; set; }
        public new List<LocalizableStringValue> SeoTitle { get; set; }
        public new List<LocalizableStringValue> SeoDescription { get; set; }
        public new List<LocalizableStringValue> SeoKeywords { get; set; }
        public new List<LocalizableStringValue> SeoText { get; set; }

        public Page GetPage()
        {
            var result = new Page();
            var properties = result.GetType().GetProperties();
            foreach (var prop in properties)
            {
                prop.SetValue(result, prop.GetValue(this));
            }

            return result;
        }
    }
}

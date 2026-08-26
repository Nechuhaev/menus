// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Menus.Web.Helpers
{
    [HtmlTargetElement("span", Attributes = "asp-rate")]
    public class RateStarTagHelper : TagHelper
    {
        [HtmlAttributeName("asp-rate")]
        public int Rating { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var rating = Rating * 20;
            var existingClasses = output.Attributes.FirstOrDefault(a => a.Name == "class");

            output.Attributes.SetAttribute("class", existingClasses == null ? "" : $"{existingClasses.Value} rate_{rating}");

            base.Process(context, output);
        }
    }
}

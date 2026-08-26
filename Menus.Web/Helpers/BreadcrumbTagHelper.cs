using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Menus.Web.Helpers
{
    [HtmlTargetElement("breadcrumb")]
    public class BreadcrumbTagHelper : AnchorTagHelper
    {
        public BreadcrumbTagHelper(IHtmlGenerator generator) : base(generator)
        {
        }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            base.Process(context, output);

            output.TagName = "a";
            output.Attributes.Add("itemprop", "url");

            output.PreContent.SetHtmlContent("<span itemprop=\"title\">");
            output.PostContent.SetHtmlContent("</span>");

            output.PreElement.SetHtmlContent("<span itemscope itemtype=\"https://data-vocabulary.org/Breadcrumb\">");
            output.PostElement.SetHtmlContent("</span>");
        }
    }
}

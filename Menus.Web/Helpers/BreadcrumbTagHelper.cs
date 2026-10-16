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
            // Розмітка "хлібних крихт" - JSON-LD BreadcrumbList у сторінках; застарілий data-vocabulary прибрано.
            // itemscope лишається: на нього прив'язаний обробник кліку в common.js
            output.PreContent.SetHtmlContent("<span>");
            output.PostContent.SetHtmlContent("</span>");

            output.PreElement.SetHtmlContent("<span itemscope>");
            output.PostElement.SetHtmlContent("</span>");
        }
    }
}

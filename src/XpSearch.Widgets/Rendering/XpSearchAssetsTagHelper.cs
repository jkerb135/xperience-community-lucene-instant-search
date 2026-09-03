using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XpSearch.Widgets.Rendering;

/// <summary>
/// <c>&lt;xps-search-assets /&gt;</c> - emits the stylesheet and script tags of the Xperience Search
/// client. Place it once per page, in the <c>&lt;head&gt;</c> or at the end of the body.
/// </summary>
[HtmlTargetElement("xps-search-assets", TagStructure = TagStructure.WithoutEndTag)]
public sealed class XpSearchAssetsTagHelper : TagHelper
{
    /// <summary>Gets or sets whether the opt-in visual theme is loaded. Set <c>default-theme="false"</c> to load only the structural stylesheet.</summary>
    [HtmlAttributeName("default-theme")]
    public bool DefaultTheme { get; set; } = true;

    /// <summary>Gets or sets which shipped palette is loaded: <c>default</c> (= <c>kentico-violet</c>) or <c>kentico-orange</c>.</summary>
    [HtmlAttributeName("theme")]
    public string Theme { get; set; } = XpSearchAssets.DefaultThemeName;

    /// <summary>Gets or sets the current view context.</summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = null;
        output.Content.SetHtmlContent(XpSearchAssets.Render(ViewContext.HttpContext.Request.PathBase, DefaultTheme, Theme));
    }
}

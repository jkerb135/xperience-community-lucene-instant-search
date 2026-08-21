using Kentico.Content.Web.Mvc;
using Kentico.PageBuilder.Web.Mvc;
using Kentico.Web.Mvc;

using Microsoft.AspNetCore.Http;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// <see cref="IXpSearchEditorContext"/> over the Xperience page state.
/// </summary>
/// <remarks>
/// Follows "Display messages to editors but not live site visitors" from
/// https://docs.kentico.com/guides/development/page-builder/define-advanced-widget - Page Builder
/// mode <c>Off</c> plus preview disabled is the only combination that means "a live-site visitor".
/// </remarks>
internal sealed class KenticoEditorContext : IXpSearchEditorContext
{
    private readonly IHttpContextAccessor accessor;

    public KenticoEditorContext(IHttpContextAccessor accessor) => this.accessor = accessor;

    public XpSearchEditorMode GetMode()
    {
        var httpContext = accessor.HttpContext;
        if (httpContext is null)
        {
            return XpSearchEditorMode.Live;
        }

        return httpContext.Kentico().PageBuilder().GetMode() switch
        {
            PageBuilderMode.Edit => XpSearchEditorMode.Edit,
            PageBuilderMode.ReadOnly => XpSearchEditorMode.ReadOnly,
            _ => httpContext.Kentico().Preview().Enabled ? XpSearchEditorMode.Preview : XpSearchEditorMode.Live
        };
    }
}

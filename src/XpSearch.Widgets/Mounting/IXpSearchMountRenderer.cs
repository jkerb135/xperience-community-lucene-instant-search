using Microsoft.AspNetCore.Html;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// Turns an <see cref="XpSearchMount"/> into the mount element. A seam so the markup contract can be
/// asserted without rendering Razor, and so a project can override how the element is emitted.
/// </summary>
public interface IXpSearchMountRenderer
{
    /// <summary>Renders the mount element.</summary>
    /// <param name="mount">The mount to render.</param>
    /// <returns>The <c>&lt;div class="xps-mount" …&gt;</c> element.</returns>
    IHtmlContent Render(XpSearchMount mount);
}

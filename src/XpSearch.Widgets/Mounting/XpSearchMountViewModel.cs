using Microsoft.AspNetCore.Html;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// What <c>_Mount.cshtml</c> renders: either a mount element, or a static preview inside the Page
/// Builder, or an editor-only instruction block, or - on a live page with an unconfigured widget -
/// nothing (spec §7.5).
/// </summary>
public sealed class XpSearchMountViewModel
{
    /// <summary>
    /// Gets the mount element, or <see langword="null"/> when the widget is not configured or is
    /// being rendered inside the Page Builder.
    /// </summary>
    public IHtmlContent? Mount { get; init; }

    /// <summary>
    /// Gets the static preview shown to an editor in the Page Builder, or <see langword="null"/>
    /// outside it. The Page Builder re-renders widget markup over AJAX on every add, move and
    /// configure, so a configured widget shows a picture of itself there instead of a mount element
    /// that would hydrate unreliably and fire search requests from the editor.
    /// </summary>
    public IHtmlContent? Preview { get; init; }

    /// <summary>
    /// Gets the instruction text for editors, or <see langword="null"/> when there is nothing to say
    /// (the widget is configured, or the page is being viewed by a live-site visitor).
    /// </summary>
    public string? EditorMessage { get; init; }

    /// <summary>Gets the heading of the editor-only instruction block.</summary>
    public string? EditorTitle { get; init; }
}

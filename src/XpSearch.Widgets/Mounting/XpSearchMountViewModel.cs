using Microsoft.AspNetCore.Html;

namespace XpSearch.Widgets.Mounting;

/// <summary>
/// What <c>_Mount.cshtml</c> renders: either a mount element, or an editor-only instruction block,
/// or - on a live page with an unconfigured widget - nothing (spec §7.5).
/// </summary>
public sealed class XpSearchMountViewModel
{
    /// <summary>Gets the mount element, or <see langword="null"/> when the widget is not configured.</summary>
    public IHtmlContent? Mount { get; init; }

    /// <summary>
    /// Gets the instruction text for editors, or <see langword="null"/> when there is nothing to say
    /// (the widget is configured, or the page is being viewed by a live-site visitor).
    /// </summary>
    public string? EditorMessage { get; init; }

    /// <summary>Gets the heading of the editor-only instruction block.</summary>
    public string? EditorTitle { get; init; }
}

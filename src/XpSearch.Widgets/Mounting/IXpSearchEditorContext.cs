namespace XpSearch.Widgets.Mounting;

/// <summary>
/// How the page carrying a widget is being viewed. Decides whether the unconfigured-state
/// instruction block is rendered (spec §7.5).
/// </summary>
public enum XpSearchEditorMode
{
    /// <summary>A live-site visitor. An unconfigured widget renders nothing at all.</summary>
    Live,

    /// <summary>The Page Builder, editable.</summary>
    Edit,

    /// <summary>The Page Builder, read-only.</summary>
    ReadOnly,

    /// <summary>Preview mode outside the Page Builder.</summary>
    Preview
}

/// <summary>
/// Supplies the current <see cref="XpSearchEditorMode"/>. A seam over
/// <c>HttpContext.Kentico().PageBuilder().GetMode()</c> and <c>HttpContext.Kentico().Preview()</c>
/// so widget output is testable without an Xperience application.
/// </summary>
public interface IXpSearchEditorContext
{
    /// <summary>Gets the mode of the current request.</summary>
    /// <returns>The mode.</returns>
    XpSearchEditorMode GetMode();
}

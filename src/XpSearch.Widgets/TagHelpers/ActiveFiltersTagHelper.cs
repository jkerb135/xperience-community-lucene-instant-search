using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>activeFilters</c> widget - the visitor's refinements as removable chips.</summary>
public sealed record ActiveFiltersOptions : XpSearchMountOptions
{
    /// <summary>Gets the heading screen readers announce for the chip list. It is never shown on screen.</summary>
    public string? Title { get; init; }

    /// <summary>Gets whether the chips stay on one scrolling row instead of wrapping.</summary>
    public bool Scroll { get; init; }
}

/// <summary><c>&lt;xps-active-filters /&gt;</c> - mounts the <c>activeFilters</c> widget.</summary>
[HtmlTargetElement("xps-active-filters")]
public sealed class ActiveFiltersTagHelper : XpSearchMountTagHelper<ActiveFiltersOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveFiltersTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ActiveFiltersTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Title"/>.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Scroll"/>.</summary>
    [HtmlAttributeName("scroll")]
    public bool? Scroll { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "activeFilters";

    /// <inheritdoc />
    protected override ActiveFiltersOptions Merge(ActiveFiltersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Title = Title ?? options.Title,
            Scroll = Scroll ?? options.Scroll
        };
    }
}

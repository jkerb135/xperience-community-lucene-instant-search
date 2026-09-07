using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>clearFilters</c> widget - the button that removes every refinement.</summary>
public sealed record ClearFiltersOptions : XpSearchMountOptions
{
    /// <summary>Gets the button text. Empty keeps "Clear all".</summary>
    public string? Label { get; init; }
}

/// <summary><c>&lt;xps-clear-filters /&gt;</c> - mounts the <c>clearFilters</c> widget.</summary>
[HtmlTargetElement("xps-clear-filters")]
public sealed class ClearFiltersTagHelper : XpSearchMountTagHelper<ClearFiltersOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ClearFiltersTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ClearFiltersTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ClearFiltersOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "clearFilters";

    /// <inheritdoc />
    protected override ClearFiltersOptions Merge(ClearFiltersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with { Label = Label ?? options.Label };
    }
}

using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>facetList</c> widget - filter checkboxes for one attribute.</summary>
public sealed record FacetListOptions : XpSearchMountOptions
{
    /// <summary>Gets the index attribute the facet filters on.</summary>
    public string? Attribute { get; init; }

    /// <summary>Gets the heading shown above the values.</summary>
    public string? Label { get; init; }

    /// <summary>Gets how several selected values combine: <c>or</c> or <c>and</c>.</summary>
    public string Operator { get; init; } = "or";

    /// <summary>Gets how many values are listed before "show more".</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Gets whether a "show more" button reveals the remaining values.</summary>
    public bool ShowMore { get; init; }

    /// <summary>Gets whether the group's title folds the values away.</summary>
    public bool Collapsible { get; init; } = true;
}

/// <summary><c>&lt;xps-facet-list /&gt;</c> - mounts the <c>facetList</c> widget.</summary>
[HtmlTargetElement("xps-facet-list")]
public sealed class FacetListTagHelper : XpSearchMountTagHelper<FacetListOptions>
{
    /// <summary>Initializes a new instance of the <see cref="FacetListTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public FacetListTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="FacetListOptions.Attribute"/>.</summary>
    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    /// <summary>Gets or sets <see cref="FacetListOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="FacetListOptions.Operator"/>.</summary>
    [HtmlAttributeName("operator")]
    public string? Operator { get; set; }

    /// <summary>Gets or sets <see cref="FacetListOptions.Limit"/>.</summary>
    [HtmlAttributeName("limit")]
    public int? Limit { get; set; }

    /// <summary>Gets or sets <see cref="FacetListOptions.ShowMore"/>.</summary>
    [HtmlAttributeName("show-more")]
    public bool? ShowMore { get; set; }

    /// <summary>Gets or sets <see cref="FacetListOptions.Collapsible"/>.</summary>
    [HtmlAttributeName("collapsible")]
    public bool? Collapsible { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "facetList";

    /// <inheritdoc />
    public override string? Validate(FacetListOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (string.IsNullOrWhiteSpace(options.Attribute) ? WidgetResources.Hint_SelectAttribute : null);
    }

    /// <inheritdoc />
    protected override FacetListOptions Merge(FacetListOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Attribute = Attribute ?? options.Attribute,
            Label = Label ?? options.Label,
            Operator = Operator ?? options.Operator,
            Limit = Limit ?? options.Limit,
            ShowMore = ShowMore ?? options.ShowMore,
            Collapsible = Collapsible ?? options.Collapsible
        };
    }
}

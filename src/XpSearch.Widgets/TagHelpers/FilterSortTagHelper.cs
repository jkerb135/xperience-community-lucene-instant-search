using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.Sorting;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>filterSort</c> widget - the mobile filter and sort sheet.</summary>
public sealed record FilterSortOptions : XpSearchMountOptions
{
    /// <summary>
    /// Gets the facet groups the sheet shows, one per line as <c>attribute;Label</c>, in the order
    /// they appear. A line without a label leaves the section named after the attribute.
    /// </summary>
    public string Facets { get; init; } = string.Empty;

    /// <summary>Gets the offered orders, one per line as <c>key;Label</c>. Empty hides the sort section.</summary>
    public string SortOptions { get; init; } = string.Empty;

    /// <summary>Gets the trigger and sheet heading. Empty keeps the JavaScript default.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Gets the text of the sheet's primary button. A <c>{count}</c> placeholder is replaced with how
    /// many results the pending selection would return. Empty keeps "Show {count} results".
    /// </summary>
    public string? ApplyLabel { get; init; }
}

/// <summary><c>&lt;xps-filter-sort /&gt;</c> - mounts the <c>filterSort</c> widget.</summary>
[HtmlTargetElement("xps-filter-sort")]
public sealed class FilterSortTagHelper : XpSearchMountTagHelper<FilterSortOptions>
{
    private readonly IOptionsMonitor<XpSearchOptions> searchOptions;

    /// <summary>Initializes a new instance of the <see cref="FilterSortTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="searchOptions">Supplies the sort keys configured per index.</param>
    public FilterSortTagHelper(
        IXpSearchMountRenderer renderer,
        IXpSearchIndexCatalog indexCatalog,
        IOptionsMonitor<XpSearchOptions> searchOptions)
        : base(renderer, indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        this.searchOptions = searchOptions;
    }

    /// <summary>Gets or sets <see cref="FilterSortOptions.Facets"/>.</summary>
    [HtmlAttributeName("facets")]
    public string? Facets { get; set; }

    /// <summary>Gets or sets <see cref="FilterSortOptions.SortOptions"/>.</summary>
    [HtmlAttributeName("sort-options")]
    public string? SortOptions { get; set; }

    /// <summary>Gets or sets <see cref="FilterSortOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="FilterSortOptions.ApplyLabel"/>.</summary>
    [HtmlAttributeName("apply-label")]
    public string? ApplyLabel { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "filterSort";

    /// <inheritdoc />
    public override string? Validate(FilterSortOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (SortOptionsValidation.Parse(options.Facets).Count == 0 ? WidgetResources.Hint_FilterSortFacets : null);
    }

    /// <inheritdoc />
    protected override FilterSortOptions Merge(FilterSortOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Facets = Facets ?? options.Facets,
            SortOptions = SortOptions ?? options.SortOptions,
            Label = Label ?? options.Label,
            ApplyLabel = ApplyLabel ?? options.ApplyLabel
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(FilterSortOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        // The facet lines have the same key;Label shape as the sort options, so they go through the
        // same parser.
        config["facets"] = SortOptionsValidation.Parse(options.Facets)
            .Select(facet => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attribute"] = facet.Value,
                ["label"] = facet.Label
            })
            .ToList();

        var sort = SortOptionsValidation.ParseValid(
            options.SortOptions,
            searchOptions.CurrentValue.Indexes.TryGetValue(CurrentIndex, out var index) ? index : null);

        if (sort.Count > 0)
        {
            config["sortOptions"] = sort
                .Select(option => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["value"] = option.Value,
                    ["label"] = option.Label
                })
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(options.Label))
        {
            config["label"] = options.Label;
        }

        if (!string.IsNullOrWhiteSpace(options.ApplyLabel))
        {
            config["applyLabel"] = options.ApplyLabel;
        }
    }
}

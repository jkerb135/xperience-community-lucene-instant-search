using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.Sorting;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>sortSelect</c> widget - the order of the results.</summary>
public sealed record SortSelectOptions : XpSearchMountOptions
{
    /// <summary>
    /// Gets the offered orders, one per line as <c>key;Label</c>. A key is <c>relevance</c>, a sort
    /// key configured for the index, or a sortable field with an <c>_asc</c> / <c>_desc</c> suffix;
    /// keys the index does not publish are dropped.
    /// </summary>
    public string SortOptions { get; init; } = "relevance;Most relevant";

    /// <summary>Gets the label of the selector. Empty keeps the JavaScript default.</summary>
    public string? Label { get; init; }

    /// <summary>Gets whether the label is hidden from sighted users. It stays available to screen readers.</summary>
    public bool HideLabel { get; init; }
}

/// <summary><c>&lt;xps-sort-select /&gt;</c> - mounts the <c>sortSelect</c> widget.</summary>
[HtmlTargetElement("xps-sort-select")]
public sealed class SortSelectTagHelper : XpSearchMountTagHelper<SortSelectOptions>
{
    private readonly IOptionsMonitor<XpSearchOptions> searchOptions;

    /// <summary>Initializes a new instance of the <see cref="SortSelectTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="searchOptions">Supplies the sort keys configured per index.</param>
    public SortSelectTagHelper(
        IXpSearchMountRenderer renderer,
        IXpSearchIndexCatalog indexCatalog,
        IOptionsMonitor<XpSearchOptions> searchOptions)
        : base(renderer, indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        this.searchOptions = searchOptions;
    }

    /// <summary>Gets or sets <see cref="SortSelectOptions.SortOptions"/>.</summary>
    [HtmlAttributeName("sort-options")]
    public string? SortOptions { get; set; }

    /// <summary>Gets or sets <see cref="SortSelectOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="SortSelectOptions.HideLabel"/>.</summary>
    [HtmlAttributeName("hide-label")]
    public bool? HideLabel { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "sortSelect";

    /// <inheritdoc />
    /// <remarks>A selector whose every key would be rejected by the API is a misconfiguration, not an empty list.</remarks>
    public override string? Validate(SortSelectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (ValidOptions(options).Count == 0 ? WidgetResources.Hint_SortOptions : null);
    }

    /// <inheritdoc />
    protected override SortSelectOptions Merge(SortSelectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            SortOptions = SortOptions ?? options.SortOptions,
            Label = Label ?? options.Label,
            HideLabel = HideLabel ?? options.HideLabel
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(SortSelectOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        config["items"] = ValidOptions(options)
            .Select(option => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = option.Value,
                ["label"] = option.Label
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(options.Label))
        {
            config["label"] = options.Label;
        }

        config["hideLabel"] = options.HideLabel;
    }

    /// <summary>The offered orders the selected index actually publishes.</summary>
    /// <param name="options">The options.</param>
    /// <returns>The orders, in the order they were written.</returns>
    private IReadOnlyList<SortOption> ValidOptions(SortSelectOptions options) =>
        SortOptionsValidation.ParseValid(
            options.SortOptions,
            searchOptions.CurrentValue.Indexes.TryGetValue(CurrentIndex, out var index) ? index : null);
}

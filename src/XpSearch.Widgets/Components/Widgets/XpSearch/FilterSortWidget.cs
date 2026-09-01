using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.Sorting;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.FilterSortIdentifier,
    viewComponentType: typeof(FilterSortWidgetViewComponent),
    name: "Search - Filter & sort sheet",
    propertiesType: typeof(FilterSortWidgetProperties),
    Description = "Mobile toolbar button that opens a bottom sheet with the facets and the sort order.",
    IconClass = "icon-funnel",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the filter and sort sheet widget (spec §7.3).</summary>
public sealed class FilterSortWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the facet groups the sheet shows, one per line as <c>attribute;Label</c>, in the
    /// order they appear. A line without a label uses the attribute name as the heading.
    /// </summary>
    [TextAreaComponent(
        Label = "Facet groups",
        ExplanationText = "One per line, as attribute;Label - for example contentType;Content type.",
        Order = OrderFirstWidgetProperty)]
    public string Facets { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the offered orders, one per line as <c>key;Label</c>, exactly as the sort
    /// selector widget takes them. Empty leaves the sheet without a "Sort by" section.
    /// </summary>
    [TextAreaComponent(
        Label = "Sort options",
        ExplanationText = "One per line, as key;Label. Leave empty to hide the sort section.",
        Order = OrderFirstWidgetProperty + 10)]
    public string SortOptions { get; set; } = string.Empty;

    /// <summary>Gets or sets the trigger and sheet heading. Empty keeps the JavaScript default.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 20)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text of the sheet's primary button. A <c>{count}</c> placeholder is replaced
    /// with how many results the pending selection would return; it disappears, with the space after
    /// it, while that count is unknown. Empty keeps the JavaScript default, "Show {count} results".
    /// </summary>
    [TextInputComponent(
        Label = "Apply button text",
        ExplanationText = "Use {count} for the live result count, e.g. \"Show {count} results\". Leave empty for that default.",
        Order = OrderFirstWidgetProperty + 30)]
    public string ApplyLabel { get; set; } = string.Empty;
}

/// <summary>Renders the <c>filterSort</c> mount.</summary>
public sealed class FilterSortWidgetViewComponent : XpSearchMountWidgetViewComponent<FilterSortWidgetProperties>
{
    private readonly IOptions<XpSearchOptions> searchOptions;

    /// <summary>Initializes a new instance of the <see cref="FilterSortWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="searchOptions">Supplies the sort keys configured per index.</param>
    public FilterSortWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog,
        IOptions<XpSearchOptions> searchOptions)
        : base(renderer, editorContext, indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        this.searchOptions = searchOptions;
    }

    /// <inheritdoc />
    protected override string WidgetType => "filterSort";

    /// <inheritdoc />
    protected override string? ConfigurationHint(FilterSortWidgetProperties properties) =>
        SortOptionsValidation.Parse(properties?.Facets).Count == 0 ? WidgetResources.Hint_FilterSortFacets : null;

    /// <inheritdoc />
    protected override void BuildConfig(FilterSortWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        config["facets"] = Facets(properties)
            .Select(facet => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["attribute"] = facet.Value,
                ["label"] = facet.Label
            })
            .ToList();

        var sort = SortOptionsValidation.ParseValid(properties.SortOptions, IndexOptions());
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

        if (!string.IsNullOrWhiteSpace(properties.Label))
        {
            config["label"] = properties.Label;
        }

        if (!string.IsNullOrWhiteSpace(properties.ApplyLabel))
        {
            config["applyLabel"] = properties.ApplyLabel;
        }
    }

    /// <inheritdoc />
    /// <remarks>The sheet is interaction-only, so the preview is the trigger the editor placed, nothing more.</remarks>
    protected override IHtmlContent BuildEditorPreview(FilterSortWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var facets = Facets(properties);
        var trigger = EditorPreview.Button(
            "xps-button xps-filter-sort__trigger",
            string.IsNullOrWhiteSpace(properties.Label) ? "Filter & Sort" : properties.Label);

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("div", "xps-filter-sort").Add(trigger))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Attribute,
                string.Join(", ", facets.Select(facet => facet.Value)))));
    }

    /// <summary>
    /// The facet lines. They have the same <c>key;Label</c> shape as the sort options, so they go
    /// through the same parser.
    /// </summary>
    private static IReadOnlyList<SortOption> Facets(FilterSortWidgetProperties properties) =>
        SortOptionsValidation.Parse(properties.Facets);

    private XpSearchIndexOptions? IndexOptions() =>
        searchOptions.Value.Indexes.TryGetValue(CurrentIndex, out var options) ? options : null;
}

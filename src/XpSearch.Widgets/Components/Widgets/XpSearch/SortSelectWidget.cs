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
    identifier: XpSearchWidgetConstants.SortSelectIdentifier,
    viewComponentType: typeof(SortSelectWidgetViewComponent),
    name: "Search - Sort selector",
    propertiesType: typeof(SortSelectWidgetProperties),
    Description = "Lets a visitor choose the order of the search results.",
    IconClass = "icon-chevron-down",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the sort selector widget (spec §7.3).</summary>
public sealed class SortSelectWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the offered orders, one per line as <c>key;Label</c>. A key is
    /// <c>relevance</c>, a sort key configured for the index, or a sortable field with an
    /// <c>_asc</c> / <c>_desc</c> suffix.
    /// </summary>
    [TextAreaComponent(
        Label = "Sort options",
        ExplanationText = "One per line, as key;Label - for example relevance;Most relevant or publishedAt_desc;Newest first.",
        Order = OrderFirstWidgetProperty)]
    public string SortOptions { get; set; } = "relevance;Most relevant";

    /// <summary>Gets or sets the label of the selector. Empty keeps the JavaScript default.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the label is hidden from sighted users. It stays available to screen readers.</summary>
    [CheckBoxComponent(Label = "Hide the label visually", Order = OrderFirstWidgetProperty + 20)]
    public bool HideLabel { get; set; }
}

/// <summary>Renders the <c>sortSelect</c> mount.</summary>
public sealed class SortSelectWidgetViewComponent : XpSearchMountWidgetViewComponent<SortSelectWidgetProperties>
{
    private readonly IOptionsMonitor<XpSearchOptions> searchOptions;

    /// <summary>Initializes a new instance of the <see cref="SortSelectWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    /// <param name="searchOptions">Supplies the sort keys configured per index.</param>
    public SortSelectWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog,
        IOptionsMonitor<XpSearchOptions> searchOptions)
        : base(renderer, editorContext, indexCatalog)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        this.searchOptions = searchOptions;
    }

    /// <inheritdoc />
    protected override string WidgetType => "sortSelect";

    /// <inheritdoc />
    /// <remarks>A selector whose every key would be rejected by the API is a misconfiguration, not an empty list.</remarks>
    protected override string? ConfigurationHint(SortSelectWidgetProperties properties) =>
        SortOptionsValidation.ParseValid(properties?.SortOptions, IndexOptions()).Count == 0
            ? WidgetResources.Hint_SortOptions
            : null;

    /// <inheritdoc />
    protected override void BuildConfig(SortSelectWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        config["items"] = SortOptionsValidation.ParseValid(properties.SortOptions, IndexOptions())
            .Select(option => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = option.Value,
                ["label"] = option.Label
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(properties.Label))
        {
            config["label"] = properties.Label;
        }

        config["hideLabel"] = properties.HideLabel;
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(SortSelectWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var select = EditorPreview.El("select", "xps-select__control").Disabled();
        foreach (var option in SortOptionsValidation.ParseValid(properties.SortOptions, IndexOptions()))
        {
            select.Add(EditorPreview.El("option", text: option.Label).Attr("value", option.Value));
        }

        var box = EditorPreview.El("div", "xps-sort-select xps-select");

        if (!string.IsNullOrWhiteSpace(properties.Label))
        {
            box.Add(EditorPreview.El(
                "label",
                properties.HideLabel ? "xps-select__label xps-sr-only" : "xps-select__label",
                properties.Label));
        }

        return box.Add(select);
    }

    private XpSearchIndexOptions? IndexOptions() =>
        searchOptions.CurrentValue.Indexes.TryGetValue(CurrentIndex, out var options) ? options : null;
}

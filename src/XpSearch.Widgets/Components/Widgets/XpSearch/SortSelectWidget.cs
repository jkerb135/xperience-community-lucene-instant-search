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
using XpSearch.Widgets.TagHelpers;

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
        Tooltip = "The orders the selector offers, one per line.",
        ExplanationText = "One per line, as key;Label - for example relevance;Most relevant or publishedAt_desc;Newest first. A key is 'relevance', a sort key a developer configured for this index in code, or a sortable index field with an _asc / _desc suffix; a key the index does not publish returns an error instead of results.",
        Order = OrderFirstWidgetProperty)]
    public string SortOptions { get; set; } = "relevance;Most relevant";

    /// <summary>Gets or sets the label of the selector. Empty keeps the JavaScript default.</summary>
    [TextInputComponent(
        Label = "Label",
        Tooltip = "The text shown beside the drop-down.",
        ExplanationText = "Empty keeps the built-in wording. The label always names the drop-down for screen readers, whether it is visible or not.",
        Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the label is hidden from sighted users. It stays available to screen readers.</summary>
    [CheckBoxComponent(
        Label = "Hide the label visually",
        Tooltip = "Hides the label on screen, keeping it for screen readers.",
        ExplanationText = "For a toolbar where the drop-down is self-explanatory. The label is not removed, only visually hidden.",
        Order = OrderFirstWidgetProperty + 20)]
    public bool HideLabel { get; set; }
}

/// <summary>Renders the <c>sortSelect</c> mount.</summary>
public sealed class SortSelectWidgetViewComponent : XpSearchMountWidgetViewComponent<SortSelectWidgetProperties, SortSelectOptions>
{
    private readonly IOptionsMonitor<XpSearchOptions> searchOptions;

    /// <summary>Initializes a new instance of the <see cref="SortSelectWidgetViewComponent"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="searchOptions">Supplies the sort keys configured per index, for the preview.</param>
    public SortSelectWidgetViewComponent(
        XpSearchMountTagHelper<SortSelectOptions> tagHelper,
        IXpSearchEditorContext editorContext,
        IOptionsMonitor<XpSearchOptions> searchOptions)
        : base(tagHelper, editorContext)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        this.searchOptions = searchOptions;
    }

    /// <inheritdoc />
    public override SortSelectOptions ToOptions(SortSelectWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new SortSelectOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            SortOptions = properties.SortOptions,
            Label = properties.Label,
            HideLabel = properties.HideLabel
        };
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

    // The tag helper resolved the index while validating, which is what runs before the preview.
    private XpSearchIndexOptions? IndexOptions() =>
        searchOptions.CurrentValue.Indexes.TryGetValue(TagHelper.CurrentIndex, out var options) ? options : null;
}

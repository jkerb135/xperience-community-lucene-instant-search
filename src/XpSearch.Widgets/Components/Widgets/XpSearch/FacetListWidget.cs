using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

using XpSearch.Core;
using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.FacetListIdentifier,
    viewComponentType: typeof(FacetListWidgetViewComponent),
    name: "Search - Facet list",
    propertiesType: typeof(FacetListWidgetProperties),
    Description = "Displays filter checkboxes for one attribute of the search index.",
    IconClass = "icon-funnel",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the facet list widget (spec §7.3, §7.4).</summary>
public sealed class FacetListWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the index attribute the facet filters on. The drop-down is filled from the
    /// selected index's schema, so only facetable fields can be chosen.
    /// </summary>
    [DropDownComponent(
        Label = "Attribute",
        Placeholder = "Select an attribute",
        Tooltip = "The index attribute this group filters on.",
        ExplanationText = "The list is the selected index's facetable fields. A Search - Filter & sort sheet on the same page has to name the same attribute for its mobile counterpart of this group.",
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the heading shown above the values. Empty leaves the group unnamed rather than showing the attribute code.</summary>
    [TextInputComponent(
        Label = "Label",
        Tooltip = "The heading shown above the values.",
        ExplanationText = "Set it: a visitor must never read a field code, so leaving it empty shows a generic heading instead and the active-filter chips of this attribute lose their name.",
        Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets how several selected values combine.</summary>
    [DropDownComponent(
        Label = "Operator",
        Options = "or;Match any of the selected values\r\nand;Match all of the selected values",
        Tooltip = "How several ticked values of this group combine.",
        ExplanationText = "'Any' widens the results with each tick and is what most facets want; 'all' narrows them, which only makes sense where one result can carry several values, such as tags.",
        Order = OrderFirstWidgetProperty + 20)]
    public string Operator { get; set; } = "or";

    /// <summary>Gets or sets how many values are listed before "show more".</summary>
    [NumberInputComponent(
        Label = "Values shown",
        Tooltip = "How many values are listed before the \"show more\" button.",
        ExplanationText = "A display limit over the values the response carried, so the index setting 'Maximum values per facet' is the real ceiling: asking for more than it returns shows all there is.",
        Order = OrderFirstWidgetProperty + 30)]
    public int Limit { get; set; } = 10;

    /// <summary>Gets or sets whether a "show more" button reveals the remaining values.</summary>
    [CheckBoxComponent(
        Label = "Show a \"show more\" button",
        Tooltip = "Reveals the values beyond 'Values shown'.",
        ExplanationText = "Off, the values past that number are simply not offered. Turn it on for an attribute with a long tail of values.",
        Order = OrderFirstWidgetProperty + 40)]
    public bool ShowMore { get; set; }

    /// <summary>Gets or sets whether the group's title folds the values away.</summary>
    [CheckBoxComponent(
        Label = "Title folds the group",
        Tooltip = "Makes the heading a disclosure button.",
        ExplanationText = "The title becomes a button with a chevron. The group starts open, and the state is not remembered between page loads.",
        Order = OrderFirstWidgetProperty + 50)]
    public bool Collapsible { get; set; } = true;
}

/// <summary>Renders the <c>facetList</c> mount.</summary>
public sealed class FacetListWidgetViewComponent : XpSearchMountWidgetViewComponent<FacetListWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="FacetListWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public FacetListWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "facetList";

    /// <inheritdoc />
    protected override string? ConfigurationHint(FacetListWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties?.Attribute) ? WidgetResources.Hint_SelectAttribute : null;

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(FacetListWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var list = EditorPreview.El("ul", "xps-facet-list__list");

        for (int row = 0; row < Math.Min(3, Math.Max(1, properties.Limit)); row++)
        {
            list.Add(EditorPreview.El("li", "xps-facet-list__item")
                .Add(EditorPreview.El("label", "xps-facet-list__label")
                    .Add(
                        EditorPreview.Input("xps-facet-list__checkbox", "checkbox"),
                        EditorPreview.El("span", "xps-facet-list__value").Add(EditorPreview.Skeleton("text")),
                        EditorPreview.El("span", "xps-facet-list__count").Add(EditorPreview.Skeleton("text")))));
        }

        var body = EditorPreview.El("div", "xps-facet-list__body").Add(list);

        if (properties.ShowMore)
        {
            body.Add(EditorPreview.Button("xps-button xps-facet-list__show-more", WidgetResources.Preview_ShowMore));
        }

        var facet = EditorPreview.El("div", "xps-facet-list").Add(Title(properties), body);

        return new HtmlContentBuilder()
            .AppendHtml(facet)
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Attribute,
                properties.Attribute.Trim())));
    }

    /// <summary>The title, as a disclosure button when the group folds and plain text when it does not.</summary>
    private static TagBuilder Title(FacetListWidgetProperties properties)
    {
        var title = EditorPreview.El("h3", "xps-facet-list__title");

        if (!properties.Collapsible)
        {
            return title.Add(EditorPreview.El("span", text: Heading(properties)));
        }

        var toggle = EditorPreview.El("button", "xps-facet-list__toggle")
            .Attr("type", "button")
            .Attr("aria-expanded", "true")
            .Disabled()
            .Add(
                EditorPreview.El("span", "xps-facet-list__toggle-label", Heading(properties)),
                EditorPreview.Chevron("xps-facet-list__chevron"));

        return title.Add(toggle);
    }

    private static string Heading(FacetListWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties.Label) ? properties.Attribute.Trim() : properties.Label;
}

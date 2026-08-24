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
    identifier: XpSearchWidgetConstants.CategoryTreeIdentifier,
    viewComponentType: typeof(CategoryTreeWidgetViewComponent),
    name: "Search - Category tree",
    propertiesType: typeof(CategoryTreeWidgetProperties),
    Description = "Displays a taxonomy attribute of the search index as a drill-down tree.",
    IconClass = "icon-tree-structure",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the category tree widget (spec §7.3, §7.4).</summary>
public sealed class CategoryTreeWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the index attribute the tree navigates. The drop-down is filled from the
    /// selected index's schema, so only facetable fields can be chosen; pick a taxonomy field, as
    /// only those carry a hierarchy.
    /// </summary>
    [DropDownComponent(
        Label = "Attribute",
        Placeholder = "Select an attribute",
        ExplanationText = "A taxonomy attribute. A flat attribute renders as one level.",
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the heading shown above the tree. Empty falls back to the attribute name.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets how many nodes are listed at each level of the tree.</summary>
    [NumberInputComponent(Label = "Nodes per level", Order = OrderFirstWidgetProperty + 20)]
    public int Limit { get; set; } = 10;
}

/// <summary>Renders the <c>categoryTree</c> mount.</summary>
public sealed class CategoryTreeWidgetViewComponent : XpSearchMountWidgetViewComponent<CategoryTreeWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="CategoryTreeWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public CategoryTreeWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "categoryTree";

    /// <inheritdoc />
    protected override string? ConfigurationHint(CategoryTreeWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties?.Attribute) ? WidgetResources.Hint_SelectAttribute : null;

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(CategoryTreeWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var children = EditorPreview.El("ul", "xps-category-tree__list xps-category-tree__list--lvl1")
            .Add(Node(), Node());

        var root = EditorPreview.El("ul", "xps-category-tree__list xps-category-tree__list--lvl0")
            .Add(
                EditorPreview.El("li", "xps-category-tree__item xps-category-tree__item--parent")
                    .Add(Link(), children),
                Node());

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("nav", "xps-category-tree")
                .Add(
                    EditorPreview.El(
                        "h3",
                        "xps-category-tree__title",
                        string.IsNullOrWhiteSpace(properties.Label) ? properties.Attribute.Trim() : properties.Label),
                    root))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Attribute,
                properties.Attribute.Trim())));
    }

    private static TagBuilder Node() => EditorPreview.El("li", "xps-category-tree__item").Add(Link());

    // A span, not an anchor: nothing in a preview is navigable.
    private static TagBuilder Link() =>
        EditorPreview.El("span", "xps-category-tree__link")
            .Add(
                EditorPreview.El("span", "xps-category-tree__value").Add(EditorPreview.Skeleton("text")),
                EditorPreview.El("span", "xps-category-tree__count").Add(EditorPreview.Skeleton("text")));
}

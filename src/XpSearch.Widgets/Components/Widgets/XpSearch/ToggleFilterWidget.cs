using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Core;
using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.TagHelpers;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ToggleFilterIdentifier,
    viewComponentType: typeof(ToggleFilterWidgetViewComponent),
    name: "Search - Toggle filter",
    propertiesType: typeof(ToggleFilterWidgetProperties),
    Description = "A single checkbox that filters the results on one value of an attribute.",
    IconClass = "icon-funnel",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the toggle filter widget (spec §7.3, RZ-1 §1.6).</summary>
public sealed class ToggleFilterWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the index attribute the checkbox filters on. The drop-down is filled from the
    /// selected index's schema, so only facetable fields can be chosen.
    /// </summary>
    [DropDownComponent(
        Label = "Attribute",
        Placeholder = "Select an attribute",
        Tooltip = "The index attribute this checkbox filters on.",
        ExplanationText = "The list is the selected index's facetable fields. A toggle is a facet list narrowed to one value, so the attribute has to be facetable exactly as it does for a Search - Facet list.",
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.FacetAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the single value the checkbox filters on.</summary>
    [TextInputComponent(
        Label = "Value",
        Tooltip = "The one value of that attribute the checkbox switches on.",
        ExplanationText = "The value as the index stores it, for example true for a flag or en for a language. Empty means true, which is what a boolean field carries.",
        Order = OrderFirstWidgetProperty + 10)]
    public string Value { get; set; } = "true";

    /// <summary>Gets or sets the visible text. Empty leaves the label the server gave the value.</summary>
    [TextInputComponent(
        Label = "Label",
        Tooltip = "The text beside the checkbox.",
        ExplanationText = "Empty shows the name the index gives the value - for a taxonomy that is the tag title - so leave it empty unless the visitor needs different wording.",
        Order = OrderFirstWidgetProperty + 20)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the number of matching documents is shown beside the label.</summary>
    [CheckBoxComponent(
        Label = "Show the count",
        Tooltip = "Shows how many results carry the value.",
        ExplanationText = "On, the checkbox reads \"English only 42\". Off, the number is hidden but the checkbox is still disabled when no result carries the value.",
        Order = OrderFirstWidgetProperty + 30)]
    public bool ShowCount { get; set; } = true;
}

/// <summary>Renders the <c>toggleFilter</c> mount.</summary>
public sealed class ToggleFilterWidgetViewComponent : XpSearchMountWidgetViewComponent<ToggleFilterWidgetProperties, ToggleFilterOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ToggleFilterWidgetViewComponent"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper.</param>
    /// <param name="editorContext">The current editing mode.</param>
    public ToggleFilterWidgetViewComponent(
        XpSearchMountTagHelper<ToggleFilterOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    /// <inheritdoc />
    public override ToggleFilterOptions ToOptions(ToggleFilterWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new ToggleFilterOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            Attribute = properties.Attribute,
            Value = string.IsNullOrWhiteSpace(properties.Value) ? "true" : properties.Value.Trim(),
            Label = properties.Label,
            ShowCount = properties.ShowCount
        };
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(ToggleFilterWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var count = EditorPreview.El("span", "xps-toggle-filter__count").Add(EditorPreview.Skeleton("text"));

        if (!properties.ShowCount)
        {
            count.Attr("hidden", "hidden");
        }

        var toggle = EditorPreview.El("div", "xps-toggle-filter")
            .Add(EditorPreview.El("label", "xps-toggle-filter__label")
                .Add(
                    EditorPreview.Input("xps-toggle-filter__checkbox", "checkbox"),
                    EditorPreview.El("span", "xps-toggle-filter__value", Text(properties)),
                    count));

        return new HtmlContentBuilder()
            .AppendHtml(toggle)
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_ToggleFilter,
                properties.Attribute.Trim(),
                string.IsNullOrWhiteSpace(properties.Value) ? "true" : properties.Value.Trim())));
    }

    // What the live widget shows before a response names the value: the editor's label, or the value.
    private static string Text(ToggleFilterWidgetProperties properties) =>
        string.IsNullOrWhiteSpace(properties.Label)
            ? (string.IsNullOrWhiteSpace(properties.Value) ? "true" : properties.Value.Trim())
            : properties.Label;
}

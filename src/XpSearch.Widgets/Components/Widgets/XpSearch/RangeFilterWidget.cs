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
    identifier: XpSearchWidgetConstants.RangeFilterIdentifier,
    viewComponentType: typeof(RangeFilterWidgetViewComponent),
    name: "Search - Range filter",
    propertiesType: typeof(RangeFilterWidgetProperties),
    Description = "Filters the results to a range of a numeric or date attribute of the search index.",
    IconClass = "icon-arrows-h",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the range filter widget (spec §7.3, §7.4).</summary>
public sealed class RangeFilterWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>
    /// Gets or sets the index attribute the filter narrows. The drop-down is filled from the selected
    /// index's schema, so only numeric and date fields can be chosen.
    /// </summary>
    [DropDownComponent(
        Label = "Attribute",
        Placeholder = "Select an attribute",
        Tooltip = "The numeric or date index attribute the range narrows.",
        ExplanationText = "The list is the selected index's numeric and date fields. The refinement joins the chips of a Search - Active filters widget and is removed by Search - Clear filters, like any facet.",
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.NumericAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the heading shown above the control. Empty leaves the group unnamed rather than showing the attribute code.</summary>
    [TextInputComponent(
        Label = "Label",
        Tooltip = "The heading shown above the control.",
        ExplanationText = "Set it: a visitor must never read a field code, so leaving it empty shows a generic heading instead and the active-filter chips of this attribute lose their name.",
        Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the lower end of the control.</summary>
    [DecimalNumberInputComponent(
        Label = "Minimum",
        Tooltip = "The lowest value the control offers.",
        ExplanationText = "The search response carries no statistics about the corpus, so both bounds have to be set here. Set it from what the content actually holds: a visitor cannot pick a value below it.",
        Order = OrderFirstWidgetProperty + 20)]
    public decimal? Minimum { get; set; }

    /// <summary>Gets or sets the upper end of the control.</summary>
    [DecimalNumberInputComponent(
        Label = "Maximum",
        Tooltip = "The highest value the control offers.",
        ExplanationText = "The other hand-set bound. Set it from what the content actually holds: a visitor cannot pick a value above it.",
        Order = OrderFirstWidgetProperty + 30)]
    public decimal? Maximum { get; set; }

    /// <summary>Gets or sets the step of the sliders and the number inputs.</summary>
    [DecimalNumberInputComponent(
        Label = "Step",
        Tooltip = "How far one move of a slider or a number input goes.",
        ExplanationText = "Match it to the precision of the attribute: 1 for whole units, 0.01 for a price in cents.",
        Order = OrderFirstWidgetProperty + 40)]
    public decimal? Step { get; set; } = 1m;

    /// <summary>Gets or sets the visible label of the lower number input. Empty leaves "From".</summary>
    [TextInputComponent(
        Label = "\"From\" label",
        Tooltip = "The label of the lower number input.",
        ExplanationText = "Empty keeps \"From\".",
        Order = OrderFirstWidgetProperty + 50)]
    public string FromLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the visible label of the upper number input. Empty leaves "To".</summary>
    [TextInputComponent(
        Label = "\"To\" label",
        Tooltip = "The label of the upper number input.",
        ExplanationText = "Empty keeps \"To\".",
        Order = OrderFirstWidgetProperty + 60)]
    public string ToLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the unit shown after the two number inputs, such as "USD" or "kg".</summary>
    [TextInputComponent(
        Label = "Unit",
        Tooltip = "Shown after the two number inputs.",
        ExplanationText = "Decoration only - \"USD\", \"kg\", \"pages\". It does not convert or format the values.",
        Order = OrderFirstWidgetProperty + 70)]
    public string Unit { get; set; } = string.Empty;
}

/// <summary>Renders the <c>rangeFilter</c> mount.</summary>
public sealed class RangeFilterWidgetViewComponent : XpSearchMountWidgetViewComponent<RangeFilterWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="RangeFilterWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public RangeFilterWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "rangeFilter";

    /// <inheritdoc />
    protected override string? ConfigurationHint(RangeFilterWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (string.IsNullOrWhiteSpace(properties.Attribute))
        {
            return WidgetResources.Hint_SelectAttribute;
        }

        // Without usable bounds the JavaScript widget renders a disabled control, which is worse than
        // telling the editor what is missing.
        return properties.Minimum is null || properties.Maximum is null || properties.Minimum >= properties.Maximum
            ? WidgetResources.Hint_RangeBounds
            : null;
    }

    /// <inheritdoc />
    protected override void BuildConfig(RangeFilterWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        config["attribute"] = properties.Attribute.Trim();
        config["min"] = properties.Minimum;
        config["max"] = properties.Maximum;

        if (properties.Step is > 0)
        {
            config["step"] = properties.Step;
        }

        if (!string.IsNullOrWhiteSpace(properties.Label))
        {
            config["label"] = properties.Label;
        }

        var labels = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(properties.FromLabel))
        {
            labels["from"] = properties.FromLabel;
        }

        if (!string.IsNullOrWhiteSpace(properties.ToLabel))
        {
            labels["to"] = properties.ToLabel;
        }

        if (labels.Count > 0)
        {
            config["labels"] = labels;
        }

        if (!string.IsNullOrWhiteSpace(properties.Unit))
        {
            config["unit"] = properties.Unit.Trim();
        }
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(RangeFilterWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // ConfigurationHint() has already rejected missing or crossed bounds.
        decimal minimum = properties.Minimum!.Value;
        decimal maximum = properties.Maximum!.Value;
        decimal step = properties.Step is > 0 ? properties.Step.Value : 1m;

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("div", "xps-range-filter")
                .Add(
                    EditorPreview.El(
                        "h3",
                        "xps-range-filter__title",
                        string.IsNullOrWhiteSpace(properties.Label) ? properties.Attribute.Trim() : properties.Label),
                    EditorPreview.El("div", "xps-range-filter__track")
                        .Add(
                            Bounded(EditorPreview.Input("xps-range-filter__range xps-range-filter__range--min", "range"), minimum, maximum, step, minimum),
                            Bounded(EditorPreview.Input("xps-range-filter__range xps-range-filter__range--max", "range"), minimum, maximum, step, maximum)),
                    Inputs(properties, minimum, maximum, step),
                    EditorPreview.El(
                        "p",
                        "xps-range-filter__values",
                        $"{Display(minimum)} – {Display(maximum)} ({Display(step)})")))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Attribute,
                properties.Attribute.Trim())));
    }

    /// <summary>The one inline row: From, To and the unit.</summary>
    private static TagBuilder Inputs(
        RangeFilterWidgetProperties properties,
        decimal minimum,
        decimal maximum,
        decimal step)
    {
        var row = EditorPreview.El("div", "xps-range-filter__inputs")
            .Add(
                EditorPreview.El("label", "xps-range-filter__input-label", Label(properties.FromLabel, WidgetResources.Preview_From)),
                Bounded(EditorPreview.Input("xps-range-filter__input", "number"), minimum, maximum, step, minimum),
                EditorPreview.El("span", "xps-range-filter__separator", "–").Decorative(),
                EditorPreview.El("label", "xps-range-filter__input-label", Label(properties.ToLabel, WidgetResources.Preview_To)),
                Bounded(EditorPreview.Input("xps-range-filter__input", "number"), minimum, maximum, step, maximum));

        return string.IsNullOrWhiteSpace(properties.Unit)
            ? row
            : row.Add(EditorPreview.El("span", "xps-range-filter__unit", properties.Unit.Trim()));
    }

    private static string Label(string configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    private static string Display(decimal value) => value.ToString(CultureInfo.CurrentUICulture);

    // HTML numeric attributes are culture-invariant; only the visible line is formatted for the editor.
    private static TagBuilder Bounded(TagBuilder input, decimal minimum, decimal maximum, decimal step, decimal value) =>
        input
            .Attr("min", minimum.ToString(CultureInfo.InvariantCulture))
            .Attr("max", maximum.ToString(CultureInfo.InvariantCulture))
            .Attr("step", step.ToString(CultureInfo.InvariantCulture))
            .Attr("value", value.ToString(CultureInfo.InvariantCulture));
}

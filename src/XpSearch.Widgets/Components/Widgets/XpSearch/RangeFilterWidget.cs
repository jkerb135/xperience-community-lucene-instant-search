using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

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
        Order = OrderFirstWidgetProperty)]
    [FormComponentConfiguration(XpSearchConstants.NumericAttributeConfiguratorIdentifier, nameof(Index))]
    public string Attribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the heading shown above the control. Empty falls back to the attribute name.</summary>
    [TextInputComponent(Label = "Label", Order = OrderFirstWidgetProperty + 10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the lower end of the control.</summary>
    [DecimalNumberInputComponent(
        Label = "Minimum",
        ExplanationText = "The lowest value the control offers. The search response carries no statistics about the corpus, so both bounds have to be set here.",
        Order = OrderFirstWidgetProperty + 20)]
    public decimal? Minimum { get; set; }

    /// <summary>Gets or sets the upper end of the control.</summary>
    [DecimalNumberInputComponent(Label = "Maximum", Order = OrderFirstWidgetProperty + 30)]
    public decimal? Maximum { get; set; }

    /// <summary>Gets or sets the step of the sliders and the number inputs.</summary>
    [DecimalNumberInputComponent(Label = "Step", Order = OrderFirstWidgetProperty + 40)]
    public decimal? Step { get; set; } = 1m;

    /// <summary>Gets or sets the visible label of the lower number input. Empty leaves "From".</summary>
    [TextInputComponent(Label = "\"From\" label", Order = OrderFirstWidgetProperty + 50)]
    public string FromLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the visible label of the upper number input. Empty leaves "To".</summary>
    [TextInputComponent(Label = "\"To\" label", Order = OrderFirstWidgetProperty + 60)]
    public string ToLabel { get; set; } = string.Empty;
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
    }
}

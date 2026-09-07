using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>rangeFilter</c> widget - a numeric or date range.</summary>
public sealed record RangeFilterOptions : XpSearchMountOptions
{
    /// <summary>Gets the numeric or date index attribute the range narrows.</summary>
    public string? Attribute { get; init; }

    /// <summary>Gets the heading shown above the control.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the lower end of the control. Required: the response carries no corpus statistics.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Gets the upper end of the control. Required: the response carries no corpus statistics.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>Gets the step of the sliders and the number inputs.</summary>
    public decimal? Step { get; init; } = 1m;

    /// <summary>Gets the visible label of the lower number input. Empty leaves "From".</summary>
    public string? FromLabel { get; init; }

    /// <summary>Gets the visible label of the upper number input. Empty leaves "To".</summary>
    public string? ToLabel { get; init; }

    /// <summary>Gets the unit shown after the two number inputs, such as "USD" or "kg".</summary>
    public string? Unit { get; init; }
}

/// <summary><c>&lt;xps-range-filter /&gt;</c> - mounts the <c>rangeFilter</c> widget.</summary>
[HtmlTargetElement("xps-range-filter")]
public sealed class RangeFilterTagHelper : XpSearchMountTagHelper<RangeFilterOptions>
{
    /// <summary>Initializes a new instance of the <see cref="RangeFilterTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public RangeFilterTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Attribute"/>.</summary>
    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Minimum"/>.</summary>
    [HtmlAttributeName("minimum")]
    public decimal? Minimum { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Maximum"/>.</summary>
    [HtmlAttributeName("maximum")]
    public decimal? Maximum { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Step"/>.</summary>
    [HtmlAttributeName("step")]
    public decimal? Step { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.FromLabel"/>.</summary>
    [HtmlAttributeName("from-label")]
    public string? FromLabel { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.ToLabel"/>.</summary>
    [HtmlAttributeName("to-label")]
    public string? ToLabel { get; set; }

    /// <summary>Gets or sets <see cref="RangeFilterOptions.Unit"/>.</summary>
    [HtmlAttributeName("unit")]
    public string? Unit { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "rangeFilter";

    /// <inheritdoc />
    public override string? Validate(RangeFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? message = base.Validate(options);
        if (message is not null)
        {
            return message;
        }

        if (string.IsNullOrWhiteSpace(options.Attribute))
        {
            return WidgetResources.Hint_SelectAttribute;
        }

        // Without usable bounds the JavaScript widget renders a disabled control, which is worse than
        // saying what is missing.
        return options.Minimum is null || options.Maximum is null || options.Minimum >= options.Maximum
            ? WidgetResources.Hint_RangeBounds
            : null;
    }

    /// <inheritdoc />
    protected override RangeFilterOptions Merge(RangeFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Attribute = Attribute ?? options.Attribute,
            Label = Label ?? options.Label,
            Minimum = Minimum ?? options.Minimum,
            Maximum = Maximum ?? options.Maximum,
            Step = Step ?? options.Step,
            FromLabel = FromLabel ?? options.FromLabel,
            ToLabel = ToLabel ?? options.ToLabel,
            Unit = Unit ?? options.Unit
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(RangeFilterOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        config["attribute"] = options.Attribute!.Trim();
        config["min"] = options.Minimum;
        config["max"] = options.Maximum;

        if (options.Step is > 0)
        {
            config["step"] = options.Step;
        }

        if (!string.IsNullOrWhiteSpace(options.Label))
        {
            config["label"] = options.Label;
        }

        var labels = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.FromLabel))
        {
            labels["from"] = options.FromLabel;
        }

        if (!string.IsNullOrWhiteSpace(options.ToLabel))
        {
            labels["to"] = options.ToLabel;
        }

        if (labels.Count > 0)
        {
            config["labels"] = labels;
        }

        if (!string.IsNullOrWhiteSpace(options.Unit))
        {
            config["unit"] = options.Unit.Trim();
        }
    }
}

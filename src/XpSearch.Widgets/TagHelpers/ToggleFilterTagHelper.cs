using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>toggleFilter</c> widget - one checkbox for a single facet value.</summary>
public sealed record ToggleFilterOptions : XpSearchMountOptions
{
    /// <summary>Gets the index attribute the checkbox filters on.</summary>
    public string? Attribute { get; init; }

    /// <summary>Gets the single value the checkbox filters on.</summary>
    public string Value { get; init; } = "true";

    /// <summary>Gets the visible text. Empty leaves the label the server gave the value.</summary>
    public string? Label { get; init; }

    /// <summary>Gets whether the number of matching documents is shown beside the label.</summary>
    public bool ShowCount { get; init; } = true;
}

/// <summary><c>&lt;xps-toggle-filter /&gt;</c> - mounts the <c>toggleFilter</c> widget.</summary>
[HtmlTargetElement("xps-toggle-filter")]
public sealed class ToggleFilterTagHelper : XpSearchMountTagHelper<ToggleFilterOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ToggleFilterTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ToggleFilterTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ToggleFilterOptions.Attribute"/>.</summary>
    [HtmlAttributeName("attribute")]
    public string? Attribute { get; set; }

    /// <summary>Gets or sets <see cref="ToggleFilterOptions.Value"/>.</summary>
    [HtmlAttributeName("value")]
    public string? Value { get; set; }

    /// <summary>Gets or sets <see cref="ToggleFilterOptions.Label"/>.</summary>
    [HtmlAttributeName("label")]
    public string? Label { get; set; }

    /// <summary>Gets or sets <see cref="ToggleFilterOptions.ShowCount"/>.</summary>
    [HtmlAttributeName("show-count")]
    public bool? ShowCount { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "toggleFilter";

    /// <inheritdoc />
    public override string? Validate(ToggleFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return base.Validate(options)
            ?? (string.IsNullOrWhiteSpace(options.Attribute) ? WidgetResources.Hint_SelectAttribute : null);
    }

    /// <inheritdoc />
    protected override ToggleFilterOptions Merge(ToggleFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Attribute = Attribute ?? options.Attribute,
            Value = Value ?? options.Value,
            Label = Label ?? options.Label,
            ShowCount = ShowCount ?? options.ShowCount
        };
    }
}

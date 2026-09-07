using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Sorting;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>activeFilters</c> widget - the visitor's refinements as removable chips.</summary>
public sealed record ActiveFiltersOptions : XpSearchMountOptions
{
    /// <summary>Gets the heading screen readers announce for the chip list. It is never shown on screen.</summary>
    public string? Title { get; init; }

    /// <summary>Gets whether the chips stay on one scrolling row instead of wrapping.</summary>
    public bool Scroll { get; init; }

    /// <summary>
    /// Gets what each attribute is called on its chips, by attribute name. Only needed for an
    /// attribute whose own filtering widget is not on the page: the chips otherwise read the label
    /// that widget declares.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AttributeLabels { get; init; }
}

/// <summary><c>&lt;xps-active-filters /&gt;</c> - mounts the <c>activeFilters</c> widget.</summary>
[HtmlTargetElement("xps-active-filters")]
public sealed class ActiveFiltersTagHelper : XpSearchMountTagHelper<ActiveFiltersOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ActiveFiltersTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ActiveFiltersTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Title"/>.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets <see cref="ActiveFiltersOptions.Scroll"/>.</summary>
    [HtmlAttributeName("scroll")]
    public bool? Scroll { get; set; }

    /// <summary>
    /// Gets or sets <see cref="ActiveFiltersOptions.AttributeLabels"/>, either as a dictionary
    /// (<c>attribute-labels="@labels"</c>) or as the <c>attribute;Label</c> lines the Page Builder
    /// takes (<c>attribute-labels="contentType;Content type"</c>).
    /// </summary>
    [HtmlAttributeName("attribute-labels")]
    public object? AttributeLabels { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "activeFilters";

    /// <inheritdoc />
    protected override ActiveFiltersOptions Merge(ActiveFiltersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Title = Title ?? options.Title,
            Scroll = Scroll ?? options.Scroll,
            AttributeLabels = Labels(AttributeLabels) ?? options.AttributeLabels
        };
    }

    /// <summary>
    /// Parses the <c>attribute;Label</c> lines an editor types into a label per attribute, through the
    /// same parser the filter and sort sheet's facet lines use. Empty text is no labels at all.
    /// </summary>
    /// <param name="text">The raw text, one <c>attribute;Label</c> per line.</param>
    /// <returns>The labels by attribute, or <see langword="null"/> when there are none.</returns>
    public static IReadOnlyDictionary<string, string>? ParseAttributeLabels(string? text)
    {
        var parsed = SortOptionsValidation.Parse(text);

        return parsed.Count == 0
            ? null
            : parsed.ToDictionary(entry => entry.Value, entry => entry.Label, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads the <c>attribute-labels</c> attribute in either of its two forms. One attribute rather
    /// than two: the labels are one option, so a Razor developer has one name to remember whether the
    /// value is a dictionary or the same <c>attribute;Label</c> text an editor types.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? Labels(object? value) => value switch
    {
        null => null,
        IReadOnlyDictionary<string, string> labels => labels,
        IDictionary<string, string> labels => labels.AsReadOnly(),
        string text => ParseAttributeLabels(text),
        _ => throw new InvalidOperationException(
            "attribute-labels takes a dictionary of attribute names to labels, or one attribute;Label per line.")
    };
}

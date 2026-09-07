using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>resultStats</c> widget - the "N results for …" line.</summary>
public sealed record ResultStatsOptions : XpSearchMountOptions
{
    /// <summary>The wording a widget shows out of the box, matching the shipped design.</summary>
    public const string DefaultTextTemplate = "{total} results for “{query}” ({tookMs} ms)";

    /// <summary>
    /// Gets the wording of the result line. Placeholders: <c>{total}</c>, <c>{tookMs}</c>,
    /// <c>{query}</c>, <c>{page}</c>, <c>{totalPages}</c>. Empty falls back to the JavaScript's text.
    /// </summary>
    public string TextTemplate { get; init; } = DefaultTextTemplate;

    /// <summary>Gets the text shown before the first search runs.</summary>
    public string? EmptyText { get; init; }
}

/// <summary><c>&lt;xps-result-stats /&gt;</c> - mounts the <c>resultStats</c> widget.</summary>
[HtmlTargetElement("xps-result-stats")]
public sealed class ResultStatsTagHelper : XpSearchMountTagHelper<ResultStatsOptions>
{
    /// <summary>Initializes a new instance of the <see cref="ResultStatsTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ResultStatsTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="ResultStatsOptions.TextTemplate"/>.</summary>
    [HtmlAttributeName("text-template")]
    public string? TextTemplate { get; set; }

    /// <summary>Gets or sets <see cref="ResultStatsOptions.EmptyText"/>.</summary>
    [HtmlAttributeName("empty-text")]
    public string? EmptyText { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "resultStats";

    /// <inheritdoc />
    protected override ResultStatsOptions Merge(ResultStatsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            TextTemplate = TextTemplate ?? options.TextTemplate,
            EmptyText = EmptyText ?? options.EmptyText
        };
    }
}

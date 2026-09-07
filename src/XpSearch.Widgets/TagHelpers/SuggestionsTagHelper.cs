using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>suggestions</c> widget - type-ahead suggestions as a combobox.</summary>
public sealed record SuggestionsOptions : XpSearchMountOptions
{
    /// <summary>The <see cref="Mode"/> value that suggests matching documents.</summary>
    public const string ModeDocuments = "documents";

    /// <summary>The <see cref="Mode"/> value that suggests previously popular queries.</summary>
    public const string ModeQuerySuggestions = "querySuggestions";

    /// <summary>The <see cref="Mode"/> value that suggests both, queries first (SG-1).</summary>
    public const string ModeMixed = "mixed";

    /// <summary>What a widget that asked for none is given: five suggestions.</summary>
    public const int DefaultLimit = 5;

    /// <summary>
    /// Gets what the suggestions are drawn from. What an index actually answers with is configured in
    /// code, per index; this records the intent and does not change the request.
    /// </summary>
    public string Mode { get; init; } = ModeDocuments;

    /// <summary>Gets how many suggestions are offered. Capped by the index's maximum suggestion count.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Gets whether the panel offers this visitor's own recent searches.</summary>
    public bool RecentSearches { get; init; } = true;
}

/// <summary><c>&lt;xps-suggestions /&gt;</c> - mounts the <c>suggestions</c> widget.</summary>
[HtmlTargetElement("xps-suggestions")]
public sealed class SuggestionsTagHelper : XpSearchMountTagHelper<SuggestionsOptions>
{
    /// <summary>Initializes a new instance of the <see cref="SuggestionsTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public SuggestionsTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="SuggestionsOptions.Mode"/>.</summary>
    [HtmlAttributeName("mode")]
    public string? Mode { get; set; }

    /// <summary>Gets or sets <see cref="SuggestionsOptions.Limit"/>.</summary>
    [HtmlAttributeName("limit")]
    public int? Limit { get; set; }

    /// <summary>Gets or sets <see cref="SuggestionsOptions.RecentSearches"/>.</summary>
    [HtmlAttributeName("recent-searches")]
    public bool? RecentSearches { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "suggestions";

    /// <inheritdoc />
    protected override SuggestionsOptions Merge(SuggestionsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Mode = Mode ?? options.Mode,
            Limit = Limit ?? options.Limit,
            RecentSearches = RecentSearches ?? options.RecentSearches
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(SuggestionsOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        // Which of the two an index answers with is server-side configuration, so "mode" documents
        // the intent for the index; it does not change the request the widget sends.
        config["mode"] = string.IsNullOrWhiteSpace(options.Mode) ? SuggestionsOptions.ModeDocuments : options.Mode;
        // "limit" is what POST /api/xpsearch/suggest calls it. Zero is a validation error on the wire.
        config["limit"] = options.Limit > 0 ? options.Limit : SuggestionsOptions.DefaultLimit;
        config["recentSearches"] = options.RecentSearches;
    }
}

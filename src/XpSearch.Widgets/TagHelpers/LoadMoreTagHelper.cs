using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>
/// Options of the <c>loadMore</c> widget - the result list that appends the next page instead of
/// replacing it.
/// </summary>
public sealed record LoadMoreOptions : XpSearchMountOptions
{
    /// <summary>Gets whether the next page loads when the end of the list scrolls into view.</summary>
    public bool AutoLoad { get; init; } = true;

    /// <summary>Gets the attribute the default card reads the title from. Empty keeps <c>title</c>.</summary>
    public string? TitleAttribute { get; init; }

    /// <summary>Gets the attribute the default card links to. Empty keeps <c>url</c>.</summary>
    public string? UrlAttribute { get; init; }

    /// <summary>Gets the attributes tried, in order, for the snippet. Empty keeps summary, content, excerpt.</summary>
    public IReadOnlyList<string> SnippetAttributes { get; init; } = [];

    /// <summary>Gets the button text while there is more to load. Empty keeps "Load more results".</summary>
    public string? MoreLabel { get; init; }

    /// <summary>Gets the button text once everything is loaded. Empty keeps "No more results".</summary>
    public string? ExhaustedLabel { get; init; }
}

/// <summary><c>&lt;xps-load-more /&gt;</c> - mounts the <c>loadMore</c> widget.</summary>
/// <remarks>
/// It renders the results itself and owns the page, so it stands in for <c>&lt;xps-results&gt;</c>
/// and <c>&lt;xps-pagination&gt;</c> rather than joining them (MB-1).
/// </remarks>
[HtmlTargetElement("xps-load-more")]
public sealed class LoadMoreTagHelper : XpSearchMountTagHelper<LoadMoreOptions>
{
    /// <summary>Initializes a new instance of the <see cref="LoadMoreTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public LoadMoreTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.AutoLoad"/>.</summary>
    [HtmlAttributeName("auto-load")]
    public bool? AutoLoad { get; set; }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.TitleAttribute"/>.</summary>
    [HtmlAttributeName("title-attribute")]
    public string? TitleAttribute { get; set; }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.UrlAttribute"/>.</summary>
    [HtmlAttributeName("url-attribute")]
    public string? UrlAttribute { get; set; }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.SnippetAttributes"/>.</summary>
    [HtmlAttributeName("snippet-attributes")]
    public IEnumerable<string>? SnippetAttributes { get; set; }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.MoreLabel"/>.</summary>
    [HtmlAttributeName("more-label")]
    public string? MoreLabel { get; set; }

    /// <summary>Gets or sets <see cref="LoadMoreOptions.ExhaustedLabel"/>.</summary>
    [HtmlAttributeName("exhausted-label")]
    public string? ExhaustedLabel { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "loadMore";

    /// <inheritdoc />
    protected override LoadMoreOptions Merge(LoadMoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            AutoLoad = AutoLoad ?? options.AutoLoad,
            TitleAttribute = TitleAttribute ?? options.TitleAttribute,
            UrlAttribute = UrlAttribute ?? options.UrlAttribute,
            SnippetAttributes = SnippetAttributes?.ToList() ?? options.SnippetAttributes,
            MoreLabel = MoreLabel ?? options.MoreLabel,
            ExhaustedLabel = ExhaustedLabel ?? options.ExhaustedLabel
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(LoadMoreOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        // Scrolling loads the next page out of the box, so only the opt-out is worth emitting.
        if (!options.AutoLoad)
        {
            config["autoLoad"] = false;
        }

        if (!string.IsNullOrWhiteSpace(options.TitleAttribute))
        {
            config["titleAttribute"] = options.TitleAttribute.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.UrlAttribute))
        {
            config["urlAttribute"] = options.UrlAttribute.Trim();
        }

        if (options.SnippetAttributes.Count > 0)
        {
            config["snippetAttributes"] = options.SnippetAttributes;
        }

        var labels = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(options.MoreLabel))
        {
            labels["more"] = options.MoreLabel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ExhaustedLabel))
        {
            labels["exhausted"] = options.ExhaustedLabel.Trim();
        }

        if (labels.Count > 0)
        {
            config["labels"] = labels;
        }
    }
}

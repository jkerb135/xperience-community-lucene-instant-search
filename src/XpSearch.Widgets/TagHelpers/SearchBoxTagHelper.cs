using Microsoft.AspNetCore.Razor.TagHelpers;

using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>Options of the <c>searchBox</c> widget - the query input of a search.</summary>
public sealed record SearchBoxOptions : XpSearchMountOptions
{
    /// <summary>Gets the placeholder text. Empty keeps the JavaScript default.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets whether the clear button is offered once the visitor has typed.</summary>
    public bool ShowReset { get; init; } = true;

    /// <summary>Gets whether the input takes focus on page load.</summary>
    public bool Autofocus { get; init; }

    /// <summary>Gets whether the input offers type-ahead suggestions as a combobox.</summary>
    public bool EnableSuggestions { get; init; }

    /// <summary>Gets how many suggestions are offered. Only used when suggestions are on.</summary>
    public int SuggestionLimit { get; init; } = 5;

    /// <summary>Gets whether the panel offers this visitor's own recent searches.</summary>
    public bool RecentSearches { get; init; } = true;

    /// <summary>Gets whether the search keeps its state in the page URL (spec §5.5). An instance-wide option.</summary>
    public bool SyncStateToUrl { get; init; } = true;
}

/// <summary><c>&lt;xps-search-box /&gt;</c> - mounts the <c>searchBox</c> widget.</summary>
[HtmlTargetElement("xps-search-box")]
public sealed class SearchBoxTagHelper : XpSearchMountTagHelper<SearchBoxOptions>
{
    /// <summary>Initializes a new instance of the <see cref="SearchBoxTagHelper"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public SearchBoxTagHelper(IXpSearchMountRenderer renderer, IXpSearchIndexCatalog indexCatalog)
        : base(renderer, indexCatalog)
    {
    }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.Placeholder"/>.</summary>
    [HtmlAttributeName("placeholder")]
    public string? Placeholder { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.ShowReset"/>.</summary>
    [HtmlAttributeName("show-reset")]
    public bool? ShowReset { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.Autofocus"/>.</summary>
    [HtmlAttributeName("autofocus")]
    public bool? Autofocus { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.EnableSuggestions"/>.</summary>
    [HtmlAttributeName("enable-suggestions")]
    public bool? EnableSuggestions { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.SuggestionLimit"/>.</summary>
    [HtmlAttributeName("suggestion-limit")]
    public int? SuggestionLimit { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.RecentSearches"/>.</summary>
    [HtmlAttributeName("recent-searches")]
    public bool? RecentSearches { get; set; }

    /// <summary>Gets or sets <see cref="SearchBoxOptions.SyncStateToUrl"/>.</summary>
    [HtmlAttributeName("sync-state-to-url")]
    public bool? SyncStateToUrl { get; set; }

    /// <inheritdoc />
    protected override string WidgetType => "searchBox";

    /// <inheritdoc />
    protected override SearchBoxOptions Merge(SearchBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options with
        {
            Placeholder = Placeholder ?? options.Placeholder,
            ShowReset = ShowReset ?? options.ShowReset,
            Autofocus = Autofocus ?? options.Autofocus,
            EnableSuggestions = EnableSuggestions ?? options.EnableSuggestions,
            SuggestionLimit = SuggestionLimit ?? options.SuggestionLimit,
            RecentSearches = RecentSearches ?? options.RecentSearches,
            SyncStateToUrl = SyncStateToUrl ?? options.SyncStateToUrl
        };
    }

    /// <inheritdoc />
    protected override void BuildConfig(SearchBoxOptions options, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);

        ReflectConfig(options, config);
        // URL syncing is a property of the search, not an option of the input; it goes to the instance.
        config.Remove("syncStateToUrl");

        // The JavaScript reads one nested group: present means on, absent means off.
        config.Remove("enableSuggestions");
        config.Remove("suggestionLimit");
        config.Remove("recentSearches");

        if (options.EnableSuggestions)
        {
            var suggestions = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (options.SuggestionLimit > 0)
            {
                suggestions["limit"] = options.SuggestionLimit;
            }

            // Recents are on by default in the JavaScript, so only the opt-out has to be said.
            if (!options.RecentSearches)
            {
                suggestions["recentSearches"] = false;
            }

            config["suggestions"] = suggestions;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Emitted whether on or off: an explicit <c>false</c> in the markup is how a page with a second,
    /// deliberately non-syncing search reads as configured rather than forgotten.
    /// </remarks>
    protected override void BuildInstanceConfig(SearchBoxOptions options, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(instanceConfig);

        instanceConfig["routing"] = options.SyncStateToUrl;
    }
}

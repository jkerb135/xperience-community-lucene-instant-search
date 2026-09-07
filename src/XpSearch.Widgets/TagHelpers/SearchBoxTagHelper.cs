using Microsoft.AspNetCore.Html;
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
    /// <summary>The magnifier inside the field, byte-identical to the client's <c>SEARCH_ICON</c>.</summary>
    private const string SearchIcon = "<svg class=\"xps-search-box__icon\" viewBox=\"0 0 24 24\" fill=\"none\""
        + " stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\""
        + " aria-hidden=\"true\" focusable=\"false\"><circle cx=\"11\" cy=\"11\" r=\"7\"></circle>"
        + "<path d=\"m20 20-3.6-3.6\"></path></svg>";

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

    /// <inheritdoc />
    /// <remarks>
    /// The real form, not a skeleton (SK-1 §2.3): without JavaScript, submitting it reloads the page
    /// with <c>?q=</c>, which <c>ServerRenderedResults</c> already honours. Same markup as the
    /// client's first render - element ids follow its <c>widgetId</c> rule
    /// (<c>xps-{instance}-search-box-{part}</c>) - so the handover moves nothing on screen; the two
    /// deliberate additions are <c>method="get"</c> and the value the visitor arrived with.
    /// </remarks>
    protected override Task<IHtmlContent?> BuildContentAsync(
        SearchBoxOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        string query = ViewContext?.HttpContext?.Request.Query["q"].ToString() ?? string.Empty;
        string id = $"xps-{CurrentInstanceId}-search-box";
        string placeholder = string.IsNullOrWhiteSpace(options.Placeholder) ? "Search…" : options.Placeholder;

        var form = new HtmlContentBuilder()
            .AppendHtml("<form data-xps-server-rendered class=\"xps xps-search-box\" role=\"search\" method=\"get\" novalidate>")
            .AppendHtml("<label class=\"xps-search-box__label xps-sr-only\" for=\"").Append(id).AppendHtml("-input\">Search this site</label>")
            .AppendHtml("<div class=\"xps-search-box__field\">")
            .AppendHtml(SearchIcon)
            .AppendHtml("<input class=\"xps-search-box__input\" id=\"").Append(id).AppendHtml("-input\" type=\"search\" name=\"q\" value=\"")
            .Append(query)
            .AppendHtml("\" placeholder=\"")
            .Append(placeholder)
            .AppendHtml("\" autocomplete=\"off\" autocapitalize=\"off\" autocorrect=\"off\" spellcheck=\"false\"");

        if (options.EnableSuggestions)
        {
            form.AppendHtml(" role=\"combobox\" aria-expanded=\"false\" aria-controls=\"")
                .Append(id)
                .AppendHtml("-listbox\" aria-autocomplete=\"list\"");
        }

        form.AppendHtml("><span class=\"xps-search-box__loading xps-skeleton\" aria-hidden=\"true\"></span>")
            .AppendHtml("<button class=\"xps-button xps-search-box__reset\" type=\"reset\" aria-label=\"Clear the search query\"")
            // The client hides it while there is nothing to clear; hidden markup is not focusable.
            .AppendHtml(options.ShowReset && query.Length > 0 ? ">" : " hidden>")
            .AppendHtml("<span aria-hidden=\"true\">&times;</span></button></div>");

        if (options.EnableSuggestions)
        {
            form.AppendHtml("<div class=\"xps-suggestions__panel\" hidden></div>");
        }

        return Task.FromResult<IHtmlContent?>(form.AppendHtml("</form>"));
    }
}

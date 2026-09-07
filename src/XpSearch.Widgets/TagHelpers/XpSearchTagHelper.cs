using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XpSearch.Widgets.TagHelpers;

/// <summary>
/// What an enclosing <c>&lt;xps-search&gt;</c> tells the mounts inside it (RZ-1 §1.3). Published
/// through <see cref="TagHelperContext.Items"/>; every mount still writes its own
/// <c>data-xps-instance-config</c>, so the mount contract is unchanged.
/// </summary>
public sealed class XpSearchScope
{
    /// <summary>Gets the index the widgets inside the element search.</summary>
    public string? Index { get; init; }

    /// <summary>Gets the search instance the widgets inside the element join.</summary>
    public string? Instance { get; init; }

    /// <summary>Gets whether the search keeps its state in the page URL (spec §5.5).</summary>
    public bool? Routing { get; init; }

    /// <summary>Gets how many results one page holds.</summary>
    public int? PageSize { get; init; }

    /// <summary>Gets the index fields the search retrieves for each result.</summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>Gets whether the search runs once as the page loads, before the visitor has typed.</summary>
    public bool? SearchOnInitialLoad { get; init; }

    /// <summary>Reads the scope a parent <c>&lt;xps-search&gt;</c> published, or <see langword="null"/>.</summary>
    /// <param name="context">The tag helper context of the mount.</param>
    /// <returns>The scope, or <see langword="null"/> outside one.</returns>
    internal static XpSearchScope? From(TagHelperContext context) =>
        context.Items.TryGetValue(typeof(XpSearchScope), out object? scope) ? scope as XpSearchScope : null;

    /// <summary>
    /// Adds the instance-wide options to a mount's instance config, never overwriting what the widget
    /// itself said: a Results widget's own page size beats the scope's.
    /// </summary>
    /// <param name="instanceConfig">The instance config of a mount inside this scope.</param>
    internal void ApplyTo(IDictionary<string, object?> instanceConfig)
    {
        if (Routing is not null && !instanceConfig.ContainsKey("routing"))
        {
            instanceConfig["routing"] = Routing;
        }

        if (PageSize is > 0 && !instanceConfig.ContainsKey("initialState"))
        {
            instanceConfig["initialState"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["pageSize"] = PageSize };
        }

        if (Fields is { Count: > 0 } && !instanceConfig.ContainsKey("fields"))
        {
            instanceConfig["fields"] = Fields;
        }

        if (SearchOnInitialLoad is not null && !instanceConfig.ContainsKey("searchOnInitialLoad"))
        {
            instanceConfig["searchOnInitialLoad"] = SearchOnInitialLoad;
        }
    }
}

/// <summary>
/// <c>&lt;xps-search index="…"&gt;</c> - the index, the instance id and the instance-wide search
/// options for every Xperience Search tag inside it. Renders no element of its own.
/// </summary>
[HtmlTargetElement("xps-search")]
public sealed class XpSearchTagHelper : TagHelper
{
    /// <summary>Gets or sets the code name of the index the widgets inside search.</summary>
    [HtmlAttributeName("index")]
    public string? Index { get; set; }

    /// <summary>Gets or sets the search instance the widgets inside join. Defaults to <c>default</c>.</summary>
    [HtmlAttributeName("instance")]
    public string? Instance { get; set; }

    /// <summary>Gets or sets whether the search keeps its query, filters and page in the address bar.</summary>
    [HtmlAttributeName("routing")]
    public bool? Routing { get; set; }

    /// <summary>Gets or sets how many results one page holds. A Results tag's own value wins.</summary>
    [HtmlAttributeName("page-size")]
    public int? PageSize { get; set; }

    /// <summary>Gets or sets the index fields the search retrieves for each result. A Results tag's own list wins.</summary>
    [HtmlAttributeName("fields")]
    public IEnumerable<string>? Fields { get; set; }

    /// <summary>
    /// Gets or sets whether the search runs once as the page loads. Unset keeps the JavaScript
    /// default, <see langword="true"/>; a search box's own value wins.
    /// </summary>
    [HtmlAttributeName("search-on-initial-load")]
    public bool? SearchOnInitialLoad { get; set; }

    /// <inheritdoc />
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        // Set before the children run: that is how a descendant tag helper reads it.
        context.Items[typeof(XpSearchScope)] = new XpSearchScope
        {
            Index = Index,
            Instance = Instance,
            Routing = Routing,
            PageSize = PageSize,
            Fields = Fields?.ToList(),
            SearchOnInitialLoad = SearchOnInitialLoad
        };

        output.TagName = null;
    }
}

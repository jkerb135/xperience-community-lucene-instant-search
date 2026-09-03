using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.SearchBoxIdentifier,
    viewComponentType: typeof(SearchBoxWidgetViewComponent),
    name: "Search - Search box",
    propertiesType: typeof(SearchBoxWidgetProperties),
    Description = "The query input of a search. Every other search widget on the page reacts to it.",
    IconClass = "icon-magnifier",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the search box widget (spec §7.3).</summary>
public sealed class SearchBoxWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>Gets or sets the placeholder text. Empty keeps the JavaScript default.</summary>
    [TextInputComponent(
        Label = "Placeholder",
        Tooltip = "The grey hint shown in the empty input.",
        ExplanationText = "Leave it empty to keep the built-in wording. It is a hint, not a label: screen readers still announce the field as \"Search\".",
        Order = OrderFirstWidgetProperty)]
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the clear button is offered once the visitor has typed.</summary>
    [CheckBoxComponent(
        Label = "Show reset button",
        Tooltip = "Offers a button that empties the field.",
        ExplanationText = "The button appears once the visitor has typed. Clearing the field runs an empty search, so the Results widget goes back to showing everything.",
        Order = OrderFirstWidgetProperty + 10)]
    public bool ShowReset { get; set; } = true;

    /// <summary>Gets or sets whether the input takes focus on page load.</summary>
    [CheckBoxComponent(
        Label = "Focus on page load",
        Tooltip = "Puts the cursor in the input as soon as the page opens.",
        ExplanationText = "Use on a dedicated search page only; stealing focus is disorienting elsewhere. Only one widget on a page should take focus.",
        Order = OrderFirstWidgetProperty + 20)]
    public bool Autofocus { get; set; }

    /// <summary>Gets or sets whether the input offers type-ahead suggestions as a combobox.</summary>
    [CheckBoxComponent(
        Label = "Suggest as the visitor types",
        Tooltip = "Drops a suggestion panel under this input.",
        ExplanationText = "Turns this input into an autocomplete combobox. Picking a suggestion searches on this page. "
            + "Do not add the separate Suggestions widget as well: that one renders a second search field.",
        Order = OrderFirstWidgetProperty + 25)]
    public bool EnableSuggestions { get; set; }

    /// <summary>Gets or sets how many suggestions are offered. Only used when suggestions are on.</summary>
    [NumberInputComponent(
        Label = "Maximum suggestions",
        Tooltip = "How many suggestions the panel offers.",
        ExplanationText = "The index's Search settings cap it: a value above 'Maximum suggestion count' comes back trimmed to that. 0 asks for the built-in 5 rather than the index's 'Default suggestion count', which only applies to callers that send no number at all.",
        Order = OrderFirstWidgetProperty + 27)]
    [VisibleIfTrue(nameof(EnableSuggestions))]
    public int SuggestionLimit { get; set; } = 5;

    /// <summary>Gets or sets whether the panel offers this visitor's own recent searches.</summary>
    [CheckBoxComponent(
        Label = "Offer recent searches",
        Tooltip = "Adds this visitor's own earlier searches to the panel.",
        ExplanationText = "Shows what this visitor searched for before as the first group of the panel, and opens it when they "
            + "focus the empty field. The list is kept in their own browser and never sent to the server. Clear the checkbox on a shared or kiosk device.",
        Order = OrderFirstWidgetProperty + 28)]
    [VisibleIfTrue(nameof(EnableSuggestions))]
    public bool RecentSearches { get; set; } = true;

    /// <summary>Gets or sets whether the search keeps its state in the page URL (spec §5.5).</summary>
    [CheckBoxComponent(
        Label = "Sync search state to the URL",
        Tooltip = "Writes the query, filters and page into the address bar.",
        ExplanationText = "Keeps the query, filters and page in the address bar, so a result page can be shared and the back button works. "
            + "Turn it off for a secondary search embedded on a content page: at most one search instance per page may sync, because all of them would write the same parameters.",
        Order = OrderFirstWidgetProperty + 30)]
    public bool SyncStateToUrl { get; set; } = true;
}

/// <summary>Renders the <c>searchBox</c> mount.</summary>
public sealed class SearchBoxWidgetViewComponent : XpSearchMountWidgetViewComponent<SearchBoxWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="SearchBoxWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public SearchBoxWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "searchBox";

    /// <inheritdoc />
    protected override void BuildConfig(SearchBoxWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        ReflectConfig(properties, config);
        // URL syncing is a property of the search, not an option of the input; it goes to the instance.
        config.Remove("syncStateToUrl");

        // The JavaScript reads one nested group: present means on, absent means off.
        config.Remove("enableSuggestions");
        config.Remove("suggestionLimit");
        config.Remove("recentSearches");

        if (properties.EnableSuggestions)
        {
            var suggestions = new Dictionary<string, object?>();

            if (properties.SuggestionLimit > 0)
            {
                suggestions["limit"] = properties.SuggestionLimit;
            }

            // Recents are on by default in the JavaScript, so only the opt-out has to be said.
            if (!properties.RecentSearches)
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
    protected override void BuildInstanceConfig(SearchBoxWidgetProperties properties, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(instanceConfig);

        instanceConfig["routing"] = properties.SyncStateToUrl;
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(SearchBoxWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var field = EditorPreview.El("div", "xps-search-box__field")
            .Add(Magnifier())
            .Add(EditorPreview.Input("xps-search-box__input", "search")
                .Attr(
                    "placeholder",
                    string.IsNullOrWhiteSpace(properties.Placeholder)
                        ? WidgetResources.Preview_SearchPlaceholder
                        : properties.Placeholder));

        if (properties.ShowReset)
        {
            field.Add(EditorPreview.Button("xps-button xps-search-box__reset", "×", glyph: true));
        }

        field.Add(EditorPreview.Button("xps-button xps-search-box__submit", "→", glyph: true));

        return EditorPreview.El("form", "xps-search-box").Add(field);
    }

    /// <summary>Builds the decorative magnifier the live widget renders inside the field.</summary>
    private static TagBuilder Magnifier() =>
        EditorPreview.El("svg", "xps-search-box__icon")
            .Attr("viewBox", "0 0 24 24")
            .Attr("fill", "none")
            .Attr("stroke", "currentColor")
            .Attr("stroke-width", "1.5")
            .Decorative()
            .Add(
                EditorPreview.El("circle").Attr("cx", "11").Attr("cy", "11").Attr("r", "7"),
                EditorPreview.El("path").Attr("d", "m20 20-3.6-3.6"));
}

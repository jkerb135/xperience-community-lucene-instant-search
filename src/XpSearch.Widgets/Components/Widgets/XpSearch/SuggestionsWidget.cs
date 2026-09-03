using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.SuggestionsIdentifier,
    viewComponentType: typeof(SuggestionsWidgetViewComponent),
    name: "Search - Suggestions",
    propertiesType: typeof(SuggestionsWidgetProperties),
    Description = "Type-ahead suggestions under the search box, as a WAI-ARIA combobox.",
    IconClass = "icon-light-bulb",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the suggestions widget (spec §7.3).</summary>
public sealed class SuggestionsWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>The <see cref="Mode"/> value that suggests matching documents.</summary>
    public const string ModeDocuments = "documents";

    /// <summary>The <see cref="Mode"/> value that suggests previously popular queries.</summary>
    public const string ModeQuerySuggestions = "querySuggestions";

    /// <summary>The <see cref="Mode"/> value that suggests both, queries first (SG-1).</summary>
    public const string ModeMixed = "mixed";

    /// <summary>Gets or sets what the suggestions are drawn from.</summary>
    [DropDownComponent(
        Label = "Mode",
        Options = $"{ModeDocuments};Matching documents\r\n{ModeQuerySuggestions};Popular queries\r\n{ModeMixed};Both, queries first",
        Tooltip = "What the suggestions are drawn from.",
        ExplanationText = "What an index actually answers with is configured in code, per index; this property records the intent and does not change the request. Popular queries come from the query log within the index's 'Query suggestion window (days)'.",
        Order = OrderFirstWidgetProperty)]
    public string Mode { get; set; } = ModeDocuments;

    /// <summary>Gets or sets how many suggestions are offered.</summary>
    [NumberInputComponent(
        Label = "Maximum items",
        Tooltip = "How many suggestions the panel offers.",
        ExplanationText = "The index's 'Maximum suggestion count' caps it: a higher number comes back trimmed to that.",
        Order = OrderFirstWidgetProperty + 10)]
    public int MaxItems { get; set; } = 5;

    /// <summary>Gets or sets whether the panel offers this visitor's own recent searches.</summary>
    [CheckBoxComponent(
        Label = "Offer recent searches",
        Tooltip = "Adds this visitor's own earlier searches to the panel.",
        ExplanationText = "Shows what this visitor searched for before as the first group of the panel, and opens it when they "
            + "focus the empty field. The list is kept in their own browser and never sent to the server. Clear the checkbox on a shared or kiosk device.",
        Order = OrderFirstWidgetProperty + 20)]
    public bool RecentSearches { get; set; } = true;
}

/// <summary>Renders the <c>suggestions</c> mount.</summary>
public sealed class SuggestionsWidgetViewComponent : XpSearchMountWidgetViewComponent<SuggestionsWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="SuggestionsWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public SuggestionsWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "suggestions";

    /// <inheritdoc />
    protected override void BuildConfig(SuggestionsWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        // Which of the two an index answers with is server-side configuration, so "mode" documents
        // the editor's intent for the index; it does not change the request the widget sends.
        config["mode"] = string.IsNullOrWhiteSpace(properties.Mode) ? SuggestionsWidgetProperties.ModeDocuments : properties.Mode;
        // "limit" is what POST /api/xpsearch/suggest calls it.
        config["limit"] = properties.MaxItems;
        config["recentSearches"] = properties.RecentSearches;
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(SuggestionsWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var field = EditorPreview.El("div", "xps-suggestions__field")
            .Add(
                EditorPreview.Input("xps-suggestions__input", "text")
                    .Attr("placeholder", WidgetResources.Preview_SearchPlaceholder),
                EditorPreview.Button("xps-button xps-suggestions__reset", "×", glyph: true));

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("div", "xps-suggestions")
                .Add(EditorPreview.El("form", "xps-suggestions__form").Add(field)))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Suggestions,
                string.IsNullOrWhiteSpace(properties.Mode) ? SuggestionsWidgetProperties.ModeDocuments : properties.Mode,
                properties.MaxItems.ToString(CultureInfo.CurrentUICulture))));
    }
}

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

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

    /// <summary>Gets or sets what the suggestions are drawn from.</summary>
    [DropDownComponent(
        Label = "Mode",
        Options = $"{ModeDocuments};Matching documents\r\n{ModeQuerySuggestions};Popular queries",
        Order = OrderFirstWidgetProperty)]
    public string Mode { get; set; } = ModeDocuments;

    /// <summary>Gets or sets how many suggestions are offered.</summary>
    [NumberInputComponent(Label = "Maximum items", Order = OrderFirstWidgetProperty + 10)]
    public int MaxItems { get; set; } = 5;
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
    }
}

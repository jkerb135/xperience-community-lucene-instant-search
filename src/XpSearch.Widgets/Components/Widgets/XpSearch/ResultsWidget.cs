using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ResultsIdentifier,
    viewComponentType: typeof(ResultsWidgetViewComponent),
    name: "Search - Results",
    propertiesType: typeof(ResultsWidgetProperties),
    Description = "The result list of a search.",
    IconClass = "icon-list",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the results widget (spec §7.3).</summary>
public sealed class ResultsWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>Gets or sets how many results a page shows. Zero keeps the API's configured default.</summary>
    [NumberInputComponent(
        Label = "Results per page",
        ExplanationText = "Leave at 0 to use the page size configured for the index.",
        Order = OrderFirstWidgetProperty)]
    public int ResultsPerPage { get; set; }

    /// <summary>Gets or sets the identifier of a registered result template (spec §5.8).</summary>
    [DropDownComponent(
        Label = "Result template",
        Placeholder = "Default template",
        DataProviderType = typeof(ResultTemplateOptionsProvider),
        Order = OrderFirstWidgetProperty + 10)]
    public string ResultTemplate { get; set; } = string.Empty;

    /// <summary>Gets or sets the document fields to retrieve, one per line. Empty retrieves the index defaults.</summary>
    [TextAreaComponent(
        Label = "Fields to show",
        ExplanationText = "One index field name per line, for example title, url, summary, image. Leave empty for the defaults.",
        Order = OrderFirstWidgetProperty + 20)]
    public string Fields { get; set; } = string.Empty;
}

/// <summary>Renders the <c>results</c> mount.</summary>
public sealed class ResultsWidgetViewComponent : XpSearchMountWidgetViewComponent<ResultsWidgetProperties>
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>Initializes a new instance of the <see cref="ResultsWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ResultsWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "results";

    /// <inheritdoc />
    protected override void BuildConfig(ResultsWidgetProperties properties, IDictionary<string, object?> config)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(properties.ResultTemplate))
        {
            config["template"] = properties.ResultTemplate.Trim();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Page size and retrieved fields are properties of the search, not of the list that displays it,
    /// so they belong in the instance options the bootstrap passes to <c>createSearch()</c>.
    /// </remarks>
    protected override void BuildInstanceConfig(ResultsWidgetProperties properties, IDictionary<string, object?> instanceConfig)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(instanceConfig);

        if (properties.ResultsPerPage > 0)
        {
            instanceConfig["initialState"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pageSize"] = properties.ResultsPerPage
            };
        }

        var fields = ParseLines(properties.Fields);
        if (fields.Count > 0)
        {
            instanceConfig["fields"] = fields;
        }
    }

    private static IReadOnlyList<string> ParseLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

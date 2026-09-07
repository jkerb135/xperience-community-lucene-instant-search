using System.Globalization;

using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Core.Rendering;
using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.TagHelpers;

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
    /// <summary>Gets or sets how many results a page shows. Always set (AR-3): the widget owns its size.</summary>
    [RequiredValidationRule]
    [MinimumIntegerValueValidationRule(1)]
    [NumberInputComponent(
        Label = "Results per page",
        Tooltip = "How many results one page of the list shows.",
        ExplanationText = "Results on each page of this widget. Capped by the index's 'Maximum page size'. It is the page size of the whole search instance, so the Search - Pagination widget beside it pages in these steps.",
        Order = OrderFirstWidgetProperty)]
    public int ResultsPerPage { get; set; } = 20;

    /// <summary>Gets or sets the identifier of a registered result template (spec §5.8).</summary>
    [DropDownComponent(
        Label = "Result template",
        Placeholder = "Default template",
        Tooltip = "Which registered card template renders one result.",
        ExplanationText = "Only templates a developer registered in the project appear here; empty renders the shipped default card. A template decides the markup, the attributes below decide what it is given.",
        DataProviderType = typeof(ResultTemplateOptionsProvider),
        Order = OrderFirstWidgetProperty + 10)]
    public string ResultTemplate { get; set; } = string.Empty;

    /// <summary>Gets or sets the document fields to retrieve. Empty retrieves the index defaults.</summary>
    [GeneralSelectorComponent(
        dataProviderType: typeof(IndexFieldSelectorDataProvider),
        Label = "Fields to show",
        Placeholder = "Index defaults",
        Tooltip = "Which index fields the search retrieves for each result.",
        ExplanationText = "The index fields each card can read, for example title, url, summary, image. Leave empty for the defaults. Once it is not empty it is the whole list, so a field a card reads - the title, link and snippet attributes below - has to be in it.",
        Order = OrderFirstWidgetProperty + 20)]
    public IEnumerable<string> FieldNames { get; set; } = [];

    /// <summary>
    /// Gets or sets the fields to retrieve as stored by widgets saved before the selector replaced
    /// the text area: one field name per line. Read only when <see cref="FieldNames"/> is empty, and
    /// deliberately without an editing component, so an existing page keeps rendering exactly as it
    /// did while the dialog offers the selector alone.
    /// </summary>
    public string Fields { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute the default template reads the title from. Empty keeps <c>title</c>.</summary>
    [SingleGeneralSelectorComponent(
        dataProviderType: typeof(IndexFieldSelectorDataProvider),
        Label = "Title attribute",
        Placeholder = "Default: title",
        Tooltip = "Index field the card's heading comes from.",
        ExplanationText = "Empty keeps 'title'. Used by the default card; a custom result template may read whatever it likes.",
        Order = OrderFirstWidgetProperty + 30)]
    public string TitleAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute the default template links to. Empty keeps <c>url</c>.</summary>
    [SingleGeneralSelectorComponent(
        dataProviderType: typeof(IndexFieldSelectorDataProvider),
        Label = "Link attribute",
        Placeholder = "Default: url",
        Tooltip = "Index field the card links to.",
        ExplanationText = "Empty keeps 'url'. Point it at another field only when your content stores its address somewhere else, and either leave 'Fields to show' empty or list that field there too.",
        Order = OrderFirstWidgetProperty + 40)]
    public string UrlAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attributes tried, in order, for the snippet, one per line.</summary>
    /// <remarks>
    /// A text area rather than a selector: the order of these attributes is what decides which one
    /// wins, and a general selector does not document its selected values as ordered.
    /// </remarks>
    [TextAreaComponent(
        Label = "Snippet attributes",
        Tooltip = "Index fields the card's summary line is taken from.",
        ExplanationText = "One index field name per line, tried in order; the first one with a value wins. Leave empty for summary, content, excerpt. A field named here is only on the card if it was retrieved: either leave 'Fields to show' empty or list it there too.",
        Order = OrderFirstWidgetProperty + 50)]
    public string SnippetAttributes { get; set; } = string.Empty;
}

/// <summary>Renders the <c>results</c> mount.</summary>
public sealed class ResultsWidgetViewComponent : XpSearchMountWidgetViewComponent<ResultsWidgetProperties, ResultsOptions>
{
    /// <summary>What a widget saved before AR-3 - when 0 meant "use the index's default" - is read as.</summary>
    private const int FallbackResultsPerPage = ResultsOptions.DefaultResultsPerPage;

    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>Initializes a new instance of the <see cref="ResultsWidgetViewComponent"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper.</param>
    /// <param name="editorContext">The current editing mode.</param>
    public ResultsWidgetViewComponent(
        XpSearchMountTagHelper<ResultsOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    /// <inheritdoc />
    public override ResultsOptions ToOptions(ResultsWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new ResultsOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            ResultsPerPage = PageSize(properties),
            Template = properties.ResultTemplate,
            Fields = EffectiveFields(properties),
            TitleAttribute = properties.TitleAttribute,
            UrlAttribute = properties.UrlAttribute,
            SnippetAttributes = ParseLines(properties.SnippetAttributes)
        };
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(ResultsWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // Four cards is enough to read as a list; a page size of 50 must not fill the builder.
        int cards = Math.Clamp(PageSize(properties), 1, 4);
        var list = EditorPreview.El("ol", "xps-results__list");

        for (int card = 0; card < cards; card++)
        {
            list.Add(EditorPreview.El("li", "xps-results__item")
                .Add(EditorPreview.El("article", "xps-result xps-result--skeleton")
                    .Add(EditorPreview.El("div", "xps-result__body")
                        .Add(
                            EditorPreview.Skeleton("title"),
                            EditorPreview.Skeleton("text"),
                            EditorPreview.Skeleton("text")))));
        }

        var fields = EffectiveFields(properties);

        return new HtmlContentBuilder()
            .AppendHtml(EditorPreview.El("div", "xps-results").Add(list))
            .AppendHtml(EditorPreview.Note(string.Format(
                CultureInfo.CurrentUICulture,
                WidgetResources.Preview_Note_Results,
                PageSize(properties).ToString(CultureInfo.CurrentUICulture),
                string.IsNullOrWhiteSpace(properties.ResultTemplate) ? WidgetResources.Preview_Unset : properties.ResultTemplate.Trim(),
                fields.Count > 0 ? string.Join(", ", fields) : WidgetResources.Preview_Unset)));
    }

    // The property is required and one or greater from AR-3 on, but a widget saved while 0 meant "use
    // the index's default" still holds 0, and 0 is a validation error on the wire.
    private static int PageSize(ResultsWidgetProperties properties) =>
        properties.ResultsPerPage > 0 ? properties.ResultsPerPage : FallbackResultsPerPage;

    /// <summary>
    /// The fields to retrieve: what the selector holds, or - for a widget saved before the selector
    /// existed - the lines of the old text area.
    /// </summary>
    private static IReadOnlyList<string> EffectiveFields(ResultsWidgetProperties properties)
    {
        var selected = properties.FieldNames?
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .ToList();

        return selected is { Count: > 0 } ? selected : ParseLines(properties.Fields);
    }

    private static IReadOnlyList<string> ParseLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

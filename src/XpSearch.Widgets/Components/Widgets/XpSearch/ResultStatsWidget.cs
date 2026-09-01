using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.ResultStatsIdentifier,
    viewComponentType: typeof(ResultStatsWidgetViewComponent),
    name: "Search - Result stats",
    propertiesType: typeof(ResultStatsWidgetProperties),
    Description = "Shows how many results the current search found and how long it took.",
    IconClass = "icon-clipboard-checklist",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the result stats widget (spec §7.3).</summary>
public sealed class ResultStatsWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>The wording a freshly placed widget shows, matching the shipped design.</summary>
    public const string DefaultTextTemplate = "{total} results for “{query}” ({tookMs} ms)";

    /// <summary>
    /// Gets or sets the wording of the result line. The default is the design's own wording;
    /// empty falls back to the JavaScript's built-in text.
    /// </summary>
    [TextInputComponent(
        Label = "Text template",
        ExplanationText = "Placeholders: {total}, {tookMs}, {query}, {page}, {totalPages}. The count is emphasised. Markup is shown, not rendered.",
        Order = OrderFirstWidgetProperty)]
    public string TextTemplate { get; set; } = DefaultTextTemplate;

    /// <summary>Gets or sets the text shown before the first search runs.</summary>
    [TextInputComponent(Label = "Text before the first search", Order = OrderFirstWidgetProperty + 10)]
    public string EmptyText { get; set; } = string.Empty;
}

/// <summary>Renders the <c>resultStats</c> mount.</summary>
public sealed class ResultStatsWidgetViewComponent : XpSearchMountWidgetViewComponent<ResultStatsWidgetProperties>
{
    /// <summary>Initializes a new instance of the <see cref="ResultStatsWidgetViewComponent"/> class.</summary>
    /// <param name="renderer">Renders the mount element.</param>
    /// <param name="editorContext">The current editing mode.</param>
    /// <param name="indexCatalog">The registered indexes.</param>
    public ResultStatsWidgetViewComponent(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editorContext,
        IXpSearchIndexCatalog indexCatalog)
        : base(renderer, editorContext, indexCatalog)
    {
    }

    /// <inheritdoc />
    protected override string WidgetType => "resultStats";

    /// <inheritdoc />
    /// <remarks>
    /// The configured template is shown with its placeholders unsubstituted, which is exactly what
    /// the editor typed and cannot be mistaken for a real count.
    /// </remarks>
    protected override IHtmlContent BuildEditorPreview(ResultStatsWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        bool empty = string.IsNullOrWhiteSpace(properties.TextTemplate);
        string text = empty
            ? (string.IsNullOrWhiteSpace(properties.EmptyText) ? "{total} · {tookMs}" : properties.EmptyText)
            : properties.TextTemplate;

        return EditorPreview
            .El("div", empty ? "xps-result-stats xps-result-stats--empty" : "xps-result-stats")
            .Add(EditorPreview.El("span", "xps-result-stats__text", text));
    }
}

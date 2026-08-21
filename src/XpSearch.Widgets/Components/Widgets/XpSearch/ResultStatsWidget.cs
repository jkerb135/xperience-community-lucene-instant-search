using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

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
    /// <summary>
    /// Gets or sets the wording of the result line. Empty keeps the built-in text
    /// ("46 results in 14 ms").
    /// </summary>
    [TextInputComponent(
        Label = "Text template",
        ExplanationText = "Placeholders: {total}, {tookMs}, {query}, {page}, {totalPages}. Leave empty for the built-in text. Markup is shown, not rendered.",
        Order = OrderFirstWidgetProperty)]
    public string TextTemplate { get; set; } = string.Empty;

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
}

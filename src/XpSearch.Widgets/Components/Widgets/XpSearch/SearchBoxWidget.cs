using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;

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
    [TextInputComponent(Label = "Placeholder", Order = OrderFirstWidgetProperty)]
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the clear button is offered once the visitor has typed.</summary>
    [CheckBoxComponent(Label = "Show reset button", Order = OrderFirstWidgetProperty + 10)]
    public bool ShowReset { get; set; } = true;

    /// <summary>Gets or sets whether the input takes focus on page load.</summary>
    [CheckBoxComponent(
        Label = "Focus on page load",
        ExplanationText = "Use on a dedicated search page only; stealing focus is disorienting elsewhere.",
        Order = OrderFirstWidgetProperty + 20)]
    public bool Autofocus { get; set; }
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
}

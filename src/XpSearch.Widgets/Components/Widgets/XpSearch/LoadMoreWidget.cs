using Kentico.PageBuilder.Web.Mvc;
using Kentico.Xperience.Admin.Base.FormAnnotations;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.Resources;
using XpSearch.Widgets.TagHelpers;

[assembly: RegisterWidget(
    identifier: XpSearchWidgetConstants.LoadMoreIdentifier,
    viewComponentType: typeof(LoadMoreWidgetViewComponent),
    name: "Search - Load more",
    propertiesType: typeof(LoadMoreWidgetProperties),
    Description = "An endless result list: each page is appended to the one before it.",
    IconClass = "icon-list",
    AllowCache = false)]

namespace XpSearch.Widgets.Components.Widgets.XpSearch;

/// <summary>Editor properties of the load more widget (spec §7.3, RZ-1 §1.6).</summary>
public sealed class LoadMoreWidgetProperties : XpSearchMountWidgetProperties
{
    /// <summary>Gets or sets whether the next page loads when the end of the list scrolls into view.</summary>
    [CheckBoxComponent(
        Label = "Load the next page on scroll",
        Tooltip = "Loads the next page when the visitor reaches the end of the list.",
        ExplanationText = "This widget replaces the Search - Results and Search - Pagination widgets and cannot sit beside them: it renders the results itself and owns which page the search is on. Off, only the button loads more; the button is always there either way, so the list stays keyboard-operable.",
        Order = OrderFirstWidgetProperty)]
    public bool AutoLoad { get; set; } = true;

    /// <summary>Gets or sets the attribute the default card reads the title from. Empty keeps <c>title</c>.</summary>
    [SingleGeneralSelectorComponent(
        dataProviderType: typeof(IndexFieldSelectorDataProvider),
        Label = "Title attribute",
        Placeholder = "Default: title",
        Tooltip = "Index field the card's heading comes from.",
        ExplanationText = "Empty keeps 'title'. The cards are the same ones the Search - Results widget renders.",
        Order = OrderFirstWidgetProperty + 10)]
    public string TitleAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attribute the default card links to. Empty keeps <c>url</c>.</summary>
    [SingleGeneralSelectorComponent(
        dataProviderType: typeof(IndexFieldSelectorDataProvider),
        Label = "Link attribute",
        Placeholder = "Default: url",
        Tooltip = "Index field the card links to.",
        ExplanationText = "Empty keeps 'url'. Point it at another field only when your content stores its address somewhere else.",
        Order = OrderFirstWidgetProperty + 20)]
    public string UrlAttribute { get; set; } = string.Empty;

    /// <summary>Gets or sets the attributes tried, in order, for the snippet, one per line.</summary>
    [TextAreaComponent(
        Label = "Snippet attributes",
        Tooltip = "Index fields the card's summary line is taken from.",
        ExplanationText = "One index field name per line, tried in order; the first one with a value wins. Leave empty for summary, content, excerpt.",
        Order = OrderFirstWidgetProperty + 30)]
    public string SnippetAttributes { get; set; } = string.Empty;

    /// <summary>Gets or sets the button text while there is more to load.</summary>
    [TextInputComponent(
        Label = "Button text",
        Tooltip = "The button's text while there are pages left.",
        ExplanationText = "Empty leaves \"Load more results\".",
        Order = OrderFirstWidgetProperty + 40)]
    public string MoreLabel { get; set; } = string.Empty;

    /// <summary>Gets or sets the button text once everything is loaded.</summary>
    [TextInputComponent(
        Label = "Button text when everything is loaded",
        Tooltip = "The button's text once every result is on screen.",
        ExplanationText = "Empty leaves \"No more results\". The button is disabled at that point rather than hidden, so the list never ends in a dead control that looks clickable.",
        Order = OrderFirstWidgetProperty + 50)]
    public string ExhaustedLabel { get; set; } = string.Empty;
}

/// <summary>Renders the <c>loadMore</c> mount.</summary>
public sealed class LoadMoreWidgetViewComponent : XpSearchMountWidgetViewComponent<LoadMoreWidgetProperties, LoadMoreOptions>
{
    private static readonly char[] LineSeparators = ['\r', '\n'];

    /// <summary>Initializes a new instance of the <see cref="LoadMoreWidgetViewComponent"/> class.</summary>
    /// <param name="tagHelper">The widget's tag helper.</param>
    /// <param name="editorContext">The current editing mode.</param>
    public LoadMoreWidgetViewComponent(
        XpSearchMountTagHelper<LoadMoreOptions> tagHelper,
        IXpSearchEditorContext editorContext)
        : base(tagHelper, editorContext)
    {
    }

    /// <inheritdoc />
    public override LoadMoreOptions ToOptions(LoadMoreWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new LoadMoreOptions
        {
            Index = properties.Index,
            InstanceId = properties.InstanceId,
            AutoLoad = properties.AutoLoad,
            TitleAttribute = properties.TitleAttribute,
            UrlAttribute = properties.UrlAttribute,
            SnippetAttributes = ParseLines(properties.SnippetAttributes),
            MoreLabel = properties.MoreLabel,
            ExhaustedLabel = properties.ExhaustedLabel
        };
    }

    /// <inheritdoc />
    protected override IHtmlContent BuildEditorPreview(LoadMoreWidgetProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var list = EditorPreview.El("ol", "xps-load-more__list");

        for (int card = 0; card < 3; card++)
        {
            list.Add(EditorPreview.El("li", "xps-load-more__item")
                .Add(EditorPreview.El("article", "xps-result xps-result--skeleton")
                    .Add(EditorPreview.El("div", "xps-result__body")
                        .Add(
                            EditorPreview.Skeleton("title"),
                            EditorPreview.Skeleton("text"),
                            EditorPreview.Skeleton("text")))));
        }

        return EditorPreview.El("div", "xps-load-more")
            .Add(
                list,
                EditorPreview.Button(
                    "xps-button xps-load-more__load-more",
                    string.IsNullOrWhiteSpace(properties.MoreLabel)
                        ? WidgetResources.Preview_LoadMore
                        : properties.MoreLabel.Trim()));
    }

    private static IReadOnlyList<string> ParseLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

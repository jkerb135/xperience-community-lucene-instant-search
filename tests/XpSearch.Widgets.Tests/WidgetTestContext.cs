using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Html;

using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// One options instance behind <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>,
/// which is what the widgets take since AR-1.
/// </summary>
/// <typeparam name="T">The options type.</typeparam>
internal sealed class StaticOptionsMonitor<T> : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    internal StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Fixed set of index names, standing in for the project's Lucene indexes.</summary>
internal sealed class FakeIndexCatalog : IXpSearchIndexCatalog
{
    private readonly string[] names;

    public FakeIndexCatalog(params string[] names) => this.names = names;

    public IReadOnlyList<string> GetIndexNames() => names;
}

/// <summary>A fixed editing mode.</summary>
internal sealed class FakeEditorContext : IXpSearchEditorContext
{
    public FakeEditorContext(XpSearchEditorMode mode) => Mode = mode;

    public XpSearchEditorMode Mode { get; set; }

    public XpSearchEditorMode GetMode() => Mode;
}

/// <summary>
/// Builds each Page Builder widget over its tag helper, which is what the DI container does on a
/// host (RZ-1 §2). One place, so a widget's dependencies change in one place.
/// </summary>
internal static class Widgets
{
    internal static SearchBoxWidgetViewComponent SearchBox(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new SearchBoxTagHelper(renderer, catalog), editor);

    internal static ResultsWidgetViewComponent Results(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editor,
        IXpSearchIndexCatalog catalog,
        Core.Rendering.ServerRenderedResults? serverResults = null) =>
        new(new ResultsTagHelper(renderer, catalog, serverResults), editor);

    internal static FacetListWidgetViewComponent FacetList(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new FacetListTagHelper(renderer, catalog), editor);

    internal static CategoryTreeWidgetViewComponent CategoryTree(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new CategoryTreeTagHelper(renderer, catalog), editor);

    internal static RangeFilterWidgetViewComponent RangeFilter(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new RangeFilterTagHelper(renderer, catalog), editor);

    internal static PaginationWidgetViewComponent Pagination(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new PaginationTagHelper(renderer, catalog), editor);

    internal static ResultStatsWidgetViewComponent ResultStats(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new ResultStatsTagHelper(renderer, catalog), editor);

    internal static ActiveFiltersWidgetViewComponent ActiveFilters(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new ActiveFiltersTagHelper(renderer, catalog), editor);

    internal static ClearFiltersWidgetViewComponent ClearFilters(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new ClearFiltersTagHelper(renderer, catalog), editor);

    internal static SuggestionsWidgetViewComponent Suggestions(
        IXpSearchMountRenderer renderer, IXpSearchEditorContext editor, IXpSearchIndexCatalog catalog) =>
        new(new SuggestionsTagHelper(renderer, catalog), editor);

    internal static SortSelectWidgetViewComponent SortSelect(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editor,
        IXpSearchIndexCatalog catalog,
        Microsoft.Extensions.Options.IOptionsMonitor<Core.Options.XpSearchOptions> searchOptions) =>
        new(new SortSelectTagHelper(renderer, catalog, searchOptions), editor, searchOptions);

    /// <summary>
    /// The view model of one render, waited for. Building a mount is asynchronous since RZ-1 (the
    /// results widget renders its first paint inside it); the tests read it synchronously.
    /// </summary>
    internal static XpSearchMountViewModel BuildModel<TProperties, TOptions>(
        this XpSearchMountWidgetViewComponent<TProperties, TOptions> component, TProperties properties)
        where TProperties : XpSearchMountWidgetProperties, new()
        where TOptions : XpSearchMountOptions, new() =>
        component.BuildModelAsync(properties, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Gives a widget the view context a live page would, so its first paint can render.</summary>
    internal static TComponent WithViewContext<TComponent>(
        this TComponent component, Microsoft.AspNetCore.Mvc.Rendering.ViewContext viewContext)
        where TComponent : Microsoft.AspNetCore.Mvc.ViewComponent
    {
        component.ViewComponentContext = new Microsoft.AspNetCore.Mvc.ViewComponents.ViewComponentContext
        {
            ViewContext = viewContext
        };

        return component;
    }

    internal static FilterSortWidgetViewComponent FilterSort(
        IXpSearchMountRenderer renderer,
        IXpSearchEditorContext editor,
        IXpSearchIndexCatalog catalog,
        Microsoft.Extensions.Options.IOptionsMonitor<Core.Options.XpSearchOptions> searchOptions) =>
        new(new FilterSortTagHelper(renderer, catalog, searchOptions), editor);
}

/// <summary>Helpers shared by the widget tests.</summary>
internal static class Rendered
{

    /// <summary>Renders HTML content the way Razor would.</summary>
    public static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);

        return writer.ToString();
    }

    /// <summary>Reads one HTML attribute of a rendered single-element markup string, decoded.</summary>
    public static string Attribute(string markup, string name)
    {
        string start = $"{name}=\"";
        int at = markup.IndexOf(start, StringComparison.Ordinal);
        Assert.That(at, Is.GreaterThanOrEqualTo(0), $"attribute {name} is missing from: {markup}");
        int from = at + start.Length;
        int to = markup.IndexOf('"', from);

        return System.Net.WebUtility.HtmlDecode(markup[from..to]);
    }

    /// <summary>Parses the JSON of an attribute into a dictionary of raw JSON elements.</summary>
    public static JsonElement Json(string markup, string name) =>
        JsonDocument.Parse(Attribute(markup, name)).RootElement.Clone();
}

/// <summary>
/// Wrappers around the NUnit assertions whose overloads a lambda cannot disambiguate; the same
/// helper the core test project keeps for the same reason.
/// </summary>
internal static class Expect
{
    internal static void Multiple(Action assertions) => Assert.Multiple(assertions);
}

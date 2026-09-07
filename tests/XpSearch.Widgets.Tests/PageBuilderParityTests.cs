using System.Reflection;

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// RZ-1's acceptance test: one implementation per widget. Whatever a Page Builder widget renders on a
/// live page, the tag element renders byte for byte from the same options record - which is what makes
/// "the widgets scaffold on the tag helpers" true rather than a claim.
/// </summary>
[TestFixture]
internal sealed class PageBuilderParityTests
{
    private const string Index = "site-content";

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index, "products");
    private readonly FakeEditorContext editor = new(XpSearchEditorMode.Live);

    /// <summary>The live-page markup of one widget, placed both ways.</summary>
    private (string PageBuilder, string Tag) BothWays<TProperties, TOptions>(
        XpSearchMountWidgetViewComponent<TProperties, TOptions> component,
        XpSearchMountTagHelper<TOptions> tagHelper,
        TProperties properties,
        string tagName)
        where TProperties : XpSearchMountWidgetProperties, new()
        where TOptions : XpSearchMountOptions, new()
    {
        var model = component.BuildModel(properties);
        Assert.That(model.Mount, Is.Not.Null, $"{tagName}: the widget rendered no mount");

        tagHelper.Options = component.ToOptions(properties);

        return (Rendered.Html(model.Mount!), TagHelperTests.Tag(tagHelper, tagName));
    }

    [Test]
    public void Every_widget_renders_the_same_bytes_however_it_was_placed()
    {
        var searchOptions = TagHelperTests.SearchOptions();

        var pairs = new List<(string Widget, string PageBuilder, string Tag)>
        {
            Pair("searchBox", BothWays(
                Widgets.SearchBox(renderer, editor, catalog),
                new SearchBoxTagHelper(renderer, catalog),
                new SearchBoxWidgetProperties
                {
                    Index = Index,
                    InstanceId = "search-1",
                    Placeholder = "Find coffee",
                    Autofocus = true,
                    EnableSuggestions = true,
                    SuggestionLimit = 8,
                    RecentSearches = false,
                    SyncStateToUrl = false,
                    SearchOnInitialLoad = false
                },
                "xps-search-box")),
            Pair("results", BothWays(
                Widgets.Results(renderer, editor, catalog),
                new ResultsTagHelper(renderer, catalog),
                new ResultsWidgetProperties
                {
                    Index = Index,
                    ResultsPerPage = 12,
                    ResultTemplate = "MyCo.Card",
                    FieldNames = ["title", " url "],
                    TitleAttribute = "heading",
                    UrlAttribute = "permalink",
                    SnippetAttributes = "teaser\r\nexcerpt"
                },
                "xps-results")),
            Pair("facetList", BothWays(
                Widgets.FacetList(renderer, editor, catalog),
                new FacetListTagHelper(renderer, catalog),
                new FacetListWidgetProperties
                {
                    Index = Index,
                    Attribute = "contentType",
                    Label = "Content type",
                    Operator = "and",
                    Limit = 5,
                    ShowMore = true,
                    Collapsible = false
                },
                "xps-facet-list")),
            Pair("categoryTree", BothWays(
                Widgets.CategoryTree(renderer, editor, catalog),
                new CategoryTreeTagHelper(renderer, catalog),
                new CategoryTreeWidgetProperties { Index = Index, Attribute = "categories", Label = "Categories", Limit = 4 },
                "xps-category-tree")),
            Pair("rangeFilter", BothWays(
                Widgets.RangeFilter(renderer, editor, catalog),
                new RangeFilterTagHelper(renderer, catalog),
                new RangeFilterWidgetProperties
                {
                    Index = Index,
                    Attribute = "price",
                    Label = "Price",
                    Minimum = 0m,
                    Maximum = 500m,
                    Step = 5m,
                    FromLabel = "Cheapest",
                    ToLabel = "Dearest",
                    Unit = "USD"
                },
                "xps-range-filter")),
            Pair("sortSelect", BothWays(
                Widgets.SortSelect(renderer, editor, catalog, searchOptions),
                new SortSelectTagHelper(renderer, catalog, searchOptions),
                new SortSelectWidgetProperties
                {
                    Index = Index,
                    SortOptions = "relevance;Most relevant\r\nnewest;Newest first\r\nnonsense;Nope",
                    Label = "Order by",
                    HideLabel = true
                },
                "xps-sort-select")),
            Pair("pagination", BothWays(
                Widgets.Pagination(renderer, editor, catalog),
                new PaginationTagHelper(renderer, catalog),
                new PaginationWidgetProperties { Index = Index, Padding = 2, ShowFirst = false, ShowLast = true },
                "xps-pagination")),
            Pair("pagination:loadMore", BothWays(
                Widgets.Pagination(renderer, editor, catalog),
                new PaginationTagHelper(renderer, catalog),
                new PaginationWidgetProperties { Index = Index, Style = PaginationWidgetProperties.StyleLoadMore },
                "xps-pagination")),
            Pair("toggleFilter", BothWays(
                Widgets.ToggleFilter(renderer, editor, catalog),
                new ToggleFilterTagHelper(renderer, catalog),
                new ToggleFilterWidgetProperties
                {
                    Index = Index,
                    Attribute = "language",
                    Value = "en",
                    Label = "English only",
                    ShowCount = false
                },
                "xps-toggle-filter")),
            Pair("loadMore", BothWays(
                Widgets.LoadMore(renderer, editor, catalog),
                new LoadMoreTagHelper(renderer, catalog),
                new LoadMoreWidgetProperties
                {
                    Index = Index,
                    AutoLoad = false,
                    TitleAttribute = "heading",
                    UrlAttribute = "permalink",
                    SnippetAttributes = "teaser\r\nexcerpt",
                    MoreLabel = "Show more coffee",
                    ExhaustedLabel = "That is all of it"
                },
                "xps-load-more")),
            Pair("resultStats", BothWays(
                Widgets.ResultStats(renderer, editor, catalog),
                new ResultStatsTagHelper(renderer, catalog),
                new ResultStatsWidgetProperties { Index = Index, TextTemplate = "{total} hits", EmptyText = "Start typing." },
                "xps-result-stats")),
            Pair("activeFilters", BothWays(
                Widgets.ActiveFilters(renderer, editor, catalog),
                new ActiveFiltersTagHelper(renderer, catalog),
                new ActiveFiltersWidgetProperties
                {
                    Index = Index,
                    Title = "Your filters",
                    Scroll = true,
                    AttributeLabels = "contentType;Content type\r\ntags"
                },
                "xps-active-filters")),
            Pair("clearFilters", BothWays(
                Widgets.ClearFilters(renderer, editor, catalog),
                new ClearFiltersTagHelper(renderer, catalog),
                new ClearFiltersWidgetProperties { Index = Index, Label = "Start over" },
                "xps-clear-filters")),
            Pair("filterSort", BothWays(
                Widgets.FilterSort(renderer, editor, catalog, searchOptions),
                new FilterSortTagHelper(renderer, catalog, searchOptions),
                new FilterSortWidgetProperties
                {
                    Index = Index,
                    Facets = "contentType;Content type\r\ntags",
                    SortOptions = "relevance;Most relevant",
                    Label = "Filter",
                    ApplyLabel = "Show them"
                },
                "xps-filter-sort")),
            Pair("suggestions", BothWays(
                Widgets.Suggestions(renderer, editor, catalog),
                new SuggestionsTagHelper(renderer, catalog),
                new SuggestionsWidgetProperties { Index = Index, Mode = SuggestionsWidgetProperties.ModeMixed, MaxItems = 8 },
                "xps-suggestions"))
        };

        Expect.Multiple(() =>
        {
            foreach ((string widget, string pageBuilder, string tag) in pairs)
            {
                Assert.That(tag, Is.EqualTo(pageBuilder), widget);
                Assert.That(pageBuilder, Does.StartWith("<div class=\"xps-mount\""), widget);
            }
        });

        // The 14 shipped widgets, plus the pagination widget's load-more style.
        Assert.That(pairs.Select(pair => pair.Widget).Distinct().Count(), Is.EqualTo(15));
    }

    private static (string, string, string) Pair(string widget, (string PageBuilder, string Tag) markup) =>
        (widget, markup.PageBuilder, markup.Tag);
}

/// <summary>
/// RZ-1 §4: the tag element's attributes are the options record, one for one, so a new option cannot
/// quietly stay out of Razor's reach.
/// </summary>
[TestFixture]
internal sealed class TagHelperAttributeTests
{
    /// <summary>Every shipped tag helper, with the options record it renders.</summary>
    private static IEnumerable<(Type TagHelper, Type Options)> TagHelpers() =>
        typeof(XpSearchMountTagHelper<>).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(TagHelper)))
            .Select(type => (TagHelper: type, Base: Closed(type)))
            .Where(pair => pair.Base is not null)
            .Select(pair => (pair.TagHelper, Options: pair.Base!.GetGenericArguments()[0]));

    private static Type? Closed(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(XpSearchMountTagHelper<>))
            {
                return current;
            }
        }

        return null;
    }

    private static string Kebab(string name) =>
        string.Concat(name.Select((character, at) =>
            char.IsUpper(character) && at > 0 ? $"-{char.ToLowerInvariant(character)}" : $"{char.ToLowerInvariant(character)}"));

    [Test]
    public void Every_option_has_exactly_one_kebab_cased_attribute()
    {
        var helpers = TagHelpers().ToList();
        Assert.That(helpers, Has.Count.EqualTo(15), "the 14 shipped widgets and <xps-widget>");

        Expect.Multiple(() =>
        {
            foreach ((var tagHelper, var options) in helpers)
            {
                var attributes = tagHelper
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.GetCustomAttribute<HtmlAttributeNameAttribute>()?.Name)
                    .Where(name => name is not null)
                    .ToList();

                foreach (var option in options.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    Assert.That(
                        attributes.Count(name => name == Kebab(option.Name)),
                        Is.EqualTo(1),
                        $"{tagHelper.Name}: {option.Name} needs exactly one [HtmlAttributeName(\"{Kebab(option.Name)}\")]");
                }

                // The three the base class binds for every widget.
                Assert.That(attributes, Does.Contain("index").And.Contain("instance").And.Contain("options"), tagHelper.Name);
                Assert.That(tagHelper.GetCustomAttribute<HtmlTargetElementAttribute>()?.Tag, Does.StartWith("xps-"), tagHelper.Name);
            }
        });
    }

    /// <summary>
    /// RZ-1's 14/14: every Page Builder widget is a tag helper's editor face, and no tag helper is
    /// missing its Page Builder widget. <c>&lt;xps-widget&gt;</c> is the one tag with no Page Builder
    /// counterpart - a custom JavaScript widget mounts through the third-party base class instead.
    /// </summary>
    [Test]
    public void The_shipped_tag_helpers_and_the_Page_Builder_widgets_are_the_same_set_of_options()
    {
        var tagHelperOptions = TagHelpers()
            .Select(pair => pair.Options)
            .Where(options => options != typeof(WidgetOptions))
            .ToList();

        var widgetOptions = typeof(XpSearchMountTagHelper<>).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract)
            .Select(Component)
            .Where(component => component is not null)
            .Select(component => component!.GetGenericArguments()[1])
            .ToList();

        Expect.Multiple(() =>
        {
            Assert.That(tagHelperOptions, Has.Count.EqualTo(14), "14 JavaScript widgets, 14 tag helpers");
            Assert.That(widgetOptions, Is.EquivalentTo(tagHelperOptions));
        });
    }

    private static Type? Component(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(XpSearchMountWidgetViewComponent<,>))
            {
                return current;
            }
        }

        return null;
    }

    [Test]
    public void AddXpSearchWidgets_registers_every_shipped_tag_helper_by_its_options_type()
    {
        var services = new ServiceCollection().AddXpSearchWidgets();

        Expect.Multiple(() =>
        {
            foreach ((var tagHelper, var options) in TagHelpers())
            {
                var service = typeof(XpSearchMountTagHelper<>).MakeGenericType(options);
                Assert.That(
                    services.Any(descriptor => descriptor.ServiceType == service),
                    Is.True,
                    $"{tagHelper.Name} is not resolvable by its options type");
            }
        });
    }
}

using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Spec §7.5, unit PB-4: inside the Page Builder a configured widget renders a static preview of
/// itself instead of the mount element, so an editor never sees an empty shell.
/// </summary>
[TestFixture]
internal sealed class EditorPreviewTests
{
    private const string Index = "site-content";

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index, "products");

    private static IOptions<XpSearchOptions> SortOptions()
    {
        var options = new XpSearchOptions();
        options.Indexes[Index].SortKeys["newest"] = new SortKey("PublishedAt", Descending: true);

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    /// <summary>Every widget, configured enough to render, in the mode under test.</summary>
    private IEnumerable<(string WidgetType, string Markup)> AllWidgets(XpSearchEditorMode mode)
    {
        var editor = new FakeEditorContext(mode);

        yield return ("searchBox", Render(new SearchBoxWidgetViewComponent(renderer, editor, catalog),
            new SearchBoxWidgetProperties { Index = Index, Placeholder = "Find coffee" }, mode));
        yield return ("results", Render(new ResultsWidgetViewComponent(renderer, editor, catalog),
            new ResultsWidgetProperties { Index = Index, ResultsPerPage = 20, ResultTemplate = "MyCo.Card", Fields = "title\nurl" }, mode));
        yield return ("facetList", Render(new FacetListWidgetViewComponent(renderer, editor, catalog),
            new FacetListWidgetProperties { Index = Index, Attribute = "contentType", Label = "Content type", ShowMore = true }, mode));
        yield return ("categoryTree", Render(new CategoryTreeWidgetViewComponent(renderer, editor, catalog),
            new CategoryTreeWidgetProperties { Index = Index, Attribute = "categories", Label = "Categories" }, mode));
        yield return ("pagination", Render(new PaginationWidgetViewComponent(renderer, editor, catalog),
            new PaginationWidgetProperties { Index = Index }, mode));
        yield return ("loadMore", Render(new PaginationWidgetViewComponent(renderer, editor, catalog),
            new PaginationWidgetProperties { Index = Index, Style = PaginationWidgetProperties.StyleLoadMore }, mode));
        yield return ("resultStats", Render(new ResultStatsWidgetViewComponent(renderer, editor, catalog),
            new ResultStatsWidgetProperties { Index = Index, TextTemplate = "{total} hits in {tookMs} ms" }, mode));
        yield return ("sortSelect", Render(new SortSelectWidgetViewComponent(renderer, editor, catalog, SortOptions()),
            new SortSelectWidgetProperties { Index = Index, Label = "Sort by", SortOptions = "relevance;Most relevant\r\nnewest;Newest first" }, mode));
        yield return ("suggestions", Render(new SuggestionsWidgetViewComponent(renderer, editor, catalog),
            new SuggestionsWidgetProperties { Index = Index, MaxItems = 7 }, mode));
        yield return ("rangeFilter", Render(new RangeFilterWidgetViewComponent(renderer, editor, catalog),
            new RangeFilterWidgetProperties { Index = Index, Attribute = "price", Label = "Price", Minimum = 0m, Maximum = 500m, Step = 5m, Unit = "USD" }, mode));
        yield return ("activeFilters", Render(new ActiveFiltersWidgetViewComponent(renderer, editor, catalog),
            new ActiveFiltersWidgetProperties { Index = Index, Scroll = true }, mode));
        yield return ("clearFilters", Render(new ClearFiltersWidgetViewComponent(renderer, editor, catalog),
            new ClearFiltersWidgetProperties { Index = Index, Label = "Start over" }, mode));
    }

    private static string Render<TProperties>(
        XpSearchMountWidgetViewComponent<TProperties> component, TProperties properties, XpSearchEditorMode mode)
        where TProperties : XpSearchMountWidgetProperties, new()
    {
        var model = component.BuildModel(properties);

        if (mode is XpSearchEditorMode.Edit or XpSearchEditorMode.ReadOnly)
        {
            Assert.That(model.Preview, Is.Not.Null, "the widget rendered no preview in the Page Builder");
            Assert.That(model.Mount, Is.Null, "the widget rendered a mount inside the Page Builder");

            return Rendered.Html(model.Preview!);
        }

        Assert.That(model.Mount, Is.Not.Null, "the widget rendered no mount outside the Page Builder");
        Assert.That(model.Preview, Is.Null, "the widget rendered a preview outside the Page Builder");

        return Rendered.Html(model.Mount!);
    }

    [TestCase(XpSearchEditorMode.Edit)]
    [TestCase(XpSearchEditorMode.ReadOnly)]
    public void Every_widget_previews_itself_in_the_Page_Builder(XpSearchEditorMode mode)
    {
        foreach ((string widgetType, string markup) in AllWidgets(mode))
        {
            Expect.Multiple(() =>
            {
                Assert.That(markup, Does.StartWith("<div class=\"xps xps-editor-preview xps-editor-preview--"), widgetType);
                Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo(widgetType));
                Assert.That(markup, Does.Contain("xps-editor-preview__badge"), widgetType);
                Assert.That(markup, Does.Contain(widgetType), $"{widgetType}: the badge does not name the widget");
                Assert.That(markup, Does.Not.Contain("xps-mount"), widgetType);
                Assert.That(markup, Does.Not.Contain("data-xps-config"), widgetType);
            });
        }
    }

    [Test]
    public void Edit_and_read_only_previews_are_identical()
    {
        var edit = AllWidgets(XpSearchEditorMode.Edit).Select(widget => widget.Markup);
        var readOnly = AllWidgets(XpSearchEditorMode.ReadOnly).Select(widget => widget.Markup);

        Assert.That(readOnly, Is.EqualTo(edit));
    }

    [TestCase(XpSearchEditorMode.Live)]
    [TestCase(XpSearchEditorMode.Preview)]
    public void Outside_the_Page_Builder_every_widget_still_renders_its_mount(XpSearchEditorMode mode)
    {
        foreach ((string widgetType, string markup) in AllWidgets(mode))
        {
            Assert.That(markup, Does.StartWith("<div class=\"xps-mount\""), widgetType);
        }
    }

    [Test]
    public void A_preview_is_inert_no_links_no_working_controls()
    {
        foreach ((string widgetType, string markup) in AllWidgets(XpSearchEditorMode.Edit))
        {
            Expect.Multiple(() =>
            {
                Assert.That(markup, Does.Not.Contain("href"), $"{widgetType}: a preview must not link anywhere");
                Assert.That(markup, Does.Not.Contain("<a "), $"{widgetType}: a preview must not link anywhere");

                foreach (var control in Regex.Matches(markup, "<(input|button|select|textarea)[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(1)))
                {
                    Assert.That(control!.ToString(), Does.Contain("disabled"), $"{widgetType}: an operable control in a preview");
                }
            });
        }
    }

    [Test]
    public void The_configured_values_an_editor_typed_are_what_the_preview_shows()
    {
        var previews = AllWidgets(XpSearchEditorMode.Edit).ToDictionary(widget => widget.WidgetType, widget => widget.Markup);

        Expect.Multiple(() =>
        {
            Assert.That(previews["searchBox"], Does.Contain("placeholder=\"Find coffee\""));
            Assert.That(previews["results"], Does.Contain("MyCo.Card").And.Contain("title, url"));
            Assert.That(previews["facetList"], Does.Contain("Content type").And.Contain("contentType"));
            Assert.That(previews["categoryTree"], Does.Contain("Categories").And.Contain("categories"));
            Assert.That(previews["resultStats"], Does.Contain("{total} hits in {tookMs} ms"));
            Assert.That(previews["sortSelect"], Does.Contain("Most relevant").And.Contain("Newest first"));
            Assert.That(previews["suggestions"], Does.Contain("documents").And.Contain("7"));
            Assert.That(previews["rangeFilter"], Does.Contain("Price").And.Contain("max=\"500\"").And.Contain("step=\"5\"")
                .And.Contain("xps-range-filter__unit"));
            // The chevroned disclosure of the live widget is part of the picture.
            Assert.That(previews["facetList"], Does.Contain("xps-facet-list__toggle").And.Contain("aria-expanded=\"true\""));
            Assert.That(previews["categoryTree"], Does.Contain("xps-category-tree__toggle"));
            Assert.That(previews["activeFilters"], Does.Contain("xps-active-filters--scroll").And.Contain("xps-chip__remove"));
            Assert.That(previews["clearFilters"], Does.Contain("Start over").And.Contain("xps-button--link"));
            Assert.That(previews["loadMore"], Does.Contain("xps-load-more__load-more"));
            Assert.That(previews["pagination"], Does.Contain("xps-pagination__item--current"));
        });
    }

    [Test]
    public void The_number_of_skeleton_result_cards_follows_the_page_size_up_to_four()
    {
        static string Preview(int resultsPerPage)
        {
            var component = new ResultsWidgetViewComponent(
                new XpSearchMountRenderer(), new FakeEditorContext(XpSearchEditorMode.Edit), new FakeIndexCatalog(Index));

            return Rendered.Html(component.BuildModel(new ResultsWidgetProperties { Index = Index, ResultsPerPage = resultsPerPage }).Preview!);
        }

        static int Cards(string markup) => Regex.Matches(markup, "xps-results__item", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;

        Expect.Multiple(() =>
        {
            Assert.That(Cards(Preview(0)), Is.EqualTo(3));
            Assert.That(Cards(Preview(2)), Is.EqualTo(2));
            Assert.That(Cards(Preview(50)), Is.EqualTo(4));
        });
    }

    [Test]
    public void A_property_value_cannot_become_markup_in_a_preview()
    {
        var component = new FacetListWidgetViewComponent(
            renderer, new FakeEditorContext(XpSearchEditorMode.Edit), catalog);

        string markup = Rendered.Html(component.BuildModel(new FacetListWidgetProperties
        {
            Index = Index,
            Attribute = "contentType",
            Label = "<script>alert(1)</script>"
        }).Preview!);

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("<script>"));
            Assert.That(markup, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt;"));
        });
    }

    [Test]
    public void An_unconfigured_widget_keeps_its_instruction_block_in_the_Page_Builder()
    {
        var component = new FacetListWidgetViewComponent(
            renderer, new FakeEditorContext(XpSearchEditorMode.Edit), catalog);

        var model = component.BuildModel(new FacetListWidgetProperties { Index = Index });

        Expect.Multiple(() =>
        {
            Assert.That(model.Preview, Is.Null);
            Assert.That(model.Mount, Is.Null);
            Assert.That(model.EditorMessage, Does.Contain("attribute"));
        });
    }

    [Test]
    public void A_third_party_widget_gets_a_labelled_preview_from_the_base_class_alone()
    {
        var component = new DropdownFacetWidgetViewComponent(
            renderer, new FakeEditorContext(XpSearchEditorMode.Edit), catalog);

        string markup = Rendered.Html(component
            .BuildModel(new DropdownFacetWidgetProperties { Index = Index, Attribute = "brand" })
            .Preview!);

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("xps-editor-preview--my-company-dropdown-facet"));
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("myCompany.dropdownFacet"));
            Assert.That(markup, Does.Contain("xps-editor-preview__note"));
        });
    }
}

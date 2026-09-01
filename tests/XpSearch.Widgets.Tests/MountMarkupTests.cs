using System.Text.Json;

using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The markup contract of spec §7.1: every widget renders one configured <c>.xps-mount</c> element
/// the JavaScript bootstrap can pick up.
/// </summary>
[TestFixture]
internal sealed class MountMarkupTests
{
    private const string Index = "site-content";

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index, "other-index");
    private readonly FakeEditorContext editor = new(XpSearchEditorMode.Live);

    private static IOptions<XpSearchOptions> SearchOptions()
    {
        var options = new XpSearchOptions();
        options.Indexes[Index].SortKeys["newest"] = new SortKey("PublishedAt", Descending: true);

        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private string Render<TProperties>(XpSearchMountWidgetViewComponent<TProperties> component, TProperties properties)
        where TProperties : XpSearchMountWidgetProperties, new()
    {
        var model = component.BuildModel(properties);
        Assert.That(model.Mount, Is.Not.Null, "the widget rendered no mount");

        return Rendered.Html(model.Mount!);
    }

    private SearchBoxWidgetViewComponent SearchBox() => new(renderer, editor, catalog);

    [Test]
    public void SearchBox_emits_the_mount_contract()
    {
        string markup = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index, Placeholder = "Find coffee" });

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("<div class=\"xps-mount\""));
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("searchBox"));
            Assert.That(Rendered.Attribute(markup, "data-xps-instance"), Is.EqualTo("default"));
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(config.GetProperty("placeholder").GetString(), Is.EqualTo("Find coffee"));
            Assert.That(config.GetProperty("showReset").GetBoolean(), Is.True);
            Assert.That(config.GetProperty("autofocus").GetBoolean(), Is.False);
        });

        var instance = Rendered.Json(markup, "data-xps-instance-config");
        Assert.That(instance.GetProperty("index").GetString(), Is.EqualTo(Index));
    }

    [Test]
    public void SearchBox_turns_URL_syncing_on_by_default_and_off_when_the_editor_unticks_it()
    {
        string synced = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index });
        string secondary = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index, SyncStateToUrl = false });

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Json(synced, "data-xps-instance-config").GetProperty("routing").GetBoolean(), Is.True);
            Assert.That(Rendered.Json(secondary, "data-xps-instance-config").GetProperty("routing").GetBoolean(), Is.False);
            // The option belongs to the search, not to the input widget.
            Assert.That(Rendered.Json(synced, "data-xps-config").TryGetProperty("syncStateToUrl", out _), Is.False);
        });

        TestContext.Out.WriteLine(synced);
    }

    [Test]
    public void Empty_editor_fields_are_left_out_so_the_JavaScript_default_wins()
    {
        string markup = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index });

        Assert.That(
            Rendered.Json(markup, "data-xps-config").TryGetProperty("placeholder", out _),
            Is.False);
    }

    [Test]
    public void InstanceId_defaults_to_default_and_is_trimmed()
    {
        string blank = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index, InstanceId = "   " });
        string named = Render(SearchBox(), new SearchBoxWidgetProperties { Index = Index, InstanceId = " search-1 " });

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(blank, "data-xps-instance"), Is.EqualTo("default"));
            Assert.That(Rendered.Attribute(named, "data-xps-instance"), Is.EqualTo("search-1"));
        });
    }

    [Test]
    public void The_only_index_of_a_project_is_used_when_the_editor_picked_none()
    {
        var single = new SearchBoxWidgetViewComponent(renderer, editor, new FakeIndexCatalog("only-index"));

        string markup = Render(single, new SearchBoxWidgetProperties());

        Assert.That(
            Rendered.Json(markup, "data-xps-instance-config").GetProperty("index").GetString(),
            Is.EqualTo("only-index"));
    }

    [Test]
    public void Quotes_and_angle_brackets_in_a_property_cannot_break_out_of_the_attribute()
    {
        string markup = Render(
            SearchBox(),
            new SearchBoxWidgetProperties { Index = Index, Placeholder = "say \"hi\" <script>alert(1)</script>" });

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("<script>"));
            Assert.That(markup, Does.Contain("&quot;"));
            Assert.That(
                Rendered.Json(markup, "data-xps-config").GetProperty("placeholder").GetString(),
                Is.EqualTo("say \"hi\" <script>alert(1)</script>"));
        });
    }

    [Test]
    public void Results_maps_results_per_page_and_fields_to_instance_options()
    {
        var component = new ResultsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new ResultsWidgetProperties
        {
            Index = Index,
            ResultsPerPage = 12,
            ResultTemplate = "MyCompany.ProductCard",
            FieldNames = ["title", " url ", "summary"]
        });

        var instance = Rendered.Json(markup, "data-xps-instance-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("results"));
            Assert.That(
                Rendered.Json(markup, "data-xps-config").GetProperty("template").GetString(),
                Is.EqualTo("MyCompany.ProductCard"));
            Assert.That(instance.GetProperty("initialState").GetProperty("pageSize").GetInt32(), Is.EqualTo(12));
            Assert.That(
                instance.GetProperty("fields").EnumerateArray().Select(field => field.GetString()),
                Is.EqualTo(new[] { "title", "url", "summary" }));
        });
    }

    [Test]
    public void Results_still_reads_the_fields_a_widget_saved_before_the_selector_existed()
    {
        var component = new ResultsWidgetViewComponent(renderer, editor, catalog);

        // The stored shape of the retired text area: one field name per line.
        string markup = Render(component, new ResultsWidgetProperties
        {
            Index = Index,
            Fields = "title\r\nurl\n summary "
        });

        var instance = Rendered.Json(markup, "data-xps-instance-config");
        Assert.That(
            instance.GetProperty("fields").EnumerateArray().Select(field => field.GetString()),
            Is.EqualTo(new[] { "title", "url", "summary" }));
    }

    [Test]
    public void Results_prefers_the_selected_fields_over_the_old_stored_ones()
    {
        var component = new ResultsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new ResultsWidgetProperties
        {
            Index = Index,
            Fields = "title\r\nurl",
            FieldNames = ["heading"]
        });

        var instance = Rendered.Json(markup, "data-xps-instance-config");
        Assert.That(
            instance.GetProperty("fields").EnumerateArray().Select(field => field.GetString()),
            Is.EqualTo(new[] { "heading" }));
    }

    [Test]
    public void Results_maps_the_title_link_and_snippet_attribute_overrides_to_display_options()
    {
        var component = new ResultsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new ResultsWidgetProperties
        {
            Index = Index,
            TitleAttribute = " heading ",
            UrlAttribute = "permalink",
            SnippetAttributes = "teaser\r\n excerpt "
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(config.GetProperty("titleAttribute").GetString(), Is.EqualTo("heading"));
            Assert.That(config.GetProperty("urlAttribute").GetString(), Is.EqualTo("permalink"));
            Assert.That(
                config.GetProperty("snippetAttributes").EnumerateArray().Select(name => name.GetString()),
                Is.EqualTo(new[] { "teaser", "excerpt" }));
            // They tell the template which attribute to show; they are not part of the search.
            Assert.That(Rendered.Json(markup, "data-xps-instance-config").TryGetProperty("titleAttribute", out _), Is.False);
        });
    }

    [Test]
    public void Results_leaves_the_defaults_alone_when_the_editor_set_nothing()
    {
        var component = new ResultsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new ResultsWidgetProperties { Index = Index });

        var instance = Rendered.Json(markup, "data-xps-instance-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-config"), Is.EqualTo("{}"));
            Assert.That(instance.TryGetProperty("initialState", out _), Is.False);
            Assert.That(instance.TryGetProperty("fields", out _), Is.False);
        });
    }

    [Test]
    public void FacetList_emits_every_option_the_JavaScript_widget_takes()
    {
        var component = new FacetListWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new FacetListWidgetProperties
        {
            Index = Index,
            Attribute = "contentType",
            Label = "Content type",
            Operator = "and",
            Limit = 5,
            ShowMore = true
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("facetList"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("contentType"));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("Content type"));
            Assert.That(config.GetProperty("operator").GetString(), Is.EqualTo("and"));
            Assert.That(config.GetProperty("limit").GetInt32(), Is.EqualTo(5));
            Assert.That(config.GetProperty("showMore").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void RangeFilter_emits_its_bounds_as_JSON_numbers_and_nests_the_input_labels()
    {
        var component = new RangeFilterWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new RangeFilterWidgetProperties
        {
            Index = Index,
            InstanceId = "search-1",
            Attribute = "price",
            Label = "Price",
            Minimum = 0m,
            Maximum = 500m,
            Step = 5m,
            FromLabel = "Cheapest",
            ToLabel = "Dearest"
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("rangeFilter"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("price"));
            Assert.That(config.GetProperty("min").ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(config.GetProperty("min").GetDecimal(), Is.EqualTo(0m));
            Assert.That(config.GetProperty("max").GetDecimal(), Is.EqualTo(500m));
            Assert.That(config.GetProperty("step").GetDecimal(), Is.EqualTo(5m));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("Price"));
            Assert.That(config.GetProperty("labels").GetProperty("from").GetString(), Is.EqualTo("Cheapest"));
            Assert.That(config.GetProperty("labels").GetProperty("to").GetString(), Is.EqualTo("Dearest"));
        });

        TestContext.Out.WriteLine(markup);
    }

    [Test]
    public void RangeFilter_leaves_out_what_the_editor_did_not_set_so_the_JavaScript_defaults_apply()
    {
        var component = new RangeFilterWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new RangeFilterWidgetProperties
        {
            Index = Index,
            Attribute = " publishedAt ",
            Minimum = 1m,
            Maximum = 2m,
            Step = null
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("publishedAt"));
            Assert.That(config.TryGetProperty("step", out _), Is.False);
            Assert.That(config.TryGetProperty("label", out _), Is.False);
            Assert.That(config.TryGetProperty("labels", out _), Is.False);
        });
    }

    [Test]
    public void CategoryTree_emits_every_option_the_JavaScript_widget_takes()
    {
        var component = new CategoryTreeWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new CategoryTreeWidgetProperties
        {
            Index = Index,
            Attribute = "category",
            Label = "Categories",
            Limit = 5
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("categoryTree"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("category"));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("Categories"));
            Assert.That(config.GetProperty("limit").GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public void CategoryTree_tells_the_editor_to_pick_an_attribute()
    {
        var component = new CategoryTreeWidgetViewComponent(renderer, new FakeEditorContext(XpSearchEditorMode.Edit), catalog);

        var model = component.BuildModel(new CategoryTreeWidgetProperties { Index = Index });

        Expect.Multiple(() =>
        {
            Assert.That(model.Mount, Is.Null, "an unconfigured widget renders no mount");
            Assert.That(model.EditorMessage, Does.Contain("attribute").IgnoreCase);
        });
    }

    [Test]
    public void Pagination_style_picks_the_JavaScript_widget_rather_than_becoming_an_option()
    {
        var component = new PaginationWidgetViewComponent(renderer, editor, catalog);

        string numbered = Render(component, new PaginationWidgetProperties { Index = Index });
        string loadMore = Render(component, new PaginationWidgetProperties
        {
            Index = Index,
            Style = PaginationWidgetProperties.StyleLoadMore
        });

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(numbered, "data-xps-widget"), Is.EqualTo("pagination"));
            Assert.That(Rendered.Attribute(numbered, "data-xps-config"), Is.EqualTo("{}"));
            Assert.That(Rendered.Attribute(loadMore, "data-xps-widget"), Is.EqualTo("loadMore"));
        });
    }

    [Test]
    public void ResultStats_emits_its_text_template_and_empty_state_text()
    {
        var component = new ResultStatsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new ResultStatsWidgetProperties
        {
            Index = Index,
            TextTemplate = "{total} hits in {tookMs} ms",
            EmptyText = "Start typing."
        });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("resultStats"));
            Assert.That(config.GetProperty("textTemplate").GetString(), Is.EqualTo("{total} hits in {tookMs} ms"));
            Assert.That(config.GetProperty("emptyText").GetString(), Is.EqualTo("Start typing."));
        });
    }

    [Test]
    public void SortSelect_emits_the_valid_options_only()
    {
        var component = new SortSelectWidgetViewComponent(renderer, editor, catalog, SearchOptions());

        string markup = Render(component, new SortSelectWidgetProperties
        {
            Index = Index,
            SortOptions = "relevance;Most relevant\r\nnewest;Newest first\r\nprice_asc;Cheapest\r\nnonsense;Nope",
            Label = "Order by",
            HideLabel = true
        });

        var config = Rendered.Json(markup, "data-xps-config");
        var items = config.GetProperty("items").EnumerateArray().ToList();

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("sortSelect"));
            Assert.That(items.Select(item => item.GetProperty("value").GetString()),
                Is.EqualTo(new[] { "relevance", "newest", "price_asc" }));
            Assert.That(items[1].GetProperty("label").GetString(), Is.EqualTo("Newest first"));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("Order by"));
            Assert.That(config.GetProperty("hideLabel").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void Suggestions_emits_the_reserved_widget_name_with_the_contract_option_names()
    {
        var component = new SuggestionsWidgetViewComponent(renderer, editor, catalog);

        string markup = Render(component, new SuggestionsWidgetProperties { Index = Index, MaxItems = 8 });

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("suggestions"));
            Assert.That(config.GetProperty("mode").GetString(), Is.EqualTo("documents"));
            Assert.That(config.GetProperty("limit").GetInt32(), Is.EqualTo(8));
        });
    }

    [Test]
    public void All_widgets_of_one_instance_name_the_same_index_so_the_bootstrap_finds_it_on_any_mount()
    {
        var properties = new object[]
        {
            new SearchBoxWidgetProperties { Index = Index, InstanceId = "search-1" },
            new ResultsWidgetProperties { Index = Index, InstanceId = "search-1" },
            new FacetListWidgetProperties { Index = Index, InstanceId = "search-1", Attribute = "tags" }
        };

        var markup = new List<string>
        {
            Render(SearchBox(), (SearchBoxWidgetProperties)properties[0]),
            Render(new ResultsWidgetViewComponent(renderer, editor, catalog), (ResultsWidgetProperties)properties[1]),
            Render(new FacetListWidgetViewComponent(renderer, editor, catalog), (FacetListWidgetProperties)properties[2])
        };

        Expect.Multiple(() =>
        {
            foreach (string mount in markup)
            {
                Assert.That(Rendered.Attribute(mount, "data-xps-instance"), Is.EqualTo("search-1"));
                Assert.That(
                    Rendered.Json(mount, "data-xps-instance-config").GetProperty("index").GetString(),
                    Is.EqualTo(Index));
            }
        });
    }

    [Test]
    public void A_mount_without_instance_options_omits_the_attribute()
    {
        var mount = new XpSearchMount("myCompany.dropdownFacet", "default");
        mount.Config["attribute"] = "brand";

        string markup = Rendered.Html(renderer.Render(mount));

        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("data-xps-instance-config"));
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("myCompany.dropdownFacet"));
            Assert.That(
                JsonDocument.Parse(Rendered.Attribute(markup, "data-xps-config")).RootElement
                    .GetProperty("attribute").GetString(),
                Is.EqualTo("brand"));
        });
    }
}

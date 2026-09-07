using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

using XpSearch.Core.Options;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.TagHelpers;

using NUnit.Framework;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// Layer 2 (RZ-1 §1): the tag elements a Razor developer writes. Each one binds its attributes into
/// the widget's options record and renders the same mount element the Page Builder widget does -
/// <see cref="PageBuilderParityTests"/> pins that equality.
/// </summary>
[TestFixture]
internal sealed class TagHelperTests
{
    private const string Index = "site-content";

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index, "products");

    internal static IOptionsMonitor<XpSearchOptions> SearchOptions()
    {
        var options = new XpSearchOptions();
        options.Indexes[Index].SortKeys["newest"] = new SortKey("PublishedAt", Descending: true);

        return new StaticOptionsMonitor<XpSearchOptions>(options);
    }

    /// <summary>Runs a tag helper the way Razor would and returns what it wrote.</summary>
    internal static string Tag<TOptions>(
        XpSearchMountTagHelper<TOptions> helper,
        string tagName,
        IDictionary<object, object>? items = null)
        where TOptions : XpSearchMountOptions, new()
    {
        var context = new TagHelperContext([], items ?? new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput(
            tagName,
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.ProcessAsync(context, output).GetAwaiter().GetResult();

        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);

        return writer.ToString();
    }

    /// <summary>The items an <c>&lt;xps-search&gt;</c> publishes to the mounts inside it.</summary>
    private static IDictionary<object, object> Scope(XpSearchTagHelper search)
    {
        var items = new Dictionary<object, object>();
        var output = new TagHelperOutput(
            "xps-search",
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        search.Process(new TagHelperContext([], items, "scope"), output);
        Assert.That(output.TagName, Is.Null, "<xps-search> must render no element of its own");

        return items;
    }

    [Test]
    public void A_tag_elements_attributes_become_the_mount_config()
    {
        string markup = Tag(
            new FacetListTagHelper(renderer, catalog)
            {
                Index = Index,
                InstanceId = "search-1",
                Attribute = "contentType",
                Label = "Content type",
                Operator = "and",
                Limit = 5,
                ShowMore = true
            },
            "xps-facet-list");

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(markup, Does.StartWith("<div class=\"xps-mount\""));
            // The <xps-facet-list> element itself is gone; the class inside is the skeleton first
            // paint (SK-1), not the placeholder tag.
            Assert.That(markup, Does.Not.Contain("<xps-facet-list"), "the placeholder tag must not survive");
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("facetList"));
            Assert.That(Rendered.Attribute(markup, "data-xps-instance"), Is.EqualTo("search-1"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("contentType"));
            Assert.That(config.GetProperty("operator").GetString(), Is.EqualTo("and"));
            Assert.That(config.GetProperty("limit").GetInt32(), Is.EqualTo(5));
            Assert.That(config.GetProperty("showMore").GetBoolean(), Is.True);
            Assert.That(
                Rendered.Json(markup, "data-xps-instance-config").GetProperty("index").GetString(),
                Is.EqualTo(Index));
        });
    }

    [Test]
    public void A_results_tag_that_states_no_page_size_takes_the_scopes_and_its_own_still_wins()
    {
        var scope = Scope(new XpSearchTagHelper { Index = Index, PageSize = 24 });

        string fromScope = Tag(new ResultsTagHelper(renderer, catalog), "xps-results", scope);
        string own = Tag(new ResultsTagHelper(renderer, catalog) { ResultsPerPage = 6 }, "xps-results", scope);
        string bare = Tag(new ResultsTagHelper(renderer, catalog) { Index = Index }, "xps-results");

        static int PageSize(string markup) =>
            Rendered.Json(markup, "data-xps-instance-config").GetProperty("initialState").GetProperty("pageSize").GetInt32();

        Expect.Multiple(() =>
        {
            Assert.That(PageSize(fromScope), Is.EqualTo(24), "the scope's page-size must reach a results tag that said nothing");
            Assert.That(PageSize(own), Is.EqualTo(6), "the tag's own results-per-page must beat the scope");
            Assert.That(PageSize(bare), Is.EqualTo(ResultsOptions.DefaultResultsPerPage), "no scope, no attribute: the code default");
        });
    }

    [Test]
    public void An_options_record_can_be_handed_over_whole_and_an_attribute_still_wins()
    {
        var record = new FacetListOptions { Index = Index, Attribute = "tags", Label = "Tags", Limit = 3 };

        string fromRecord = Tag(new FacetListTagHelper(renderer, catalog) { Options = record }, "xps-facet-list");
        string overridden = Tag(
            new FacetListTagHelper(renderer, catalog) { Options = record, Limit = 25 },
            "xps-facet-list");

        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Json(fromRecord, "data-xps-config").GetProperty("limit").GetInt32(), Is.EqualTo(3));
            Assert.That(Rendered.Json(overridden, "data-xps-config").GetProperty("limit").GetInt32(), Is.EqualTo(25));
            // The record the view handed over is not mutated by the render.
            Assert.That(record.Limit, Is.EqualTo(3));
        });
    }

    [Test]
    public void The_enclosing_xps_search_supplies_the_index_and_the_instance()
    {
        var items = Scope(new XpSearchTagHelper { Index = Index, Instance = "search-1" });

        string inherited = Tag(new FacetListTagHelper(renderer, catalog) { Attribute = "tags" }, "xps-facet-list", items);
        string own = Tag(
            new FacetListTagHelper(renderer, catalog) { Attribute = "tags", Index = "products", InstanceId = "other" },
            "xps-facet-list",
            items);

        Expect.Multiple(() =>
        {
            Assert.That(
                Rendered.Json(inherited, "data-xps-instance-config").GetProperty("index").GetString(),
                Is.EqualTo(Index));
            Assert.That(Rendered.Attribute(inherited, "data-xps-instance"), Is.EqualTo("search-1"));
            // A tag's own attributes beat the scope.
            Assert.That(
                Rendered.Json(own, "data-xps-instance-config").GetProperty("index").GetString(),
                Is.EqualTo("products"));
            Assert.That(Rendered.Attribute(own, "data-xps-instance"), Is.EqualTo("other"));
        });
    }

    [Test]
    public void The_enclosing_xps_search_supplies_the_instance_wide_options_without_overruling_a_widget()
    {
        var items = Scope(new XpSearchTagHelper
        {
            Index = Index,
            Routing = false,
            PageSize = 30,
            Fields = ["title", "url"]
        });

        var instance = Rendered.Json(
            Tag(new FacetListTagHelper(renderer, catalog) { Attribute = "tags" }, "xps-facet-list", items),
            "data-xps-instance-config");

        // The results widget owns its page size, so the scope's must not overwrite it.
        var results = Rendered.Json(
            Tag(new ResultsTagHelper(renderer, catalog) { ResultsPerPage = 12 }, "xps-results", items),
            "data-xps-instance-config");

        Expect.Multiple(() =>
        {
            Assert.That(instance.GetProperty("routing").GetBoolean(), Is.False);
            Assert.That(instance.GetProperty("initialState").GetProperty("pageSize").GetInt32(), Is.EqualTo(30));
            Assert.That(
                instance.GetProperty("fields").EnumerateArray().Select(field => field.GetString()),
                Is.EqualTo(new[] { "title", "url" }));
            Assert.That(results.GetProperty("initialState").GetProperty("pageSize").GetInt32(), Is.EqualTo(12));
        });
    }

    [Test]
    public void The_only_index_of_a_project_is_used_when_no_one_named_one()
    {
        string markup = Tag(
            new FacetListTagHelper(renderer, new FakeIndexCatalog("only-index")) { Attribute = "tags" },
            "xps-facet-list");

        Assert.That(
            Rendered.Json(markup, "data-xps-instance-config").GetProperty("index").GetString(),
            Is.EqualTo("only-index"));
    }

    [Test]
    public void An_unrenderable_tag_throws_where_the_developer_can_see_it()
    {
        var noIndex = Assert.Throws<InvalidOperationException>(new Action(
            () => Tag(new FacetListTagHelper(renderer, catalog) { Attribute = "tags" }, "xps-facet-list")));
        var noAttribute = Assert.Throws<InvalidOperationException>(new Action(
            () => Tag(new FacetListTagHelper(renderer, catalog) { Index = Index }, "xps-facet-list")));
        var noBounds = Assert.Throws<InvalidOperationException>(new Action(
            () => Tag(
                new RangeFilterTagHelper(renderer, catalog) { Index = Index, Attribute = "price" },
                "xps-range-filter")));

        Expect.Multiple(() =>
        {
            // Two indexes and none named: the same message an editor gets.
            Assert.That(noIndex!.Message, Does.Contain("Select a search index"));
            Assert.That(noAttribute!.Message, Does.Contain("attribute"));
            Assert.That(noBounds!.Message, Does.Contain("Minimum"));
        });
    }

    [Test]
    public void The_toggle_filter_tag_carries_the_single_value_it_switches_on()
    {
        string markup = Tag(
            new ToggleFilterTagHelper(renderer, catalog)
            {
                Index = Index,
                Attribute = "language",
                Value = "en",
                Label = "English only",
                ShowCount = false
            },
            "xps-toggle-filter");

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("toggleFilter"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("language"));
            Assert.That(config.GetProperty("value").GetString(), Is.EqualTo("en"));
            Assert.That(config.GetProperty("label").GetString(), Is.EqualTo("English only"));
            Assert.That(config.GetProperty("showCount").GetBoolean(), Is.False);
        });

        // The JavaScript default is the same "true", so an untouched tag still filters a flag.
        var plain = Rendered.Json(
            Tag(new ToggleFilterTagHelper(renderer, catalog) { Index = Index, Attribute = "isFeatured" }, "xps-toggle-filter"),
            "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(plain.GetProperty("value").GetString(), Is.EqualTo("true"));
            Assert.That(plain.GetProperty("showCount").GetBoolean(), Is.True);
            Assert.That(plain.TryGetProperty("label", out _), Is.False);
        });

        var noAttribute = Assert.Throws<InvalidOperationException>(new Action(
            () => Tag(new ToggleFilterTagHelper(renderer, catalog) { Index = Index }, "xps-toggle-filter")));
        Assert.That(noAttribute!.Message, Does.Contain("attribute"));
    }

    [Test]
    public void The_load_more_tag_emits_only_what_departs_from_the_JavaScript_defaults()
    {
        string configured = Tag(
            new LoadMoreTagHelper(renderer, catalog)
            {
                Index = Index,
                AutoLoad = false,
                TitleAttribute = " heading ",
                UrlAttribute = "permalink",
                SnippetAttributes = ["teaser", "excerpt"],
                MoreLabel = "Show more coffee",
                ExhaustedLabel = "That is all of it"
            },
            "xps-load-more");

        var config = Rendered.Json(configured, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(configured, "data-xps-widget"), Is.EqualTo("loadMore"));
            Assert.That(config.GetProperty("autoLoad").GetBoolean(), Is.False);
            Assert.That(config.GetProperty("titleAttribute").GetString(), Is.EqualTo("heading"));
            Assert.That(config.GetProperty("urlAttribute").GetString(), Is.EqualTo("permalink"));
            Assert.That(
                config.GetProperty("snippetAttributes").EnumerateArray().Select(name => name.GetString()),
                Is.EqualTo(new[] { "teaser", "excerpt" }));
            Assert.That(config.GetProperty("labels").GetProperty("more").GetString(), Is.EqualTo("Show more coffee"));
            Assert.That(config.GetProperty("labels").GetProperty("exhausted").GetString(), Is.EqualTo("That is all of it"));
        });

        // Scrolling loads the next page out of the box, so an untouched tag carries nothing at all.
        Assert.That(
            Rendered.Attribute(Tag(new LoadMoreTagHelper(renderer, catalog) { Index = Index }, "xps-load-more"), "data-xps-config"),
            Is.EqualTo("{}"));
    }

    [Test]
    public void The_generic_widget_tag_mounts_any_registered_JavaScript_widget()
    {
        string markup = Tag(
            new XpSearchWidgetTagHelper(renderer, catalog)
            {
                Index = Index,
                Type = "myCompany.dropdownFacet",
                Config = new { attribute = "brand", allLabel = "Any brand" },
                InstanceConfig = new { routing = false }
            },
            "xps-widget");

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(Rendered.Attribute(markup, "data-xps-widget"), Is.EqualTo("myCompany.dropdownFacet"));
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("brand"));
            Assert.That(config.GetProperty("allLabel").GetString(), Is.EqualTo("Any brand"));
            Assert.That(
                Rendered.Json(markup, "data-xps-instance-config").GetProperty("routing").GetBoolean(),
                Is.False);
        });

        var unnamed = Assert.Throws<InvalidOperationException>(new Action(
            () => Tag(new XpSearchWidgetTagHelper(renderer, catalog) { Index = Index }, "xps-widget")));
        Assert.That(unnamed!.Message, Does.Contain("type"));
    }

    [Test]
    public void A_dictionary_config_reaches_the_mount_key_for_key()
    {
        string markup = Tag(
            new XpSearchWidgetTagHelper(renderer, catalog)
            {
                Index = Index,
                Type = "myCompany.dropdownFacet",
                Config = new Dictionary<string, object?> { ["attribute"] = "brand", ["max"] = 3 }
            },
            "xps-widget");

        var config = Rendered.Json(markup, "data-xps-config");
        Expect.Multiple(() =>
        {
            Assert.That(config.GetProperty("attribute").GetString(), Is.EqualTo("brand"));
            Assert.That(config.GetProperty("max").GetInt32(), Is.EqualTo(3));
        });
    }
}

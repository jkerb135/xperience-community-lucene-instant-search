using CMS.Websites.Routing;

using Kentico.Content.Web.Mvc.Routing;

using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Widgets.Components.Widgets.XpSearch;
using XpSearch.Widgets.Mounting;
using XpSearch.Widgets.Options;
using XpSearch.Widgets.TagHelpers;

namespace XpSearch.Widgets.Tests;

/// <summary>
/// The page decides (LC-1): every mount carries the language and the website channel of the page it
/// sits on, unless the tag, the options record or the enclosing <c>&lt;xps-search&gt;</c> said
/// otherwise.
/// </summary>
[TestFixture]
internal sealed class LanguageAndChannelTests
{
    private const string Index = "site-content";

    private readonly XpSearchMountRenderer renderer = new();
    private readonly FakeIndexCatalog catalog = new(Index);

    [Test]
    public void The_page_language_and_channel_reach_every_mount()
    {
        var config = InstanceConfig(Box(new FakePageContext("es", "DancingGoat")));

        Expect.Multiple(() =>
        {
            Assert.That(config.GetProperty("language").GetString(), Is.EqualTo("es"));
            Assert.That(config.GetProperty("channel").GetString(), Is.EqualTo("DancingGoat"));
        });
    }

    [Test]
    public void Outside_a_website_channel_request_the_keys_are_absent()
    {
        Expect.Multiple(() =>
        {
            var unknown = InstanceConfig(Box(new FakePageContext()));
            Assert.That(unknown.TryGetProperty("language", out _), Is.False, "an unknown language searches every language");
            Assert.That(unknown.TryGetProperty("channel", out _), Is.False, "an unknown channel searches every channel");

            var none = InstanceConfig(Box(pageContext: null));
            Assert.That(none.TryGetProperty("language", out _), Is.False, "no page to ask at all");
            Assert.That(none.TryGetProperty("channel", out _), Is.False);
        });
    }

    [Test]
    public void The_tag_attribute_beats_the_options_record_which_beats_the_scope_which_beats_the_page()
    {
        var page = new FakePageContext("es", "DancingGoat");

        Expect.Multiple(() =>
        {
            var scoped = InstanceConfig(Box(page), Scope(language: "fr", channel: "Partner"));
            Assert.That((scoped.GetProperty("language").GetString(), scoped.GetProperty("channel").GetString()), Is.EqualTo(("fr", "Partner")));

            var box = Box(page);
            box.Options = new SearchBoxOptions { Language = "de", Channel = "Records" };
            var record = InstanceConfig(box, Scope(language: "fr", channel: "Partner"));
            Assert.That((record.GetProperty("language").GetString(), record.GetProperty("channel").GetString()), Is.EqualTo(("de", "Records")));

            var attributed = Box(page);
            attributed.Options = new SearchBoxOptions { Language = "de", Channel = "Records" };
            attributed.Language = "en";
            attributed.Channel = "Store";
            var own = InstanceConfig(attributed, Scope(language: "fr", channel: "Partner"));
            Assert.That((own.GetProperty("language").GetString(), own.GetProperty("channel").GetString()), Is.EqualTo(("en", "Store")));
        });
    }

    [Test]
    public void A_star_searches_every_language_and_every_channel()
    {
        var box = Box(new FakePageContext("es", "DancingGoat"));
        box.Language = "*";
        box.Channel = "*";

        var config = InstanceConfig(box);

        Expect.Multiple(() =>
        {
            Assert.That(config.TryGetProperty("language", out _), Is.False);
            Assert.That(config.TryGetProperty("channel", out _), Is.False);
        });
    }

    [Test]
    public void The_widget_asks_once_per_render_whether_the_index_covers_what_it_searches()
    {
        var page = new FakePageContext("es", "DancingGoat");

        TagHelperTests.Tag(Box(page), "xps-search-box");

        Assert.That(page.Checks, Is.EqualTo(new[] { $"{Index}|es|DancingGoat" }).AsCollection);
    }

    [Test]
    public void A_Page_Builder_widget_and_a_tag_still_render_the_same_bytes()
    {
        var page = new FakePageContext("es", "DancingGoat");
        var properties = new SearchBoxWidgetProperties { Index = Index, Placeholder = "Find coffee" };

        var component = new SearchBoxWidgetViewComponent(Box(page), new FakeEditorContext(XpSearchEditorMode.Live));
        var model = component.BuildModel(properties);

        var tagHelper = Box(page);
        tagHelper.Options = component.ToOptions(properties);

        Assert.That(TagHelperTests.Tag(tagHelper, "xps-search-box"), Is.EqualTo(Rendered.Html(model.Mount!)));
    }

    [Test]
    public async Task The_index_picker_lists_the_indexes_of_this_channel_first()
    {
        var page = new FakePageContext("es", "DancingGoat");
        page.NotCovering.Add("partner-content");

        var provider = new XpSearchIndexOptionsProvider(new FakeIndexCatalog("partner-content", "site-content"), page);
        var items = (await provider.GetOptionItems()).ToList();

        var everywhere = new XpSearchIndexOptionsProvider(new FakeIndexCatalog("partner-content", "site-content"), new FakePageContext());

        Expect.Multiple(() =>
        {
            Assert.That(items.Select(item => item.Value), Is.EqualTo(new[] { "site-content", "partner-content" }).AsCollection);
            Assert.That(items.Select(item => item.Text), Is.EqualTo(new[] { "site-content", "partner-content (other channel)" }).AsCollection);
            Assert.That(
                everywhere.GetOptionItems().GetAwaiter().GetResult().Select(item => item.Text),
                Is.EqualTo(new[] { "partner-content", "site-content" }).AsCollection,
                "outside a website channel request every index is listed as before");
        });
    }

    [Test]
    public async Task The_page_context_warns_once_when_the_index_does_not_cover_what_the_page_searches()
    {
        var logger = new RecordingLogger<KenticoPageContext>();
        var context = PageContext(logger, language: "es", channel: "DancingGoat", covers: new IndexDefinition("wrong", ["Partner"], ["en"], [], []));

        await context.WarnIfNotCoveredAsync("wrong", "es", "DancingGoat", CancellationToken.None);
        await context.WarnIfNotCoveredAsync("wrong", "es", "DancingGoat", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(logger.Warnings, Has.Count.EqualTo(2), "one for the language, one for the channel, and neither repeated");
            Assert.That(logger.Warnings[0], Does.Contain("language").And.Contain("es").And.Contain("wrong"));
            Assert.That(logger.Warnings[1], Does.Contain("website channel").And.Contain("DancingGoat"));
        });
    }

    [Test]
    public async Task An_index_that_covers_the_page_is_silent_and_answers_the_picker()
    {
        var logger = new RecordingLogger<KenticoPageContext>();
        var definition = new IndexDefinition("right", ["DancingGoat"], ["en", "es"], [], []);
        var context = PageContext(logger, language: "es", channel: "DancingGoat", covers: definition);

        await context.WarnIfNotCoveredAsync("right", "es", "DancingGoat", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(logger.Warnings, Is.Empty);
            Assert.That(context.CoversCurrentChannelAsync("right", CancellationToken.None).GetAwaiter().GetResult(), Is.True);
            Assert.That(context.GetLanguage(), Is.EqualTo("es"));
            Assert.That(context.GetChannel(), Is.EqualTo("DancingGoat"));
        });
    }

    [Test]
    public async Task An_unreadable_definition_hides_nothing_and_says_nothing()
    {
        var logger = new RecordingLogger<KenticoPageContext>();
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        accessor.GetDefinitionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IndexDefinition>>(_ => throw new InvalidOperationException("no such index"));

        var context = new KenticoPageContext(Languages("es"), Channels("DancingGoat"), accessor, logger);

        await context.WarnIfNotCoveredAsync("missing", "es", "DancingGoat", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(logger.Warnings, Is.Empty);
            Assert.That(context.CoversCurrentChannelAsync("missing", CancellationToken.None).GetAwaiter().GetResult(), Is.True);
        });
    }

    [Test]
    public void A_request_outside_a_website_channel_yields_no_defaults_and_never_throws()
    {
        var languages = Substitute.For<IPreferredLanguageRetriever>();
        languages.Get().Returns(_ => throw new InvalidOperationException("no channel"));

        var context = new KenticoPageContext(
            languages,
            Channels(string.Empty),
            Substitute.For<ILuceneIndexAccessor>(),
            new RecordingLogger<KenticoPageContext>());

        Expect.Multiple(() =>
        {
            Assert.That(context.GetLanguage(), Is.Null);
            Assert.That(context.GetChannel(), Is.Null);
        });
    }

    private static KenticoPageContext PageContext(
        ILogger<KenticoPageContext> logger,
        string language,
        string channel,
        IndexDefinition covers)
    {
        var accessor = Substitute.For<ILuceneIndexAccessor>();
        accessor.GetDefinitionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(covers));

        return new KenticoPageContext(Languages(language), Channels(channel), accessor, logger);
    }

    private static IPreferredLanguageRetriever Languages(string language)
    {
        var languages = Substitute.For<IPreferredLanguageRetriever>();
        languages.Get().Returns(language);

        return languages;
    }

    private static IWebsiteChannelContext Channels(string channel)
    {
        var channels = Substitute.For<IWebsiteChannelContext>();
        channels.WebsiteChannelName.Returns(channel);

        return channels;
    }

    private SearchBoxTagHelper Box(IXpSearchPageContext? pageContext) =>
        new(renderer, catalog) { Index = Index, PageContext = pageContext };

    /// <summary>The items an <c>&lt;xps-search language channel&gt;</c> publishes to the mounts inside it.</summary>
    private static IDictionary<object, object> Scope(string? language, string? channel)
    {
        var items = new Dictionary<object, object>();
        var output = new TagHelperOutput(
            "xps-search",
            [],
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        new XpSearchTagHelper { Language = language, Channel = channel }
            .Process(new TagHelperContext([], items, "scope"), output);

        return items;
    }

    private static System.Text.Json.JsonElement InstanceConfig(
        SearchBoxTagHelper helper,
        IDictionary<object, object>? scope = null) =>
        Rendered.Json(TagHelperTests.Tag(helper, "xps-search-box", scope), "data-xps-instance-config");
}

/// <summary>Keeps the warnings a component logged, formatted.</summary>
/// <typeparam name="T">The component the logger belongs to.</typeparam>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    internal List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}

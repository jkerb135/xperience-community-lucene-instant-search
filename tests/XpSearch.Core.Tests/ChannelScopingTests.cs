using CMS.Websites.Routing;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Core.Abstractions;
using XpSearch.Core.Analytics;
using XpSearch.Core.Contract;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Rendering;
using XpSearch.Core.Search;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Channel scoping (LC-1): the channel is a document field, a request member and a term filter, so a
/// page searches its own channel while one index still covers several. The fixture corpus has two
/// channels and one document in none, which is what a reusable item looks like.
/// </summary>
[TestFixture]
internal sealed class ChannelScopingTests
{
    private TestHarness harness = null!;

    [SetUp]
    public void SetUp() => harness = new TestHarness();

    [TearDown]
    public void TearDown() => harness.Dispose();

    [Test]
    public async Task Channel_NarrowsTheResultsToThatChannel()
    {
        var request = TestHarness.Request();
        request.Channel = TestCorpus.PartnerChannel;

        var response = await harness.Search(request);

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-4:en", "doc-5:en" }),
            "the other channel's pages and the document with no channel are excluded");
    }

    [Test]
    public async Task Channel_CombinesWithTheLanguageFilter()
    {
        var request = TestHarness.Request("espresso");
        request.Channel = TestCorpus.MainChannel;
        request.Language = "de";

        var response = await harness.Search(request);

        Assert.That(response.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-6:de" }).AsCollection);
    }

    [Test]
    public async Task Channel_IsCountableAndRespectedByTheOtherFacetCounts()
    {
        var counted = TestHarness.Request();
        counted.Facets = [TestCorpus.ChannelField, TestCorpus.ContentTypeField];

        var all = await harness.Search(counted);

        var scoped = TestHarness.Request();
        scoped.Facets = [TestCorpus.ContentTypeField];
        scoped.Channel = TestCorpus.PartnerChannel;

        var narrowed = await harness.Search(scoped);

        Expect.Multiple(() =>
        {
            Assert.That(all.Facets![TestCorpus.ChannelField].Single(value => value.Value == TestCorpus.MainChannel).Count, Is.EqualTo(4));
            Assert.That(all.Facets[TestCorpus.ChannelField].Single(value => value.Value == TestCorpus.PartnerChannel).Count, Is.EqualTo(2));
            Assert.That(all.Facets[TestCorpus.ContentTypeField].Single(value => value.Value == "Product").Count, Is.EqualTo(3));
            Assert.That(
                narrowed.Facets![TestCorpus.ContentTypeField].Single(value => value.Value == "Product").Count,
                Is.EqualTo(2),
                "a channel filter is a filter clause, so the facet counts respect it like the language");
        });
    }

    [Test]
    public void EmptyChannel_IsRejectedLikeAnyOtherBadRequest()
    {
        var request = TestHarness.Request();
        request.Channel = "   ";

        var failure = Expect.ThrowsAsync<SearchValidationException>(() => harness.Search(request));

        Assert.That(failure.Message, Does.Contain("channel"));
    }

    [Test]
    public async Task ChannelTheIndexDoesNotCover_MatchesNothingRatherThanFailing()
    {
        var request = TestHarness.Request();
        request.Channel = "NoSuchChannel";

        var response = await harness.Search(request);

        Assert.That(response.Total, Is.Zero);
    }

    [Test]
    public void CacheKey_DiffersByChannel()
    {
        var first = TestHarness.Request("espresso");
        first.Channel = TestCorpus.MainChannel;

        var second = TestHarness.Request("espresso");
        second.Channel = TestCorpus.PartnerChannel;

        Expect.Multiple(() =>
        {
            Assert.That(
                Caching.SearchCacheKey.Compute(first, "espresso"),
                Is.Not.EqualTo(Caching.SearchCacheKey.Compute(second, "espresso")));
            Assert.That(
                Caching.SearchCacheKey.Compute(first, "espresso"),
                Is.Not.EqualTo(Caching.SearchCacheKey.Compute(TestHarness.Request("espresso"), "espresso")),
                "a channel-scoped response must not be served to a request that asked for every channel");
        });
    }

    [Test]
    public async Task Suggest_FiltersByChannel()
    {
        using var index = new TestSearchIndex(TestCorpus.IndexName, TestCorpus.Documents);
        var options = new XpSearchOptions();
        options.Indexes[TestCorpus.IndexName].SuggestField = IndexSchemaProvider.TitleAttribute;

        var service = new DocumentSuggestService(
            index,
            new StaticSchemaProvider(TestCorpus.Schema),
            new FakeQuerySuggestionSource(),
            new StaticOptionsMonitor<XpSearchOptions>(options),
            new PerIndexSettings(options),
            NullLogger<DocumentSuggestService>.Instance);

        var response = await service.SuggestAsync(
            new SuggestRequest { Index = TestCorpus.IndexName, Query = "espresso", Channel = TestCorpus.PartnerChannel },
            CancellationToken.None);

        Assert.That(response.Suggestions.Select(suggestion => suggestion.Text), Is.Empty, "no partner-channel title starts with 'espresso'");
    }

    [Test]
    public void Journal_LogsTheRequestedChannel_AndFallsBackToTheCurrentOne()
    {
        var queue = new RecordingQueryLogQueue();
        var context = Substitute.For<IWebsiteChannelContext>();
        context.WebsiteChannelName.Returns("TheCurrentChannel");

        var journal = new SearchRequestJournal(
            Substitute.For<ISearchActivityLogger>(),
            new QueryContextMap(),
            queue,
            context,
            NullLogger<SearchRequestJournal>.Instance);

        journal.Record("q-1", "espresso", TestCorpus.IndexName, 1, TimeSpan.Zero, "en", channel: TestCorpus.PartnerChannel);
        journal.Record("q-2", "espresso", TestCorpus.IndexName, 1, TimeSpan.Zero, "en");

        Assert.That(
            queue.Items.Select(item => item.Entry!.ChannelName),
            Is.EqualTo(new[] { TestCorpus.PartnerChannel, "TheCurrentChannel" }).AsCollection);
    }

    [Test]
    public void SearchQueryState_TakesTheChannelAndLanguageThePageDecided_UnlessTheUrlSaysOtherwise()
    {
        Expect.Multiple(() =>
        {
            var decided = Parse(string.Empty, "es", TestCorpus.MainChannel);
            Assert.That((decided.Language, decided.Channel), Is.EqualTo(("es", TestCorpus.MainChannel)));

            var overridden = Parse("?language=de&channel=PartnerSite", "es", TestCorpus.MainChannel);
            Assert.That((overridden.Language, overridden.Channel), Is.EqualTo(("de", "PartnerSite")));

            var neither = Parse(string.Empty);
            Assert.That((neither.Language, neither.Channel), Is.EqualTo(((string?)null, (string?)null)));

            var facets = Parse("?language=de&channel=PartnerSite");
            Assert.That(facets.Filters, Is.Null, "language and channel are request members, not facet filters");
        });
    }

    private static SearchRequest Parse(string queryString, string? language = null, string? channel = null)
    {
        var request = new SearchRequest { Index = TestCorpus.IndexName };

        SearchQueryState.Apply(
            request,
            new QueryCollection(QueryHelpers.ParseQuery(queryString)),
            TestCorpus.Schema,
            language,
            channel);

        return request;
    }
}

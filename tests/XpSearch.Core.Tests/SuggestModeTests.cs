using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Search;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// The suggest modes (SG-1): the mixed response's deterministic limit split, the <c>group</c> every
/// suggestion now carries, and the two single-source modes pinned to what they answered before.
/// </summary>
[TestFixture]
internal sealed class SuggestModeTests
{
    private TestSearchIndex index = null!;
    private FakeQuerySuggestionSource queries = null!;
    private XpSearchOptions options = null!;
    private DocumentSuggestService service = null!;

    [SetUp]
    public void Build()
    {
        index = new TestSearchIndex(TestCorpus.IndexName, TestCorpus.Documents);
        queries = new FakeQuerySuggestionSource();
        options = new XpSearchOptions();
        service = new DocumentSuggestService(
            index,
            new StaticSchemaProvider(TestCorpus.Schema),
            queries,
            Microsoft.Extensions.Options.Options.Create(options));
    }

    [TearDown]
    public void Drop() => index.Dispose();

    /// <summary>
    /// The split, expressed as the source list sizes and the resulting counts. Queries take half of
    /// the limit (at least one), documents fill the rest, and either source hands its unused share
    /// to the other.
    /// </summary>
    [TestCase(5, 3, 3, 2, 3, TestName = "Mix_GivesQueriesHalfAndDocumentsTheRest")]
    [TestCase(5, 1, 9, 1, 4, TestName = "Mix_BackfillsDocumentsIntoTheQueriesUnusedShare")]
    [TestCase(5, 9, 1, 4, 1, TestName = "Mix_BackfillsQueriesIntoTheDocumentsUnusedShare")]
    [TestCase(1, 2, 2, 1, 0, TestName = "Mix_KeepsOneQueryEvenWhenHalfTheLimitIsZero")]
    [TestCase(5, 0, 9, 0, 5, TestName = "Mix_IsAllDocumentsWhenNoQueryMatches")]
    [TestCase(5, 9, 0, 5, 0, TestName = "Mix_IsAllQueriesWhenNoDocumentMatches")]
    [TestCase(5, 1, 1, 1, 1, TestName = "Mix_NeverPadsBeyondWhatEitherSourceReturned")]
    public void Mix_SplitsTheLimitDeterministically(
        int limit,
        int queryCount,
        int documentCount,
        int expectedQueries,
        int expectedDocuments)
    {
        var mixed = DocumentSuggestService
            .Mix(Suggestions(queryCount, Group.Query), Suggestions(documentCount, Group.Document), limit)
            .ToList();

        Expect.Multiple(() =>
        {
            Assert.That(mixed.Count(entry => entry.Group == Group.Query), Is.EqualTo(expectedQueries));
            Assert.That(mixed.Count(entry => entry.Group == Group.Document), Is.EqualTo(expectedDocuments));
            Assert.That(mixed.Count, Is.LessThanOrEqualTo(limit));
            Assert.That(
                mixed.Take(expectedQueries).Select(entry => entry.Group),
                Is.All.EqualTo(Group.Query),
                "queries lead, matching the panel's visual order");
        });
    }

    [Test]
    public async Task Mixed_AnswersWithBothSourcesAndLabelsEachEntry()
    {
        options.Indexes[TestCorpus.IndexName].SuggestMode = SuggestMode.Mixed;
        queries.Suggestions.AddRange(["espresso machine", "espresso beans", "espresso cups"]);

        var response = await Suggest("espr", limit: 5);

        Expect.Multiple(() =>
        {
            Assert.That(response.Suggestions, Has.Length.EqualTo(5));
            Assert.That(
                response.Suggestions.Take(2).Select(entry => entry.Text),
                Is.EqualTo(new[] { "espresso machine", "espresso beans" }).AsCollection);
            Assert.That(response.Suggestions.Take(2).Select(entry => entry.Group), Is.All.EqualTo(Group.Query));
            Assert.That(response.Suggestions.Skip(2).Select(entry => entry.Group), Is.All.EqualTo(Group.Document));
            Assert.That(response.Suggestions.Skip(2).Select(entry => entry.Result), Is.All.Not.Null);
        });
    }

    [Test]
    public async Task Documents_StillAnswerWithDocumentsAlone_NowLabelled()
    {
        queries.Suggestions.Add("espresso machine");

        var response = await Suggest("espr", limit: 5);

        Expect.Multiple(() =>
        {
            Assert.That(response.Suggestions, Is.Not.Empty);
            Assert.That(response.Suggestions.Select(entry => entry.Group), Is.All.EqualTo(Group.Document));
            Assert.That(response.Suggestions.Select(entry => entry.Result), Is.All.Not.Null);
            Assert.That(response.Suggestions.Select(entry => entry.Text), Has.None.EqualTo("espresso machine"));
        });
    }

    [Test]
    public async Task QuerySuggestions_StillAnswerWithTextAlone_NowLabelled()
    {
        options.Indexes[TestCorpus.IndexName].SuggestMode = SuggestMode.QuerySuggestions;
        queries.Suggestions.AddRange(["espresso machine", "espresso beans"]);

        var response = await Suggest("espr", limit: 5);

        Expect.Multiple(() =>
        {
            Assert.That(
                response.Suggestions.Select(entry => entry.Text),
                Is.EqualTo(new[] { "espresso machine", "espresso beans" }).AsCollection);
            Assert.That(response.Suggestions.Select(entry => entry.Group), Is.All.EqualTo(Group.Query));
            Assert.That(response.Suggestions.Select(entry => entry.Result), Is.All.Null);
            Assert.That(response.Suggestions.Select(entry => entry.Url), Is.All.Null);
        });
    }

    [Test]
    public async Task Mixed_AnswersNothingForAnEmptyPrefix()
    {
        options.Indexes[TestCorpus.IndexName].SuggestMode = SuggestMode.Mixed;
        queries.Suggestions.Add("espresso machine");

        var response = await Suggest("  ", limit: 5);

        Assert.That(response.Suggestions, Is.Empty);
    }

    private Task<SuggestResponse> Suggest(string query, int limit) =>
        service.SuggestAsync(
            new SuggestRequest { Index = TestCorpus.IndexName, Query = query, Limit = limit },
            CancellationToken.None);

    private static Suggestion[] Suggestions(int count, Group group) =>
        [.. Enumerable.Range(0, count).Select(at => new Suggestion { Text = $"{group}-{at}", Group = group })];
}

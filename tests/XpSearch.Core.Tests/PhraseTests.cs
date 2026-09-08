using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using Lucene.Net.Util;

using NUnit.Framework;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>
/// Quoted phrases (PH-1): a balanced pair of double quotes is the one piece of syntax the endpoint
/// honours, and everything the pipeline already did to a query - fuzzing, synonyms, stopwords,
/// highlighting, caching, explain - has to keep working around it.
/// </summary>
[TestFixture]
internal sealed class PhraseTests
{
    /// <summary>
    /// The fixture corpus plus the pair the unit turns on: one document that says "french press" and
    /// one that holds both words far apart, in the wrong order.
    /// </summary>
    private static readonly TestDocument[] Corpus =
    [
        .. TestCorpus.Documents,
        new("doc-fp:en", "French Press", "A french press and a hand grinder for filter coffee.", "Product", "en", "/products/french-press", ["equipment"], ["brewing"], [], 39.5, 1_690_000_200),
        new("doc-pf:en", "Press Room", "Press releases about coffee, also available in french.", "Article", "en", "/articles/press-room", ["coffee"], [], [], null, 1_700_000_400)
    ];

    [Test]
    public async Task AQuotedPhrase_MatchesOnlyTheDocumentWithTheWordsAdjacent()
    {
        using var harness = new TestHarness(documents: Corpus);

        var response = await harness.Search(TestHarness.Request("\"french press\""));

        Assert.That(response.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-fp:en" }).AsCollection);
    }

    [Test]
    public async Task WithoutQuotes_TheWordsAreStillJustANDedTerms()
    {
        using var harness = new TestHarness(documents: Corpus);

        var response = await harness.Search(TestHarness.Request("french press"));

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-fp:en", "doc-pf:en" }),
            "the pre-PH-1 behaviour: both words anywhere in the document");
    }

    [Test]
    public async Task APhraseAndALooseTerm_AreBothRequired()
    {
        using var harness = new TestHarness(documents: Corpus);

        var both = await harness.Search(TestHarness.Request("\"french press\" grinder"));
        var missing = await harness.Search(TestHarness.Request("\"french press\" espresso"));

        Expect.Multiple(() =>
        {
            Assert.That(both.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-fp:en" }).AsCollection);
            Assert.That(missing.Results, Is.Empty, "the loose term is ANDed onto the phrase, not ORed");
        });
    }

    /// <summary>The shape of the query, which is what every other stage then sees.</summary>
    [Test]
    public void APhraseBecomesAPhraseQuery_AndTheLooseTermATermQuery()
    {
        var query = Build("\"french press\" grinder", fuzzy: false);
        var phrases = Flatten(query).OfType<PhraseQuery>().ToList();

        Expect.Multiple(() =>
        {
            Assert.That(phrases, Is.Not.Empty, "the parser must build a positional query, not two terms");
            Assert.That(
                phrases.Select(phrase => string.Join(' ', phrase.GetTerms().Select(term => term.Text))).Distinct(),
                Is.EqualTo(new[] { "french press" }).AsCollection,
                "in that order");
            Assert.That(
                phrases.Select(phrase => phrase.Slop).Distinct(),
                Is.EqualTo(new[] { 0 }).AsCollection,
                "adjacent: no proximity search is exposed");
            Assert.That(
                Flatten(query).OfType<TermQuery>().Select(term => term.Term.Text),
                Does.Contain("grinder"));
            Assert.That(
                phrases.Select(phrase => phrase.GetTerms()[0].Field).Distinct().Count(),
                Is.GreaterThan(1),
                "one phrase clause per searchable field, so the field weights still apply");
        });
    }

    [Test]
    public async Task AnUnbalancedQuote_IsALiteralCharacter()
    {
        using var harness = new TestHarness(documents: Corpus);

        var response = await harness.Search(TestHarness.Request("\"french press"));

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EquivalentTo(new[] { "doc-fp:en", "doc-pf:en" }),
            "no phrase, no syntax error: exactly the pre-PH-1 result");
    }

    [Test]
    public async Task AnEmptyPhrase_IsIgnored()
    {
        using var harness = new TestHarness(documents: Corpus);

        var empty = await harness.Search(TestHarness.Request("\"\""));
        var nothing = await harness.Search(TestHarness.Request(""));

        Assert.That(empty.Total, Is.EqualTo(nothing.Total).And.GreaterThan(0));
    }

    [Test]
    public async Task SmartQuotes_AreQuotes()
    {
        using var harness = new TestHarness(documents: Corpus);

        var response = await harness.Search(TestHarness.Request("“french press”"));

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EqualTo(new[] { "doc-fp:en" }).AsCollection,
            "visitors paste from word processors");
    }

    /// <summary>A phrase is exact by intent, so the FZ-1 suffix must not reach inside it.</summary>
    [Test]
    public void WithTypoToleranceOn_ThePhraseIsNotFuzzedButTheLooseTermIs()
    {
        var query = Build("\"french press\" grindr", fuzzy: true);

        Expect.Multiple(() =>
        {
            Assert.That(
                Flatten(query).OfType<FuzzyQuery>().Select(fuzzy => fuzzy.Term.Text).Distinct(),
                Is.EqualTo(new[] { "grindr" }).AsCollection);
            Assert.That(
                Flatten(query).OfType<PhraseQuery>().SelectMany(phrase => phrase.GetTerms()).Select(term => term.Text).Distinct(),
                Is.EqualTo(new[] { "french", "press" }).AsCollection);
        });
    }

    [Test]
    public async Task WithTypoToleranceOn_AMisspelledLooseTermStillFindsThePhrase()
    {
        using var harness = new TestHarness(typoTolerance: true, documents: Corpus);

        var response = await harness.Search(TestHarness.Request("\"french press\" grindr"));

        Assert.That(response.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-fp:en" }).AsCollection);
    }

    [Test]
    public async Task ASynonym_DoesNotStandInForAWordInsideAPhrase()
    {
        var tuning = new FakeTuningSource
        {
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["press", "machine"], [])]
        };

        using var harness = new TestHarness(tuning: tuning, documents: Corpus);

        var loose = await harness.Search(TestHarness.Request("press"));
        var phrase = await harness.Search(TestHarness.Request("\"french press\""));

        Expect.Multiple(() =>
        {
            Assert.That(loose.Results.Select(result => result.Id), Does.Contain("doc-3:en"), "the synonym works");
            Assert.That(
                phrase.Results.Select(result => result.Id),
                Is.EqualTo(new[] { "doc-fp:en" }).AsCollection,
                "but not inside the quotes: the espresso machine is not a french press");
        });
    }

    /// <summary>The loose text around a phrase is still expanded, and the phrase is still required.</summary>
    [Test]
    public async Task ASynonym_StillExpandsTheTextAroundAPhrase()
    {
        var tuning = new FakeTuningSource
        {
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["grinder", "mill"], [])]
        };

        using var harness = new TestHarness(tuning: tuning, documents: Corpus);

        var response = await harness.Search(TestHarness.Request("\"french press\" mill"));

        Assert.That(response.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-fp:en" }).AsCollection);
    }

    /// <summary>
    /// Stopword removal is a tuning list, not an analyzer: it may not take a word out of a phrase the
    /// visitor asked for, and it still takes it out of the loose text around it.
    /// </summary>
    [Test]
    public async Task StopwordRemoval_KeepsTheWordsInsideAPhrase()
    {
        var context = Context("the \"the press\" the coffee");
        context.Tuning = context.Tuning with { Stopwords = ["the"] };

        await new StopwordRemovalStage().ExecuteAsync(context, CancellationToken.None);

        Assert.That(context.QueryText, Is.EqualTo("\"the press\" coffee"));
    }

    [Test]
    public async Task Highlighting_MarksBothWordsOfThePhrase()
    {
        using var harness = new TestHarness(documents: Corpus);

        var request = TestHarness.Request("\"french press\"");
        request.Highlight = new HighlightOptions { Fields = [TestCorpus.BodyField] };

        var response = await harness.Search(request);
        string snippet = response.Results.Single().Highlights![TestCorpus.BodyField];

        Expect.Multiple(() =>
        {
            Assert.That(snippet, Does.Contain("<mark>french</mark>"));
            Assert.That(snippet, Does.Contain("<mark>press</mark>"));
        });
    }

    /// <summary>QT-2 explains every checkpoint against the document; a positional query is one.</summary>
    [Test]
    public async Task Explain_ScoresAPhraseQueryWithoutChoking()
    {
        using var harness = new TestHarness(documents: Corpus);

        var request = TestHarness.Request("\"french press\"");
        request.Explain = true;

        var response = await harness.Search(request);
        var ranking = response.Results.Single().Ranking;

        Expect.Multiple(() =>
        {
            Assert.That(ranking, Is.Not.Null);
            Assert.That(ranking!.Steps, Is.Not.Null.And.Not.Empty);
            Assert.That(ranking.Steps!.Sum(step => step.Score), Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// The cache key is a hash of the raw normalized text, so PH-1 adds nothing to it: a plain query
    /// keys exactly as before, and a quoted one - a different search - keys differently.
    /// </summary>
    [Test]
    public void TheCacheKey_IsUnchangedForAPlainQueryAndDiffersForAQuotedOne()
    {
        var request = new SearchRequest { Index = TestCorpus.IndexName, Query = "french press" };

        Expect.Multiple(() =>
        {
            Assert.That(
                SearchCacheKey.Compute(request, "french press"),
                Is.EqualTo(SearchCacheKey.Compute(request, "french press")));
            Assert.That(
                SearchCacheKey.Compute(request, "\"french press\""),
                Is.Not.EqualTo(SearchCacheKey.Compute(request, "french press")),
                "the two are different searches and must not share an entry");
        });
    }

    /// <summary>
    /// Tuning rules match the raw query text, quotes and all: a <c>contains</c> pattern still fires on
    /// a quoted query, while <c>is</c> and <c>starts with</c> only do when the marketer typed the
    /// quotes too. Nothing in PH-1 changes that - it is stated in the guide so nobody is surprised.
    /// </summary>
    [Test]
    public void ARule_MatchesTheRawTextIncludingTheQuotes()
    {
        var match = RuleMatchContext.ForQuery("\"french press\"");

        Expect.Multiple(() =>
        {
            Assert.That(Fires(QueryOperator.Contains, "french press", match), Is.True);
            Assert.That(Fires(QueryOperator.Is, "french press", match), Is.False);
            Assert.That(Fires(QueryOperator.Is, "\"french press\"", match), Is.True);
            Assert.That(Fires(QueryOperator.StartsWith, "french", match), Is.False, "the quote is the first character");
        });
    }

    private static bool Fires(QueryOperator op, string pattern, RuleMatchContext match) =>
        RuleSelection.Active(
            [
                new TuningRule(
                    1,
                    "rule",
                    true,
                    100,
                    null,
                    null,
                    new RuleConditions(new QueryCondition(op, pattern, false), [], string.Empty, string.Empty),
                    [new RuleAction.Redirect("/x")])
            ],
            match,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Count == 1;

    /// <summary>The cap is applied to the raw text, so a cut-off quote is simply unbalanced.</summary>
    [Test]
    public void MaxQueryLength_StillCapsTheRawText()
    {
        string normalized = NormalizeRequestStage.Normalize("\"french press\"", 9);

        Expect.Multiple(() =>
        {
            Assert.That(normalized, Is.EqualTo("\"french p"));
            Assert.That(Flatten(Build(normalized, fuzzy: false)).OfType<PhraseQuery>(), Is.Empty);
        });
    }

    private static SearchContext Context(string query) =>
        new(
            new SearchRequest { Index = TestCorpus.IndexName, Query = query },
            TestCorpus.Schema,
            new StandardAnalyzer(LuceneVersion.LUCENE_48),
            null,
            CancellationToken.None)
        {
            QueryText = query
        };

    private static Query Build(string query, bool fuzzy)
    {
        var context = Context(query);

        new BuildQueryStage(new FixedTypoToleranceSource(fuzzy))
            .ExecuteAsync(context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return context.BaseQuery;
    }

    private static IEnumerable<Query> Flatten(Query query) =>
        query is BooleanQuery boolean
            ? boolean.Clauses.SelectMany(clause => Flatten(clause.Query))
            : [query];
}

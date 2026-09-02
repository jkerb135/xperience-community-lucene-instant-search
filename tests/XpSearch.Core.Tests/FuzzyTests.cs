using CMS.FormEngine;

using Lucene.Net.Analysis.Standard;
using Lucene.Net.Search;
using Lucene.Net.Util;

using NUnit.Framework;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Fuzzy;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Tests.Fixtures;
using XpSearch.Core.Tuning;

namespace XpSearch.Core.Tests;

/// <summary>
/// Typo tolerance (FZ-1): the fixed length policy, the query both paths build, and the edges that make
/// it safe to ship - AND semantics, the response cache key, and highlighting a term nobody spelled right.
/// </summary>
[TestFixture]
internal sealed class FuzzyTests
{
    [TestCase("a", 0)]
    [TestCase("at", 0, Description = "two letters: almost every other short word is one edit away")]
    [TestCase("red", 1)]
    [TestCase("sofas", 1)]
    [TestCase("coffee", 2)]
    [TestCase("espresso", 2)]
    [TestCase("2024", 0, Description = "a wrong digit is a different value, not a misspelling")]
    [TestCase("15", 0)]
    [TestCase("a1b2c3", 2, Description = "only an all-digit token stays exact")]
    public void ThePolicy_ScalesTheEditDistanceWithTheTermLength(string token, int expected) =>
        Assert.That(FuzzyPolicy.MaxEdits(token), Is.EqualTo(expected));

    [Test]
    public void ThePolicy_RequiresTheFirstLetterToMatch() =>
        Assert.That(FuzzyPolicy.PrefixLength, Is.EqualTo(1));

    /// <summary>With the toggle off the query is byte-identical to the one built before FZ-1.</summary>
    [Test]
    public void WithTheToggleOff_NoTermIsFuzzy()
    {
        var query = Build("espresso machine", fuzzy: false);

        Assert.That(Fuzzies(query), Is.Empty);
    }

    [Test]
    public void WithTheToggleOn_EveryLongEnoughTermBecomesAFuzzyQuery()
    {
        var query = Build("espresso at 2024 machine", fuzzy: true);
        var terms = Fuzzies(query);

        Expect.Multiple(() =>
        {
            Assert.That(terms.Select(term => term.Term.Text).Distinct(), Is.EquivalentTo(new[] { "espresso", "machine" }));
            Assert.That(terms.Select(term => term.MaxEdits).Distinct(), Is.EqualTo(new[] { 2 }).AsCollection);
            Assert.That(terms.Select(term => term.PrefixLength).Distinct(), Is.EqualTo(new[] { FuzzyPolicy.PrefixLength }).AsCollection);
            Assert.That(
                Terms(query).Select(term => term.Term.Text),
                Does.Contain("2024"),
                "the all-digit token is still required, just exactly");
        });
    }

    [Test]
    public void WithTheToggleOn_AFiveLetterTermIsAllowedOneEditAndASixLetterTermTwo()
    {
        var edits = Fuzzies(Build("sofas coffee", fuzzy: true))
            .GroupBy(query => query.Term.Text)
            .ToDictionary(group => group.Key, group => group.First().MaxEdits, StringComparer.Ordinal);

        Expect.Multiple(() =>
        {
            Assert.That(edits["sofas"], Is.EqualTo(1));
            Assert.That(edits["coffee"], Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The escaping and the suffix must not fight: the visitor's own tilde stays a literal character of
    /// the term, and only the policy decides the distance.
    /// </summary>
    [Test]
    public void TheSuffixIsAppendedAfterEscaping_SoATypedTildeIsNotAnOperator()
    {
        var fuzzy = Fuzzies(Build("sofas~9", fuzzy: true)).First();

        Expect.Multiple(() =>
        {
            Assert.That(fuzzy.Term.Text, Is.EqualTo("sofas~9"), "the typed tilde is part of the term, escaped");
            Assert.That(fuzzy.MaxEdits, Is.EqualTo(2), "seven characters, so the policy allows two edits - not the nine that were typed");
        });
    }

    /// <summary>A query of nothing but operator characters still parses; escaping runs before the split.</summary>
    [Test]
    public void AQueryOfOperatorCharactersStillParses() =>
        Assert.That(Build("+ -", fuzzy: true), Is.Not.Null);

    [Test]
    public async Task AMisspelledTerm_FindsTheDocumentOnlyWithTheToggleOn()
    {
        using var off = new TestHarness();
        using var on = new TestHarness(typoTolerance: true);

        var strict = await off.Search(TestHarness.Request("expresso"));
        var tolerant = await on.Search(TestHarness.Request("expresso"));

        Expect.Multiple(() =>
        {
            Assert.That(strict.Results, Is.Empty, "nobody spelled it that way");
            Assert.That(tolerant.Results.Select(result => result.Id), Does.Contain("doc-1:en"));
        });
    }

    /// <summary>An exact hit still outranks a fuzzy one: <c>FuzzyQuery</c> discounts by distance.</summary>
    [Test]
    public async Task AnExactHitOutranksAFuzzyOne()
    {
        using var harness = new TestHarness(typoTolerance: true);

        var response = await harness.Search(TestHarness.Request("grinder"));

        Assert.That(response.Results.First().Id, Is.EqualTo("doc-4:en"), "the document that really says 'grinder'");
    }

    /// <summary>Typo tolerance widens what fills a position, never how many positions must be filled.</summary>
    [Test]
    public async Task EveryPositionIsStillRequired()
    {
        using var harness = new TestHarness(typoTolerance: true);

        var response = await harness.Search(TestHarness.Request("espresso machne"));

        Expect.Multiple(() =>
        {
            Assert.That(response.Results.Select(result => result.Id), Is.EqualTo(new[] { "doc-3:en" }).AsCollection);
            Assert.That(
                response.Results.Select(result => result.Id),
                Does.Not.Contain("doc-1:en"),
                "a document with only the first term must not match");
        });
    }

    /// <summary>The same on the synonym-slot path, which parses each alternative on its own.</summary>
    [Test]
    public async Task EveryPositionIsStillRequiredWhenSynonymsExpandedTheQuery()
    {
        var tuning = new FakeTuningSource
        {
            Synonyms = [new TuningSynonym(SynonymDirection.TwoWay, ["espresso", "coffee"], [])]
        };

        using var harness = new TestHarness(tuning: tuning, typoTolerance: true);

        var response = await harness.Search(TestHarness.Request("espresso machne"));

        Assert.That(
            response.Results.Select(result => result.Id),
            Is.EqualTo(new[] { "doc-3:en" }).AsCollection,
            "'coffee' fills the first position for doc-4, but nothing fills the second");
    }

    [Test]
    public void TheSynonymSlotPath_MakesEveryAlternativeFuzzyToo()
    {
        var context = Context("espresso machne");
        context.QuerySlots = [["espresso"], ["machne", "device"]];

        var fuzzy = Fuzzies(Build(context, fuzzy: true)).Select(query => query.Term.Text).Distinct();

        Assert.That(fuzzy, Is.EquivalentTo(new[] { "espresso", "machne", "device" }));
    }

    /// <summary>
    /// The STOP clause of the unit: <see cref="FuzzyQuery"/> is a <see cref="MultiTermQuery"/>, and a
    /// highlighter that does not rewrite it against the text silently stops highlighting the term that
    /// matched. A fuzzy-only hit must still come back with a snippet.
    /// </summary>
    [Test]
    public async Task AFuzzyOnlyHit_StillComesBackWithAHighlightedSnippet()
    {
        using var harness = new TestHarness(typoTolerance: true);

        var request = TestHarness.Request("expresso");
        request.Highlight = new HighlightOptions { Fields = [TestCorpus.BodyField] };

        var response = await harness.Search(request);
        var hit = response.Results.Single(result => result.Id == "doc-1:en");

        Expect.Multiple(() =>
        {
            Assert.That(hit.Highlights, Is.Not.Null, "the misspelled term matched, so the snippet must show why");
            Assert.That(hit.Highlights![TestCorpus.BodyField], Does.Contain("<mark>espresso</mark>"));
        });
    }

    /// <summary>
    /// HL-1: what the highlighter scores against must already be rewritten against the reader. An
    /// unrewritten <see cref="FuzzyQuery"/> makes the scorer re-expand it per document and per field
    /// (135 ms p50 at 10k docs in the PF-1 bench); the rewrite happens once, inside the searcher lease.
    /// </summary>
    [Test]
    public async Task TheHighlighter_ScoresAgainstTheRewrittenQuery_NotTheFuzzyOne()
    {
        var capture = new CaptureHighlightQueryStage();
        using var harness = new TestHarness(typoTolerance: true, extraStages: capture);

        var request = TestHarness.Request("expresso");
        request.Highlight = new HighlightOptions { Fields = [TestCorpus.BodyField] };

        await harness.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(capture.HighlightQuery, Is.Not.Null, "the execute stage prepares it while the lease is open");
            Assert.That(
                Flatten(capture.HighlightQuery!).OfType<MultiTermQuery>(),
                Is.Empty,
                "a multi-term query left in here is the per-document expansion coming back");
            Assert.That(
                Flatten(capture.HighlightQuery!).OfType<TermQuery>().Select(term => term.Term.Text),
                Does.Contain("espresso"),
                "the rewrite resolves the misspelling to the term that actually exists in the index");
        });
    }

    [Test]
    public async Task WithoutHighlighting_NoRewriteIsPrepared()
    {
        var capture = new CaptureHighlightQueryStage();
        using var harness = new TestHarness(typoTolerance: true, extraStages: capture);

        await harness.Search(TestHarness.Request("expresso"));

        Assert.That(capture.HighlightQuery, Is.Null, "nothing to highlight, nothing to rewrite for");
    }

    /// <summary>Captures the rewritten query as <c>HighlightStage</c> is about to see it.</summary>
    private sealed class CaptureHighlightQueryStage : ISearchStage
    {
        public Query? HighlightQuery { get; private set; }

        public int Order => SearchStageOrder.Highlight - 1;

        public Task ExecuteAsync(SearchContext context, CancellationToken cancellationToken)
        {
            HighlightQuery = context.HighlightQuery;

            return Task.CompletedTask;
        }
    }

    [Test]
    public void TheCacheKey_ChangesWithTheToggleAndOnlyWhenItIsOn()
    {
        var request = new SearchRequest { Index = "articles", Query = "espresso" };

        string off = SearchCacheKey.Compute(request, "espresso");
        string on = SearchCacheKey.Compute(request, "espresso", typoTolerance: true);

        Expect.Multiple(() =>
        {
            Assert.That(
                SearchCacheKey.Compute(request, "espresso", typoTolerance: false),
                Is.EqualTo(off),
                "a host that never turned it on keys exactly as before FZ-1");
            Assert.That(on, Is.Not.EqualTo(off), "flipping the toggle must never serve the other setting's cached page");
        });
    }

    [Test]
    public async Task Explain_SaysTypoToleranceWasApplied()
    {
        using var on = new TestHarness(typoTolerance: true);
        using var off = new TestHarness();

        var request = TestHarness.Request("espresso");
        request.Explain = true;

        var tolerant = await on.Search(request);
        var strict = await off.Search(request);

        Expect.Multiple(() =>
        {
            Assert.That(tolerant.Results.First().Ranking!.Boosts, Does.Contain("fuzzy:on"));
            Assert.That(strict.Results.First().Ranking!.Boosts ?? [], Does.Not.Contain("fuzzy:on"));
        });
    }

    /// <summary>The installed class has the columns the source reads the setting from.</summary>
    [Test]
    public void TheStorageHasTheColumnsTheSettingIsReadFrom() =>
        Assert.That(
            Analytics.XpSearchAnalyticsModuleInstaller.FuzzyIndexForm().GetFields(true, true).Select(field => field.Name),
            Is.SupersetOf(new[]
            {
                nameof(XpSearchFuzzyIndexInfo.FuzzyIndexGuid),
                nameof(XpSearchFuzzyIndexInfo.FuzzyIndexName),
                nameof(XpSearchFuzzyIndexInfo.FuzzyIndexEnabled)
            }));

    /// <summary>The setting is read through the seam, so Core without XpSearch.Admin answers "off".</summary>
    [Test]
    public async Task WithoutTheAdminPackage_TheSettingIsOff() =>
        Assert.That(await new DisabledTypoToleranceSource().IsEnabledAsync("articles", CancellationToken.None), Is.False);

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

    private static Query Build(string query, bool fuzzy) => Build(Context(query), fuzzy);

    private static Query Build(SearchContext context, bool fuzzy)
    {
        new BuildQueryStage(new FixedTypoToleranceSource(fuzzy))
            .ExecuteAsync(context, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return context.BaseQuery;
    }

    private static IReadOnlyList<FuzzyQuery> Fuzzies(Query query) => Flatten(query).OfType<FuzzyQuery>().ToList();

    private static IReadOnlyList<TermQuery> Terms(Query query) => Flatten(query).OfType<TermQuery>().ToList();

    private static IEnumerable<Query> Flatten(Query query) =>
        query is BooleanQuery boolean
            ? boolean.Clauses.SelectMany(clause => Flatten(clause.Query))
            : [query];
}

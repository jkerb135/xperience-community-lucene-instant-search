using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using XpSearch.Core.Analytics;
using XpSearch.Core.Options;
using XpSearch.Core.Popularity;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// Tests the mined synonyms of SY-1: which adjacent searches count as a reformulation, the occurrence
/// threshold, what a task run writes, and that an answered pair never comes back.
/// </summary>
[TestFixture]
internal sealed class SynonymMiningTests
{
    /// <summary>Inside the task's default lookback window, so the same rows serve the task run test.</summary>
    private static readonly DateTime Start = DateTime.UtcNow.AddDays(-1);

    [Test]
    public void AFailedSearchFollowedByAClickedOne_IsMinedOncePerOccurrence()
    {
        var pairs = SynonymMiner.Mine(
            [
                .. Reformulation("settee", "sofa", 0),
                .. Reformulation("settee", "sofa", 600),
                .. Reformulation("settee", "sofa", 1200)
            ],
            minimumOccurrences: 3,
            windowSeconds: 60);

        Expect.Multiple(() =>
        {
            Assert.That(pairs.Count, Is.EqualTo(1));
            Assert.That(pairs[0].FailedQuery, Is.EqualTo("settee"));
            Assert.That(pairs[0].SucceededQuery, Is.EqualTo("sofa"));
            Assert.That(pairs[0].Occurrences, Is.EqualTo(3));
            Assert.That(pairs[0].LastSeenUtc, Is.EqualTo(Start.AddSeconds(1210)), "last seen is the newest click of the pair");
        });
    }

    [Test]
    public void APairUnderTheThreshold_IsNotSuggested() =>
        Assert.That(
            SynonymMiner.Mine([.. Reformulation("settee", "sofa", 0), .. Reformulation("settee", "sofa", 600)], 3, 60),
            Is.Empty,
            "two visitors happening to interleave must not become a synonym");

    [Test]
    public void AClickOutsideTheWindow_IsNotTheSameReformulation() =>
        Assert.That(
            SynonymMiner.Mine(
                [
                    Miss("settee", 0),
                    Click("sofa", 90),
                    Miss("settee", 300),
                    Click("sofa", 390),
                    Miss("settee", 600),
                    Click("sofa", 690)
                ],
                3,
                60),
            Is.Empty);

    /// <summary>
    /// Prefix typing and narrowing look exactly like a reformulation in the log, and neither is a
    /// synonym; nor is the same phrase searched twice.
    /// </summary>
    [Test]
    public void ContainmentAndRepeats_AreNotReformulations()
    {
        Expect.Multiple(() =>
        {
            Assert.That(SynonymMiner.IsReformulation("coff", "coffee"), Is.False, "autocomplete typing");
            Assert.That(SynonymMiner.IsReformulation("sofa", "red sofa"), Is.False, "narrowing");
            Assert.That(SynonymMiner.IsReformulation("sofa", "sofa"), Is.False, "the same search again");
            Assert.That(SynonymMiner.IsReformulation("settee", "sofa"), Is.True);
        });
    }

    [Test]
    public void CaseAndSpacing_AreTheSameQuery()
    {
        var pairs = SynonymMiner.Mine(
            [
                .. Reformulation("  SETTEE ", "red  sofa", 0),
                .. Reformulation("settee", "RED SOFA", 600),
                .. Reformulation("Settee", "red sofa", 1200)
            ],
            3,
            60);

        Expect.Multiple(() =>
        {
            Assert.That(pairs.Count, Is.EqualTo(1), "three spellings of one pair are one pair");
            Assert.That(pairs[0].FailedQuery, Is.EqualTo("settee"));
            Assert.That(pairs[0].SucceededQuery, Is.EqualTo("red sofa"));
        });
    }

    /// <summary>Only the nearest click counts: a later one belongs to whatever the visitor did next.</summary>
    [Test]
    public void OnlyTheFirstClickAfterAFailure_Pairs()
    {
        var pairs = SynonymMiner.Mine(
            [
                Miss("settee", 0),
                Click("sofa", 10),
                Click("kettle", 20),
                Miss("settee", 300),
                Click("sofa", 310),
                Click("kettle", 320),
                Miss("settee", 600),
                Click("sofa", 610),
                Click("kettle", 620)
            ],
            3,
            60);

        Assert.That(pairs.Select(pair => pair.SucceededQuery), Is.EqualTo(new[] { "sofa" }).AsCollection);
    }

    [Test]
    public void AnAnsweredPair_NeverComesBack()
    {
        var mined = SynonymMiner.Mine(
            [.. Reformulation("settee", "sofa", 0), .. Reformulation("settee", "sofa", 600), .. Reformulation("settee", "sofa", 1200)],
            3,
            60);

        Assert.That(SynonymMiner.Pending(mined, [("SETTEE", "sofa")]), Is.Empty, "case is not a new pair either");
    }

    [Test]
    public async Task OneTaskRun_MinesEveryIndexItSaw()
    {
        var log = new InMemoryQueryLogStore();
        var signals = new FakePopularitySignalStore();
        var mined = new FakeSynonymSuggestionStore();

        foreach (var row in Reformulation("settee", "sofa", 0)
            .Concat(Reformulation("settee", "sofa", 600))
            .Concat(Reformulation("settee", "sofa", 1200))
            .Concat(Reformulation("cooker", "oven", 0).Select(entry => entry with { IndexName = "B" })))
        {
            await log.AppendAsync(row, CancellationToken.None);
        }

        var task = new XpSearchPopularityTask(
            log,
            signals,
            mined,
            Microsoft.Extensions.Options.Options.Create(new XpSearchOptions()),
            NullLogger<XpSearchPopularityTask>.Instance);

        await task.Execute(null!, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(mined.Written.Select(entry => entry.IndexName), Is.EquivalentTo(new[] { TestCorpus.IndexName, "B" }));
            Assert.That(
                mined.Written.Single(entry => entry.IndexName == TestCorpus.IndexName).Pairs.Single().SucceededQuery,
                Is.EqualTo("sofa"));
            Assert.That(
                mined.Written.Single(entry => entry.IndexName == "B").Pairs,
                Is.Empty,
                "one occurrence is under the default threshold");
        });
    }

    [Test]
    public void TheStoredSuggestion_HasEveryFieldTheMinerProduces()
    {
        var form = XpSearchAnalyticsModuleInstaller.SynonymSuggestionForm();

        Expect.Multiple(() =>
        {
            foreach (string field in new[]
            {
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionIndexName),
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionFailed),
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionSucceeded),
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionOccurrences),
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionLastSeen),
                nameof(XpSearchSynonymSuggestionInfo.SynonymSuggestionState)
            })
            {
                Assert.That(form.GetFormField(field), Is.Not.Null, field);
            }
        });
    }

    /// <summary>One reformulation: a search with no click, and a clicked search ten seconds later.</summary>
    private static QueryLogEntry[] Reformulation(string failed, string succeeded, int offsetSeconds) =>
        [Miss(failed, offsetSeconds), Click(succeeded, offsetSeconds + 10)];

    private static QueryLogEntry Miss(string query, int offsetSeconds) =>
        new("q", TestCorpus.IndexName, query, 0, Start.AddSeconds(offsetSeconds), "Store", "en", 12);

    private static QueryLogEntry Click(string query, int offsetSeconds) =>
        Miss(query, offsetSeconds) with { ResultCount = 5, ClickedPosition = 1, ClickedResultId = "doc-1:en" };
}

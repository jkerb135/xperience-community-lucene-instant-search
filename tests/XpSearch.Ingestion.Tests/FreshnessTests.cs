using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// A pushed document must be findable through the query pipeline in the same process, without a
/// restart: <c>DefaultLuceneClient</c> writes in place and does not invalidate the integration's
/// cached searcher, so the write path has to.
/// </summary>
[TestFixture]
internal sealed class FreshnessTests
{
    [Test]
    public async Task PushedDocumentIsSearchableWithoutARestart()
    {
        using var harness = new TestHarness();
        var pipeline = harness.Pipeline();

        var before = await pipeline.ExecuteAsync(
            new SearchRequest { Index = TestHarness.IndexName, Query = "espresso" },
            CancellationToken.None);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")])],
            waitForIndex: true);

        var after = await pipeline.ExecuteAsync(
            new SearchRequest { Index = TestHarness.IndexName, Query = "espresso" },
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(before.Total, Is.Zero);
            Assert.That(after.Total, Is.EqualTo(1));
            Assert.That(after.Results[0].Id, Is.EqualTo("pim-1"));
        });
    }

    [Test]
    public async Task StatusReflectsAPushWithoutARestart()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")])],
            waitForIndex: true);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Documents.Total, Is.EqualTo(1));
            Assert.That(status.Documents.BySource["pim"], Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Work waiting in the queue is the normal state of an asynchronous write, so the status of an
    /// index whose counts are merely lagging must not read as an incident. Only work that failed to
    /// reach Lucene is degraded.
    /// </summary>
    [Test]
    public async Task QueuedWorkIsHealthyAndFailedWorkIsDegraded()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Espresso machine")])],
            waitForIndex: false);

        var queued = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        harness.Queue.FailedCount = 1;

        var failed = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(harness.Queue.Queued, Is.Not.Empty, "the write is still waiting to be indexed");
            Assert.That(queued.Documents.Total, Is.Zero, "and the counts lag behind it");
            Assert.That(queued.Health, Is.EqualTo(Health.Healthy));
            Assert.That(failed.Health, Is.EqualTo(Health.Degraded));
        });
    }
}

using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Queue;
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
    /// reach Lucene is degraded - and the failure is read from the ingestion log, so an instance that
    /// never ran the work reports it too (WF-1).
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

        // What the queue worker writes when an item throws - here, as if on another instance.
        await harness.Log.WriteAsync(
            XpSearchIngestionQueueWorker.FailureEntry(
                IngestionWorkItem.New(TestHarness.IndexName, IngestionOperation.Upsert, ["pim-1"]),
                new InvalidOperationException("Lucene is not writable."),
                harness.Time.Now.UtcDateTime),
            CancellationToken.None);

        var failed = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(harness.Queue.Queued, Is.Not.Empty, "the write is still waiting to be indexed");
            Assert.That(queued.Documents.Total, Is.Zero, "and the counts lag behind it");
            Assert.That(queued.Health, Is.EqualTo(Health.Healthy));
            Assert.That(harness.Queue.FailedCount, Is.Zero, "the per-instance counter no longer decides health");
            Assert.That(failed.Health, Is.EqualTo(Health.Degraded));
        });
    }

    /// <summary>
    /// The log has no "recovered" row, so the failure is a window: once it ages out, the index is
    /// healthy again (WF-1).
    /// </summary>
    [Test]
    public async Task AFailureOlderThanTheWindowIsNoLongerDegraded()
    {
        using var harness = new TestHarness();

        await harness.Log.WriteAsync(
            XpSearchIngestionQueueWorker.FailureEntry(
                IngestionWorkItem.New(TestHarness.IndexName, IngestionOperation.Upsert, ["pim-1"]),
                new InvalidOperationException("Lucene was not writable."),
                harness.Time.Now.UtcDateTime - XpSearchIndexer.FailureWindow.Add(TimeSpan.FromMinutes(1))),
            CancellationToken.None);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Assert.That(status.Health, Is.EqualTo(Health.Healthy));
    }

    /// <summary>
    /// A rejected API call is not an index incident: only the background work rows the queue worker
    /// writes count, which is what the shared operation name pins.
    /// </summary>
    [Test]
    public async Task ARejectedApiCallDoesNotDegradeTheIndex()
    {
        using var harness = new TestHarness();

        await harness.Log.WriteAsync(
            new IngestionLogEntry("test1234", TestHarness.IndexName, "upsert", 1, false, "1 document(s) rejected", harness.Time.Now.UtcDateTime),
            CancellationToken.None);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Assert.That(status.Health, Is.EqualTo(Health.Healthy));
    }
}

using NUnit.Framework;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Indexing;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// The rebuild state the ingestion log carries (RB-1): the finished row the replay writes, and what
/// <c>GET …/status</c> answers between the two rows. The library owns no rebuild event - the state is
/// derived from a started row, a finished row and the clock - so this is where that derivation is
/// pinned.
/// </summary>
[TestFixture]
internal sealed class RebuildProgressTests
{
    private static Task StartedRowAsync(TestHarness harness, DateTimeOffset at) =>
        harness.Log.WriteAsync(
            new IngestionLogEntry("admin-ui", TestHarness.IndexName, RebuildProgress.StartedOperation, 0, true, "Rebuild triggered.", at.UtcDateTime),
            CancellationToken.None);

    [Test]
    public async Task Replay_WritesTheFinishedRowWithTheDocumentCount()
    {
        using var harness = new TestHarness(xperienceContent: [TestLuceneIndex.XperienceDocument(Guid.NewGuid().ToString(), "en", "Coffee")]);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: ("title", "Grinder"))],
            waitForIndex: true);

        await StartedRowAsync(harness, harness.Time.Now);
        await harness.Client.Rebuild(TestHarness.IndexName, CancellationToken.None);

        Assert.That(
            harness.Log.Entries.Any(entry => entry.Operation == RebuildProgress.FinishedOperation),
            Is.False,
            "the rebuild is not over until the replay behind it has run");

        harness.Time.Now = harness.Time.Now.AddMinutes(3);
        await harness.Queue.DrainAsync();

        var finished = harness.Log.Entries.Single(entry => entry.Operation == RebuildProgress.FinishedOperation);

        Expect.Multiple(() =>
        {
            Assert.That(finished.DocumentCount, Is.EqualTo(harness.Index.Count()));
            Assert.That(finished.At, Is.EqualTo(harness.Time.Now.UtcDateTime));
            Assert.That(finished.Succeeded, Is.True);
            Assert.That(finished.Message, Does.Contain("close estimate"), "the finish is quiescence, not an event");
        });
    }

    [Test]
    public async Task StartedWithoutFinished_IsRunningAndDegradesHealth()
    {
        using var harness = new TestHarness();

        await StartedRowAsync(harness, harness.Time.Now);

        harness.Time.Now = harness.Time.Now.AddMinutes(5);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Rebuild!.Running, Is.True);
            Assert.That(status.Rebuild.StartedAt, Is.EqualTo(harness.Time.Now.AddMinutes(-5)));
            Assert.That(status.Rebuild.FinishedAt, Is.Null, "there is no honest finish time while it runs");
            Assert.That(status.Rebuild.Documents, Is.Null, "and no total to count towards");
            Assert.That(status.Health, Is.EqualTo(Health.Degraded), "an external poller should wait");
        });
    }

    [Test]
    public async Task StartedLongerAgoThanTheThreshold_IsStuck()
    {
        using var harness = new TestHarness();

        await StartedRowAsync(harness, harness.Time.Now);

        var justBefore = await RebuildProgress.ReadAsync(
            harness.Log,
            TestHarness.IndexName,
            harness.Time.Now + harness.Options.RebuildStuckAfter,
            harness.Options.RebuildStuckAfter,
            CancellationToken.None);

        var after = await RebuildProgress.ReadAsync(
            harness.Log,
            TestHarness.IndexName,
            harness.Time.Now + harness.Options.RebuildStuckAfter + TimeSpan.FromMinutes(1),
            harness.Options.RebuildStuckAfter,
            CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(harness.Options.RebuildStuckAfter, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(justBefore.Phase, Is.EqualTo(RebuildPhase.Running));
            Assert.That(after.Phase, Is.EqualTo(RebuildPhase.Stuck));
            Assert.That(after.Running, Is.True, "a stuck rebuild has still not finished");
        });
    }

    [Test]
    public async Task FinishedRowWithoutAStartedOne_ReportsTheFinishAlone()
    {
        using var harness = new TestHarness();

        // What a rebuild started from Kentico's own Search application leaves behind: the replay
        // writes the finished row, and nothing wrote a start.
        await harness.Log.WriteAsync(
            new IngestionLogEntry("in-process", TestHarness.IndexName, RebuildProgress.FinishedOperation, 42, true, "Rebuild finished.", harness.Time.Now.UtcDateTime),
            CancellationToken.None);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Rebuild!.Running, Is.False);
            Assert.That(status.Rebuild.StartedAt, Is.Null);
            Assert.That(status.Rebuild.FinishedAt, Is.EqualTo(harness.Time.Now));
            Assert.That(status.Rebuild.Documents, Is.EqualTo(42));
            Assert.That(status.Health, Is.EqualTo(Health.Healthy));
        });
    }

    [Test]
    public async Task NoRebuildEverRecorded_LeavesTheStatusAsItWas()
    {
        using var harness = new TestHarness();

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Rebuild, Is.Null, "the field is additive: absent when there is nothing to say");
            Assert.That(status.Health, Is.EqualTo(Health.Healthy));
        });
    }
}

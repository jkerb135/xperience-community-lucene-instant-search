using System.Text.Json;

using NUnit.Framework;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;
using XpSearch.Ingestion.Persistence;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// The write semantics of spec §10.2 and §10.6: idempotent upsert, partial-batch failure, the
/// read-modify-rewrite patch, and durability across a restart.
/// </summary>
[TestFixture]
internal sealed class IngestionTests
{
    [Test]
    public async Task Upsert_IsIdempotent()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "First title"), ("price", 18.5)])],
            waitForIndex: true);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Second title"), ("price", 21.0)])],
            waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(harness.Index.Count(), Is.EqualTo(1), "the same id replaces the document rather than adding one");
            Assert.That(harness.Store.Rows, Has.Count.EqualTo(1));
            Assert.That(harness.Index.Stored("pim-1", "title"), Is.EqualTo("Second title"));
            Assert.That(harness.Index.Matching("title", "first"), Is.Zero, "the old body is gone");
        });
    }

    [Test]
    public async Task Upsert_IndexesTheValidDocumentsOfAPartiallyInvalidBatch()
    {
        using var harness = new TestHarness();

        var response = await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("good-1", attributes: ("title", "Fine")),
                TestHarness.Document("bad-1", attributes: ("price", "not a number")),
                TestHarness.Document("good-2", attributes: ("title", "Also fine")),
            ],
            waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Indexed, Is.EqualTo(2));
            Assert.That(response.Failed, Is.EqualTo(1));
            Assert.That(response.Errors, Has.Length.EqualTo(1));
            Assert.That(response.Errors[0].Id, Is.EqualTo("bad-1"));
            Assert.That(response.Errors[0].Field, Is.EqualTo("price"));
            Assert.That(response.Errors[0].Message, Does.Contain("number"));
            Assert.That(harness.Index.Documents().Select(document => document.Id), Is.EquivalentTo(["good-1", "good-2"]));
        });
    }

    [Test]
    public async Task Upsert_QueuesTheLuceneWriteAndReportsATaskId()
    {
        using var harness = new TestHarness();

        var response = await harness.Indexer.UpsertAsync(TestHarness.IndexName, [TestHarness.Document("pim-1", attributes: ("title", "Queued"))]);

        Expect.Multiple(() =>
        {
            Assert.That(response.TaskId, Is.Not.Null.And.Not.Empty);
            Assert.That(harness.Store.Rows, Has.Count.EqualTo(1), "the row is committed before the queue runs");
            Assert.That(harness.Store.Rows.Single().Status, Is.EqualTo(ExternalDocumentStatus.Pending));
            Assert.That(harness.Index.Count(), Is.Zero, "nothing has reached Lucene yet");
        });

        await harness.Queue.DrainAsync();

        Expect.Multiple(() =>
        {
            Assert.That(harness.Index.Count(), Is.EqualTo(1));
            Assert.That(harness.Store.Rows.Single().Status, Is.EqualTo(ExternalDocumentStatus.Indexed));
        });
    }

    [Test]
    public async Task PendingRowsAreRequeuedOnStartup()
    {
        using var harness = new TestHarness();

        // A push whose queued work never ran: the process died between the commit and the write.
        await harness.Indexer.UpsertAsync(TestHarness.IndexName, [TestHarness.Document("pim-1", attributes: ("title", "Survivor"))]);
        harness.Queue.Queued.Clear();

        Assert.That(harness.Index.Count(), Is.Zero);

        int requeued = await XpSearchIngestionModule.RequeuePendingAsync(harness.Store, harness.Queue, CancellationToken.None);
        await harness.Queue.DrainAsync();

        Expect.Multiple(() =>
        {
            Assert.That(requeued, Is.EqualTo(1));
            Assert.That(harness.Index.Stored("pim-1", "title"), Is.EqualTo("Survivor"));
        });
    }

    [Test]
    public async Task Patch_RewritesOnlyTheNamedAttributes()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [TestHarness.Document("pim-1", attributes: [("title", "Ethiopian Yirgacheffe"), ("price", 18.5), ("sku", "88213")])],
            waitForIndex: true);

        var response = await harness.Indexer.PatchAsync(
            TestHarness.IndexName,
            "pim-1",
            new Dictionary<string, JsonElement> { ["price"] = TestHarness.Value(21.0) },
            waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Indexed, Is.EqualTo(1));
            Assert.That(harness.Index.Stored("pim-1", "title"), Is.EqualTo("Ethiopian Yirgacheffe"), "untouched attributes survive");
            Assert.That(harness.Index.Stored("pim-1", "sku"), Is.EqualTo("88213"));
            Assert.That(harness.Index.Stored("pim-1", "price"), Is.EqualTo("21.0"), "the patched value replaced the stored one");
            Assert.That(harness.Index.Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Patch_OfAnUnknownDocumentIsNotFound()
    {
        using var harness = new TestHarness();

        Expect.ThrowsAsync<DocumentNotFoundException>(() => harness.Indexer.PatchAsync(
            TestHarness.IndexName,
            "nope",
            new Dictionary<string, JsonElement> { ["price"] = TestHarness.Value(1) }));
    }

    [Test]
    public async Task Delete_RemovesTheRowAndTheDocument()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(TestHarness.IndexName, [TestHarness.Document("pim-1", attributes: ("title", "Gone soon"))], waitForIndex: true);

        var response = await harness.Indexer.DeleteAsync(TestHarness.IndexName, ["pim-1"], waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(response.Deleted, Is.EqualTo(1));
            Assert.That(harness.Index.Count(), Is.Zero);
            Assert.That(harness.Store.Rows, Is.Empty);
        });
    }

    [Test]
    public async Task Status_CountsDocumentsBySource()
    {
        using var harness = new TestHarness(xperienceContent:
        [
            TestLuceneIndex.XperienceDocument("11111111-1111-1111-1111-111111111111", "en", "Espresso Basics"),
        ]);

        await harness.Indexer.UpsertAsync(
            TestHarness.IndexName,
            [
                TestHarness.Document("pim-1", attributes: ("title", "One")),
                TestHarness.Document("kb-1", "support", ("title", "Two")),
            ],
            waitForIndex: true);

        var status = await harness.Indexer.GetStatusAsync(TestHarness.IndexName);

        Expect.Multiple(() =>
        {
            Assert.That(status.Index, Is.EqualTo(TestHarness.IndexName));
            Assert.That(status.Documents.Total, Is.EqualTo(3));
            Assert.That(status.Documents.BySource["xperience"], Is.EqualTo(1));
            Assert.That(status.Documents.BySource["pim"], Is.EqualTo(1));
            Assert.That(status.Documents.BySource["support"], Is.EqualTo(1));
            Assert.That(status.LastWrite, Is.Not.Null);
            Assert.That(status.Health, Is.EqualTo(Health.Healthy));
        });
    }

    [Test]
    public async Task EveryWriteIsLoggedWithTheKeyPrefixIndexAndCount()
    {
        using var harness = new TestHarness();

        await harness.Indexer.UpsertAsync(TestHarness.IndexName, [TestHarness.Document("pim-1", attributes: ("title", "Logged"))], waitForIndex: true);
        await harness.Indexer.DeleteAsync(TestHarness.IndexName, ["pim-1"], waitForIndex: true);

        Expect.Multiple(() =>
        {
            Assert.That(harness.Log.Entries.Select(entry => entry.Operation), Is.EqualTo(new[] { "upsert", "delete" }));
            Assert.That(harness.Log.Entries, Has.All.Matches<IngestionLogEntry>(entry => entry.KeyPrefix == "test1234"));
            Assert.That(harness.Log.Entries, Has.All.Matches<IngestionLogEntry>(entry => entry.IndexName == TestHarness.IndexName));
            Assert.That(harness.Log.Entries[0].DocumentCount, Is.EqualTo(1));
        });
    }
}

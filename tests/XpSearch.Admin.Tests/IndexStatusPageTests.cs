using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using NSubstitute;

using NUnit.Framework;

using XpSearch.Admin.UIPages;
using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Contract;

namespace XpSearch.Admin.Tests;

/// <summary>Covers the index status page's model building and its rebuild command (spec §10.8).</summary>
[TestFixture]
internal sealed class IndexStatusPageTests
{
    private const int IndexIdentifier = 7;
    private const string IndexName = "articles";

    private static readonly DateTime Now = new(2026, 8, 23, 9, 31, 0, DateTimeKind.Utc);

    private IXpSearchIndexer indexer = null!;
    private IIngestionQueue queue = null!;
    private ILuceneClient client = null!;
    private RecordingLog log = null!;
    private IndexStatusPage page = null!;

    [SetUp]
    public void SetUp()
    {
        indexer = Substitute.For<IXpSearchIndexer>();
        indexer.GetStatusAsync(IndexName, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Status(Health.Healthy)));

        queue = Substitute.For<IIngestionQueue>();
        client = Substitute.For<ILuceneClient>();
        log = new RecordingLog();

        page = Build();
    }

    [Test]
    public async Task Load_ReportsCountsSharesAndTheProvenanceOfEachSource()
    {
        var status = (await page.Load(CancellationToken.None)).Result;

        Expect.Multiple(() =>
        {
            Assert.That(status.Error, Is.Empty);
            Assert.That(status.Health, Is.EqualTo("Healthy"));
            Assert.That(status.Documents, Is.EqualTo(152));
            Assert.That(status.Sources, Is.EqualTo(2));
            Assert.That(status.LastWrite, Is.EqualTo("2026-08-23 09:14 UTC"));
            Assert.That(status.BySource.Select(row => row.Source), Is.EqualTo(new[] { "pim", "xperience" }), "largest source first");
            Assert.That(status.BySource[0].Count, Is.EqualTo(120));
            Assert.That(status.BySource[0].Share, Is.EqualTo(120d / 152).Within(0.0001));
            Assert.That(status.BySource[1].Kind, Does.Contain("CMS"));
            Assert.That(status.BySource[0].Kind, Does.Contain("ingestion API"));
        });
    }

    /// <summary>The failed-write count is the queue's, which the wire contract's IndexStatus does not carry.</summary>
    [Test]
    public async Task Load_TakesTheFailedWriteCountFromTheQueue()
    {
        queue.FailedCount.Returns(3);

        Assert.That((await page.Load(CancellationToken.None)).Result.FailedWrites, Is.EqualTo(3));
    }

    [Test]
    public async Task Load_LeavesTheLogNewestFirstWhileTheIndexIsHealthy()
    {
        log.Entries.AddRange(Entries());

        var status = (await page.Load(CancellationToken.None)).Result;

        Assert.That(
            status.RecentIngestion.Select(entry => entry.Message),
            Is.EqualTo(new[] { "newest ok", "older failed", "oldest ok" }));
    }

    [Test]
    public async Task Load_LiftsTheFailedEntriesToTheTopWhileTheIndexIsDegraded()
    {
        indexer.GetStatusAsync(IndexName, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Status(Health.Degraded)));
        log.Entries.AddRange(Entries());

        var status = (await page.Load(CancellationToken.None)).Result;

        Expect.Multiple(() =>
        {
            Assert.That(
                status.RecentIngestion.Select(entry => entry.Message),
                Is.EqualTo(new[] { "older failed", "newest ok", "oldest ok" }));
            Assert.That(status.RecentIngestion[0].Succeeded, Is.False);
            Assert.That(status.RecentIngestion[0].Timestamp, Is.EqualTo("2026-08-23 08:02 UTC"));
        });
    }

    [Test]
    public async Task Load_AsksForTenEntriesOfThisIndexOnly()
    {
        await page.Load(CancellationToken.None);

        Assert.That(log.Requested, Is.EqualTo((IndexName, IndexStatusPage.RecentEntryCount)));
    }

    [Test]
    public async Task Load_ReportsAnUnregisteredIndexInsteadOfThrowing()
    {
        var orphan = Build(indexIdentifier: 999);

        var status = (await orphan.Load(CancellationToken.None)).Result;

        Expect.Multiple(() =>
        {
            Assert.That(status.Error, Is.Not.Empty);
            Assert.That(status.Documents, Is.Zero);
        });
    }

    [Test]
    public async Task Load_ReportsAnIndexWithoutLuceneStorageInsteadOfThrowing()
    {
        indexer.GetStatusAsync(IndexName, Arg.Any<CancellationToken>())
            .Returns<Task<IndexStatus>>(_ => throw new Core.Abstractions.IndexNotFoundException(IndexName));

        Assert.That((await page.Load(CancellationToken.None)).Result.Error, Does.Contain(IndexName));
    }

    [Test]
    public async Task Rebuild_RebuildsTheIndexAndRecordsIt()
    {
        var response = await page.Rebuild(CancellationToken.None);

        await client.Received(1).Rebuild(IndexName, Arg.Any<CancellationToken>());

        Expect.Multiple(() =>
        {
            Assert.That(log.Entries, Has.Count.EqualTo(1));
            Assert.That(log.Entries[0].KeyPrefix, Is.EqualTo("admin-ui"));
            Assert.That(log.Entries[0].IndexName, Is.EqualTo(IndexName));
            Assert.That(log.Entries[0].Operation, Is.EqualTo("rebuild"));
            Assert.That(log.Entries[0].Succeeded, Is.True);
            Assert.That(log.Entries[0].At, Is.EqualTo(Now));
            Assert.That(response.Result.RebuildStartedAt, Is.EqualTo("2026-08-23 09:31 UTC"));
            Assert.That(response.Result.Error, Is.Empty);
        });
    }

    [Test]
    public async Task Rebuild_RefusesAnUnregisteredIndex()
    {
        var orphan = Build(indexIdentifier: 999);

        var response = await orphan.Rebuild(CancellationToken.None);

        await client.DidNotReceiveWithAnyArgs().Rebuild(default!, default);

        Expect.Multiple(() =>
        {
            Assert.That(log.Entries, Is.Empty);
            Assert.That(response.Result.Error, Is.Not.Empty);
        });
    }

    /// <summary>Reading the page is a VIEW; emptying and rewriting it needs the integration's Rebuild permission.</summary>
    [Test]
    public void PageAndCommands_AreBehindTheApplicationsPermissions()
    {
        var pagePermission = typeof(IndexStatusPage)
            .GetCustomAttributes(typeof(UIEvaluatePermissionAttribute), inherit: false)
            .Cast<UIEvaluatePermissionAttribute>()
            .Single();

        Expect.Multiple(() =>
        {
            Assert.That(pagePermission.Permission, Is.EqualTo(SystemPermissions.VIEW));
            Assert.That(Command(nameof(IndexStatusPage.Load)).Permission, Is.EqualTo(SystemPermissions.VIEW));
            Assert.That(Command(nameof(IndexStatusPage.Rebuild)).Permission, Is.EqualTo(IndexStatusPage.RebuildPermission));
        });
    }

    [Test]
    public async Task ConfigureTemplateProperties_NamesTheIndexInTheUrl()
    {
        var properties = await page.ConfigureTemplateProperties(new IndexStatusClientProperties());

        Assert.That(properties.IndexName, Is.EqualTo(IndexName));
    }

    private static PageCommandAttribute Command(string method) =>
        typeof(IndexStatusPage)
            .GetMethod(method)!
            .GetCustomAttributes(typeof(PageCommandAttribute), inherit: false)
            .Cast<PageCommandAttribute>()
            .Single();

    private static IndexStatus Status(Health health) =>
        new()
        {
            Index = IndexName,
            Health = health,
            Documents = new DocumentCounts { Total = 152, BySource = new() { ["xperience"] = 32, ["pim"] = 120 } },
            LastWrite = new DateTimeOffset(2026, 8, 23, 9, 14, 0, TimeSpan.Zero),
        };

    private static IEnumerable<IngestionLogEntry> Entries() =>
    [
        new("pim", IndexName, "upsert", 12, false, "older failed", new DateTime(2026, 8, 23, 8, 2, 0, DateTimeKind.Utc)),
        new("pim", IndexName, "upsert", 20, true, "newest ok", new DateTime(2026, 8, 23, 9, 14, 0, DateTimeKind.Utc)),
        new("erp", IndexName, "upsert", 5, true, "oldest ok", new DateTime(2026, 8, 22, 7, 0, 0, DateTimeKind.Utc)),
    ];

    private IndexStatusPage Build(int indexIdentifier = IndexIdentifier) =>
        new(Storage.Holding(IndexIdentifier, IndexName), indexer, queue, client, log, new FakeTime(Now))
        {
            IndexIdentifier = indexIdentifier
        };

    /// <summary>An ingestion log that answers from memory and remembers what the page asked for.</summary>
    private sealed class RecordingLog : IIngestionLog
    {
        public List<IngestionLogEntry> Entries { get; } = [];

        public (string IndexName, int Count) Requested { get; private set; }

        public Task WriteAsync(IngestionLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IngestionLogEntry>> ReadRecentAsync(string indexName, int count, CancellationToken cancellationToken)
        {
            Requested = (indexName, count);

            return Task.FromResult<IReadOnlyList<IngestionLogEntry>>(
                Entries.OrderByDescending(entry => entry.At).Take(count).ToList());
        }
    }

    private sealed class FakeTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using XpSearch.Core.Contract;
using XpSearch.Core.Options;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// The administration's per-index settings overlay (AR-2): an index's stored values win over the
/// host's <c>AddXpSearch(o =&gt; ...)</c> lambda, an index with no row keeps what the lambda set, and a
/// save rebuilds only the index that was saved.
/// </summary>
[TestFixture]
internal sealed class StoredSettingsTests
{
    private const string IndexA = "Products";
    private const string IndexB = "Articles";

    [Test]
    public void StoredValues_WinOverTheConfiguredOnes_ForTheirIndexOnly()
    {
        var options = new XpSearchOptions { MaxPageSize = 40 };
        options.Analytics.RetentionDays = 90;

        var source = new FakeSettingsSource
        {
            [IndexA] = SearchSettingsValues.From(XpSearchIndexSettings.FromOptions(options)) with
            {
                MaxPageSize = 25,
                RetentionDays = 7
            }
        };

        var monitor = Monitor(options, source);

        Expect.Multiple(() =>
        {
            Assert.That(monitor.Get(IndexA).MaxPageSize, Is.EqualTo(25));
            Assert.That(monitor.Get(IndexA).RetentionDays, Is.EqualTo(7));

            // Columns nobody edited still carry the configured defaults, not zero.
            Assert.That(monitor.Get(IndexA).MaxQueryLength, Is.EqualTo(options.MaxQueryLength));

            // The other index has no row of its own.
            Assert.That(monitor.Get(IndexB).MaxPageSize, Is.EqualTo(40));
            Assert.That(monitor.Get(IndexB).RetentionDays, Is.EqualTo(90));

            // The unnamed instance is the code defaults, which is what an orphan index is pruned with.
            Assert.That(monitor.Get(Microsoft.Extensions.Options.Options.DefaultName).RetentionDays, Is.EqualTo(90));
        });
    }

    [Test]
    public void UpgradeAddedColumns_ReadAsZero_AndKeepTheConfiguredValue()
    {
        var options = new XpSearchOptions { MaxFacetValues = 42, CacheTtl = TimeSpan.FromSeconds(30) };

        // What a row written before the column existed looks like: everything but the edited value zero.
        var source = new FakeSettingsSource
        {
            [IndexA] = new SearchSettingsValues { MaxPageSize = 25, CacheTtlSeconds = 0 }
        };

        var settings = Monitor(options, source).Get(IndexA);

        Expect.Multiple(() =>
        {
            Assert.That(settings.MaxPageSize, Is.EqualTo(25));
            Assert.That(settings.MaxFacetValues, Is.EqualTo(42), "a zero column is 'nobody set this'");

            // Zero is a legal cache lifetime, so that one column is applied as stored.
            Assert.That(settings.CacheTtl, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void UnreadableStorage_LeavesTheConfiguredValuesAlone()
    {
        var options = new XpSearchOptions { MaxPageSize = 40 };

        Assert.That(Monitor(options, new ThrowingSettingsSource()).Get(IndexA).MaxPageSize, Is.EqualTo(40));
    }

    [Test]
    public void SavingOneIndex_RebuildsOnlyThatIndex()
    {
        var options = new XpSearchOptions();
        var source = new FakeSettingsSource
        {
            [IndexA] = new SearchSettingsValues { MaxPageSize = 25 }
        };

        var cache = new OptionsCache<XpSearchIndexSettings>();
        var responses = new MemorySearchCache();
        var monitor = Monitor(options, source, cache);

        var before = monitor.Get(IndexB);

        Assert.That(monitor.Get(IndexA).MaxPageSize, Is.EqualTo(25));

        source[IndexA] = new SearchSettingsValues { MaxPageSize = 26 };
        new XpSearchIndexSettingsInvalidator(cache, responses).Invalidate(IndexA);

        Expect.Multiple(() =>
        {
            Assert.That(monitor.Get(IndexA).MaxPageSize, Is.EqualTo(26), "the saved index is read again");
            Assert.That(monitor.Get(IndexB), Is.SameAs(before), "no other index is rebuilt");

            // A request that omits pageSize computes the same cache key before and after the save, so
            // the settings alone would not reach a cached response.
            Assert.That(responses.Evicted, Is.EqualTo(new[] { IndexA }).AsCollection);
        });
    }

    /// <summary>A row with no index name reaches neither cache: there is nothing to invalidate.</summary>
    [Test]
    public void SavingARowWithNoIndex_EvictsNothing()
    {
        var responses = new MemorySearchCache();

        new XpSearchIndexSettingsInvalidator(new OptionsCache<XpSearchIndexSettings>(), responses).Invalidate(null);

        Assert.That(responses.Evicted, Is.Empty);
    }

    [Test]
    public void SeededValues_AreTheEffectiveConfiguredOnes()
    {
        var options = new XpSearchOptions { CacheTtl = TimeSpan.FromSeconds(15) };
        options.Analytics.SynonymMinimumOccurrences = 9;

        var values = SearchSettingsValues.From(XpSearchIndexSettings.FromOptions(options));

        Expect.Multiple(() =>
        {
            Assert.That(values.CacheTtlSeconds, Is.EqualTo(15));
            Assert.That(values.SynonymMinimumOccurrences, Is.EqualTo(9));
            Assert.That(values.RetentionDays, Is.EqualTo(365), "the shipped retention default");
        });
    }

    /// <summary>
    /// The pipeline keys the settings by the index's registered spelling, because
    /// <see cref="IOptionsMonitor{TOptions}.Get"/> compares names ordinally.
    /// </summary>
    [Test]
    public async Task ARequestSpellingTheIndexDifferently_ReadsTheRegisteredIndexsSettings()
    {
        var capture = new CaptureIndexNameStage();

        using var harness = new TestHarness(extraStages: capture);

        await harness.Search(new SearchRequest { Index = TestCorpus.IndexName.ToUpperInvariant() });

        Assert.That(capture.IndexName, Is.EqualTo(TestCorpus.IndexName));
    }

    private static IOptionsMonitor<XpSearchIndexSettings> Monitor(
        XpSearchOptions options,
        IStoredSearchSettingsSource source,
        IOptionsMonitorCache<XpSearchIndexSettings>? cache = null)
    {
        var setup = new XpSearchIndexSettingsSetup(
            Microsoft.Extensions.Options.Options.Create(options),
            source,
            NullLogger<XpSearchIndexSettingsSetup>.Instance);

        return new OptionsMonitor<XpSearchIndexSettings>(
            new OptionsFactory<XpSearchIndexSettings>([setup], []),
            [],
            cache ?? new OptionsCache<XpSearchIndexSettings>());
    }

    /// <summary>Records the index name the pipeline resolved, which is what every <c>Get</c> is keyed by.</summary>
    private sealed class CaptureIndexNameStage : Pipeline.ISearchStage
    {
        internal string? IndexName { get; private set; }

        public int Order => Pipeline.SearchStageOrder.Normalize - 1;

        public Task ExecuteAsync(Pipeline.SearchContext context, CancellationToken cancellationToken)
        {
            IndexName = context!.IndexName;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsSource : IStoredSearchSettingsSource
    {
        private readonly Dictionary<string, SearchSettingsValues> rows = new(StringComparer.Ordinal);

        internal SearchSettingsValues this[string indexName]
        {
            set => rows[indexName] = value;
        }

        public SearchSettingsValues? Get(string indexName) => rows.GetValueOrDefault(indexName);
    }

    private sealed class ThrowingSettingsSource : IStoredSearchSettingsSource
    {
        public SearchSettingsValues? Get(string indexName) => throw new InvalidOperationException("no database");
    }
}

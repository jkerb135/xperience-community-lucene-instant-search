using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using XpSearch.Core.Options;
using XpSearch.Core.Tests.Fixtures;

namespace XpSearch.Core.Tests;

/// <summary>
/// The administration's global settings overlay (AR-1): the stored values win over the host's
/// <c>AddXpSearch(o =&gt; ...)</c> lambda, and an installation with no row keeps what the lambda set.
/// </summary>
[TestFixture]
internal sealed class StoredSettingsTests
{
    [Test]
    public void StoredValues_WinOverTheConfiguredOnes()
    {
        var options = new XpSearchOptions { MaxPageSize = 40 };
        options.Analytics.RetentionDays = 90;

        var stored = SearchSettingsValues.From(new XpSearchOptions()) with { MaxPageSize = 25, RetentionDays = 7 };

        Configure(stored).Configure(options);

        Expect.Multiple(() =>
        {
            Assert.That(options.MaxPageSize, Is.EqualTo(25));
            Assert.That(options.Analytics.RetentionDays, Is.EqualTo(7));

            // Columns nobody edited still carry the seeded defaults, not zero.
            Assert.That(options.MaxQueryLength, Is.EqualTo(new XpSearchOptions().MaxQueryLength));
        });
    }

    [Test]
    public void NoStoredRow_LeavesTheConfiguredValuesAlone()
    {
        var options = new XpSearchOptions { MaxPageSize = 40 };
        options.Analytics.RetentionDays = 90;

        Configure(null).Configure(options);

        Expect.Multiple(() =>
        {
            Assert.That(options.MaxPageSize, Is.EqualTo(40));
            Assert.That(options.Analytics.RetentionDays, Is.EqualTo(90));
        });
    }

    [Test]
    public void UnreadableStorage_LeavesTheConfiguredValuesAlone()
    {
        var options = new XpSearchOptions { MaxPageSize = 40 };

        new XpSearchStoredSettingsConfigureOptions(
            new ThrowingSettingsSource(),
            NullLogger<XpSearchStoredSettingsConfigureOptions>.Instance)
            .Configure(options);

        Assert.That(options.MaxPageSize, Is.EqualTo(40));
    }

    [Test]
    public void SeededValues_AreTheEffectiveConfiguredOnes()
    {
        var options = new XpSearchOptions { CacheTtl = TimeSpan.FromSeconds(15) };
        options.Analytics.SynonymMinimumOccurrences = 9;

        var values = SearchSettingsValues.From(options);

        Expect.Multiple(() =>
        {
            Assert.That(values.CacheTtlSeconds, Is.EqualTo(15));
            Assert.That(values.SynonymMinimumOccurrences, Is.EqualTo(9));
            Assert.That(values.RetentionDays, Is.EqualTo(365), "the shipped retention default");
        });
    }

    [Test]
    public void ZeroCacheLifetime_SurvivesTheRoundTrip()
    {
        var options = new XpSearchOptions();

        (SearchSettingsValues.From(new XpSearchOptions { CacheTtl = TimeSpan.Zero })).ApplyTo(options);

        Assert.That(options.CacheTtl, Is.EqualTo(TimeSpan.Zero));
    }

    private static XpSearchStoredSettingsConfigureOptions Configure(SearchSettingsValues? values) =>
        new(new FixedSettingsSource(values), NullLogger<XpSearchStoredSettingsConfigureOptions>.Instance);

    private sealed class FixedSettingsSource : IStoredSearchSettingsSource
    {
        private readonly SearchSettingsValues? values;

        internal FixedSettingsSource(SearchSettingsValues? values) => this.values = values;

        public SearchSettingsValues? Get() => values;
    }

    private sealed class ThrowingSettingsSource : IStoredSearchSettingsSource
    {
        public SearchSettingsValues? Get() => throw new InvalidOperationException("no database");
    }
}

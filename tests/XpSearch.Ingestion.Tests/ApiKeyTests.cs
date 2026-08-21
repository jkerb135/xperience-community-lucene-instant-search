using Microsoft.Extensions.Options;

using NUnit.Framework;

using XpSearch.Ingestion.Options;
using XpSearch.Ingestion.Security;
using XpSearch.Ingestion.Tests.Fixtures;

namespace XpSearch.Ingestion.Tests;

/// <summary>
/// Spec §10.4: keys are scoped per index and per operation, expire, can be disabled, and their
/// plaintext exists exactly once - at creation.
/// </summary>
[TestFixture]
internal sealed class ApiKeyTests
{
    private InMemoryApiKeyStore store = null!;
    private FakeClock clock = null!;
    private ApiKeyService keys = null!;

    [SetUp]
    public void CreateService()
    {
        store = new InMemoryApiKeyStore();
        clock = new FakeClock(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        keys = new ApiKeyService(store, Microsoft.Extensions.Options.Options.Create(new XpSearchIngestionOptions()), clock);
    }

    [Test]
    public async Task TheKeyIsShownOnceAndStoredOnlyAsAHash()
    {
        var created = await keys.CreateAsync("PIM sync", Scopes("products", "write"), null, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(created.Key, Does.StartWith("xps_"));
            Assert.That(created.Record.Hash, Does.Not.Contain(created.Key), "the plaintext is nowhere in the row");
            Assert.That(created.Record.Hash, Does.StartWith("pbkdf2-sha256$"));
            Assert.That(created.Record.Prefix, Is.EqualTo(created.Key[..8]));
            Assert.That(ApiKeyService.Verify(created.Key, created.Record.Hash), Is.True);
            Assert.That(ApiKeyService.Verify(created.Key + "x", created.Record.Hash), Is.False);
        });
    }

    [Test]
    public async Task AKeyInScopeIsAccepted()
    {
        var created = await keys.CreateAsync("PIM sync", Scopes("products", "write"), null, CancellationToken.None);

        var (key, failure) = await keys.AuthenticateAsync(created.Key, "products", "write", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(failure, Is.EqualTo(ApiKeyFailure.None));
            Assert.That(key!.Name, Is.EqualTo("PIM sync"));
            Assert.That(store.Keys.Single().LastUsedAt, Is.EqualTo(clock.GetUtcNow().UtcDateTime), "the last-used timestamp is written back");
        });
    }

    [Test]
    public async Task AKeyOutOfScopeIsRefused()
    {
        var created = await keys.CreateAsync("PIM sync", Scopes("products", "write"), null, CancellationToken.None);

        var otherIndex = await keys.AuthenticateAsync(created.Key, "articles", "write", CancellationToken.None);
        var otherOperation = await keys.AuthenticateAsync(created.Key, "products", "delete", CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(otherIndex.Failure, Is.EqualTo(ApiKeyFailure.OutOfScope));
            Assert.That(otherOperation.Failure, Is.EqualTo(ApiKeyFailure.OutOfScope));
        });
    }

    [Test]
    public async Task AnExpiredOrDisabledKeyIsRefused()
    {
        var expiring = await keys.CreateAsync("Expiring", Scopes("products", "write"), clock.GetUtcNow().UtcDateTime.AddHours(1), CancellationToken.None);
        var disabled = await keys.CreateAsync("Disabled", Scopes("products", "write"), null, CancellationToken.None);

        store.Replace(disabled.Record with { Enabled = false });
        clock.Advance(TimeSpan.FromHours(2));

        Expect.Multiple(() =>
        {
            Assert.That(keys.AuthenticateAsync(expiring.Key, "products", "write", CancellationToken.None).Result.Failure, Is.EqualTo(ApiKeyFailure.Expired));
            Assert.That(keys.AuthenticateAsync(disabled.Key, "products", "write", CancellationToken.None).Result.Failure, Is.EqualTo(ApiKeyFailure.Disabled));
        });
    }

    [Test]
    public async Task AnUnknownOrMissingKeyIsRefused()
    {
        await keys.CreateAsync("PIM sync", Scopes("products", "write"), null, CancellationToken.None);

        Expect.Multiple(() =>
        {
            Assert.That(keys.AuthenticateAsync(null, "products", "write", CancellationToken.None).Result.Failure, Is.EqualTo(ApiKeyFailure.Unknown));
            Assert.That(keys.AuthenticateAsync("xps_nonsense", "products", "write", CancellationToken.None).Result.Failure, Is.EqualTo(ApiKeyFailure.Unknown));
        });
    }

    [Test]
    public void WildcardScopesAllowEverything()
    {
        var scopes = Scopes("*", "*");

        Expect.Multiple(() =>
        {
            Assert.That(scopes.Allows("anything", "rebuild"), Is.True);
            Assert.That(new ApiKeyScopes().Allows("products", "write"), Is.False, "an unreadable or empty scope set allows nothing");
        });
    }

    private static ApiKeyScopes Scopes(string index, string operation) => new() { Indexes = [index], Ops = [operation] };

    /// <summary>A clock the test moves by hand, so expiry does not depend on wall time.</summary>
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        internal void Advance(TimeSpan by) => current += by;
    }
}

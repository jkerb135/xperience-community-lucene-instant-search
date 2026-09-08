using CMS.Helpers;

namespace XpSearch.Core.Tests.Fixtures;

/// <summary>
/// Xperience's progressive cache in a dictionary: enough of it to test that a cached entry expires
/// with its TTL and drops when one of its dependency keys is touched (WF-1).
/// </summary>
internal sealed class FakeProgressiveCache : IProgressiveCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> clock;

    internal FakeProgressiveCache(Func<DateTime>? clock = null) => this.clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>
    /// Builds a dependency the way <c>CacheHelper.GetCacheDependency</c> would, without the running
    /// application it needs.
    /// </summary>
    internal static CMSCacheDependency Dependency(string key) => new() { CacheKeys = [key] };

    /// <summary>How many times a load delegate actually ran.</summary>
    internal int Loads { get; private set; }

    /// <summary>Drops every entry that depends on a dummy cache key, the way <c>CacheHelper.TouchKey</c> does.</summary>
    internal void TouchKey(string key)
    {
        foreach (var doomed in entries.Where(entry => entry.Value.Keys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            entries.Remove(doomed.Key);
        }
    }

    public async Task<TData> LoadAsync<TData>(Func<CacheSettings, CancellationToken, Task<TData>> loadMethod, CacheSettings settings, CancellationToken cancellationToken)
    {
        var now = clock();

        if (entries.TryGetValue(settings.CacheItemName, out var entry) && entry.Expires > now)
        {
            return (TData)entry.Value!;
        }

        Loads++;

        var data = await loadMethod(settings, cancellationToken);

        entries[settings.CacheItemName] = new Entry(
            data,
            settings.CacheDependency?.CacheKeys ?? [],
            now.AddMinutes(settings.CacheMinutes));

        return data;
    }

    public Task<TData> LoadAsync<TData>(Func<CacheSettings, Task<TData>> loadMethod, CacheSettings settings) =>
        LoadAsync((cacheSettings, _) => loadMethod(cacheSettings), settings, CancellationToken.None);

    public Task<TData> LoadAsync<TData>(Func<CacheSettings, Task<TData>> loadMethod, CacheSettings settings, CancellationToken cancellationToken) =>
        LoadAsync((cacheSettings, _) => loadMethod(cacheSettings), settings, cancellationToken);

    public TData Load<TData>(Func<CacheSettings, TData> loadMethod, CacheSettings settings) =>
        LoadAsync((cacheSettings, _) => Task.FromResult(loadMethod(cacheSettings)), settings, CancellationToken.None).GetAwaiter().GetResult();

    private sealed record Entry(object? Value, IEnumerable<string> Keys, DateTime Expires);
}

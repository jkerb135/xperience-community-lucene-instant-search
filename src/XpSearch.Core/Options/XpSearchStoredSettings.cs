using CMS.DataEngine;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Options;

/// <summary>
/// One index's search settings as plain numbers (AR-2), free of the storage row: what an administrator
/// edits, what is stored, and what the named options overlay writes onto <see cref="XpSearchIndexSettings"/>.
/// </summary>
/// <remarks>
/// A separate type because a Kentico Info object cannot be constructed without the application's IoC
/// container, and the overlay has to be testable without a database.
/// </remarks>
public sealed record SearchSettingsValues
{
    /// <summary>Gets how long an identical query is served from cache, in seconds.</summary>
    public int CacheTtlSeconds { get; init; }

    /// <summary>Gets the maximum accepted length of the query text.</summary>
    public int MaxQueryLength { get; init; }

    /// <summary>Gets the page size used when a request omits one.</summary>
    public int DefaultPageSize { get; init; }

    /// <summary>Gets the server-side page size ceiling.</summary>
    public int MaxPageSize { get; init; }

    /// <summary>Gets the maximum number of values returned per facet dimension.</summary>
    public int MaxFacetValues { get; init; }

    /// <summary>Gets how deep paging may go.</summary>
    public int MaxResultWindow { get; init; }

    /// <summary>Gets the number of suggestions returned when a request omits a limit.</summary>
    public int DefaultSuggestLimit { get; init; }

    /// <summary>Gets the ceiling on the suggestion limit.</summary>
    public int MaxSuggestLimit { get; init; }

    /// <summary>Gets how many days of search analytics are kept.</summary>
    public int RetentionDays { get; init; }

    /// <summary>Gets how many rows the retention task deletes per batch.</summary>
    public int RetentionBatchSize { get; init; }

    /// <summary>Gets how far back query suggestions count query volume, in days.</summary>
    public int QuerySuggestionDays { get; init; }

    /// <summary>Gets how many days of clicks the popularity signal is computed from.</summary>
    public int PopularityLookbackDays { get; init; }

    /// <summary>Gets how many documents per index the popularity signal keeps.</summary>
    public int PopularityDocumentLimit { get; init; }

    /// <summary>Gets how many frequent queries are examined for a suggested boost rule.</summary>
    public int PopularitySuggestionQueries { get; init; }

    /// <summary>Gets the reformulation window synonym mining uses, in seconds.</summary>
    public int SynonymWindowSeconds { get; init; }

    /// <summary>Gets how often a reformulation has to happen before it is suggested.</summary>
    public int SynonymMinimumOccurrences { get; init; }

    /// <summary>Reads the values off an index's settings, which is what its form is filled from.</summary>
    /// <param name="settings">The settings to read.</param>
    /// <returns>The values.</returns>
    public static SearchSettingsValues From(XpSearchIndexSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new SearchSettingsValues
        {
            CacheTtlSeconds = (int)Math.Max(0, settings.CacheTtl.TotalSeconds),
            MaxQueryLength = settings.MaxQueryLength,
            DefaultPageSize = settings.DefaultPageSize,
            MaxPageSize = settings.MaxPageSize,
            MaxFacetValues = settings.MaxFacetValues,
            MaxResultWindow = settings.MaxResultWindow,
            DefaultSuggestLimit = settings.DefaultSuggestLimit,
            MaxSuggestLimit = settings.MaxSuggestLimit,
            RetentionDays = settings.RetentionDays,
            RetentionBatchSize = settings.RetentionBatchSize,
            QuerySuggestionDays = settings.QuerySuggestionDays,
            PopularityLookbackDays = settings.PopularityLookbackDays,
            PopularityDocumentLimit = settings.PopularityDocumentLimit,
            PopularitySuggestionQueries = settings.PopularitySuggestionQueries,
            SynonymWindowSeconds = settings.SynonymWindowSeconds,
            SynonymMinimumOccurrences = settings.SynonymMinimumOccurrences
        };
    }

    /// <summary>Writes the stored values over the code-configured ones.</summary>
    /// <param name="settings">The settings to overwrite.</param>
    /// <remarks>
    /// A column an upgrade added exists but was never written, and reads as 0; since every value but
    /// the cache lifetime has to be one or greater, a non-positive number means "nobody set this" and
    /// the code default stands.
    /// </remarks>
    public void ApplyTo(XpSearchIndexSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Zero is a legal cache lifetime - it turns response caching off - so this one is always applied.
        settings.CacheTtl = TimeSpan.FromSeconds(Math.Max(0, CacheTtlSeconds));

        settings.MaxQueryLength = Or(MaxQueryLength, settings.MaxQueryLength);
        settings.DefaultPageSize = Or(DefaultPageSize, settings.DefaultPageSize);
        settings.MaxPageSize = Or(MaxPageSize, settings.MaxPageSize);
        settings.MaxFacetValues = Or(MaxFacetValues, settings.MaxFacetValues);
        settings.MaxResultWindow = Or(MaxResultWindow, settings.MaxResultWindow);
        settings.DefaultSuggestLimit = Or(DefaultSuggestLimit, settings.DefaultSuggestLimit);
        settings.MaxSuggestLimit = Or(MaxSuggestLimit, settings.MaxSuggestLimit);
        settings.RetentionDays = Or(RetentionDays, settings.RetentionDays);
        settings.RetentionBatchSize = Or(RetentionBatchSize, settings.RetentionBatchSize);
        settings.QuerySuggestionDays = Or(QuerySuggestionDays, settings.QuerySuggestionDays);
        settings.PopularityLookbackDays = Or(PopularityLookbackDays, settings.PopularityLookbackDays);
        settings.PopularityDocumentLimit = Or(PopularityDocumentLimit, settings.PopularityDocumentLimit);
        settings.PopularitySuggestionQueries = Or(PopularitySuggestionQueries, settings.PopularitySuggestionQueries);
        settings.SynonymWindowSeconds = Or(SynonymWindowSeconds, settings.SynonymWindowSeconds);
        settings.SynonymMinimumOccurrences = Or(SynonymMinimumOccurrences, settings.SynonymMinimumOccurrences);
    }

    private static int Or(int stored, int fallback) => stored > 0 ? stored : fallback;
}

/// <summary>
/// Turns a stored settings row into <see cref="SearchSettingsValues"/> and back (AR-2). The one place
/// the column mapping lives: the overlay and the administration's Search settings page both use it.
/// </summary>
public static class StoredSearchSettings
{
    /// <summary>Reads a stored row.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>Its values.</returns>
    public static SearchSettingsValues Read(XpSearchSettingsInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SearchSettingsValues
        {
            CacheTtlSeconds = row.SettingsCacheTtlSeconds,
            MaxQueryLength = row.SettingsMaxQueryLength,
            DefaultPageSize = row.SettingsDefaultPageSize,
            MaxPageSize = row.SettingsMaxPageSize,
            MaxFacetValues = row.SettingsMaxFacetValues,
            MaxResultWindow = row.SettingsMaxResultWindow,
            DefaultSuggestLimit = row.SettingsDefaultSuggestLimit,
            MaxSuggestLimit = row.SettingsMaxSuggestLimit,
            RetentionDays = row.SettingsRetentionDays,
            RetentionBatchSize = row.SettingsRetentionBatchSize,
            QuerySuggestionDays = row.SettingsQuerySuggestionDays,
            PopularityLookbackDays = row.SettingsPopularityLookbackDays,
            PopularityDocumentLimit = row.SettingsPopularityDocumentLimit,
            PopularitySuggestionQueries = row.SettingsPopularitySuggestionQueries,
            SynonymWindowSeconds = row.SettingsSynonymWindowSeconds,
            SynonymMinimumOccurrences = row.SettingsSynonymMinimumOccurrences
        };
    }

    /// <summary>Builds an unsaved row carrying every column (RK-2: an unset column would insert NULL).</summary>
    /// <param name="values">The values to store.</param>
    /// <param name="indexName">Code name of the index the row belongs to.</param>
    /// <returns>The row.</returns>
    public static XpSearchSettingsInfo NewRow(SearchSettingsValues values, string indexName)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        return new XpSearchSettingsInfo
        {
            SettingsGuid = Guid.NewGuid(),
            SettingsIndexName = indexName,
            SettingsCacheTtlSeconds = values.CacheTtlSeconds,
            SettingsMaxQueryLength = values.MaxQueryLength,
            SettingsDefaultPageSize = values.DefaultPageSize,
            SettingsMaxPageSize = values.MaxPageSize,
            SettingsMaxFacetValues = values.MaxFacetValues,
            SettingsMaxResultWindow = values.MaxResultWindow,
            SettingsDefaultSuggestLimit = values.DefaultSuggestLimit,
            SettingsMaxSuggestLimit = values.MaxSuggestLimit,
            SettingsRetentionDays = values.RetentionDays,
            SettingsRetentionBatchSize = values.RetentionBatchSize,
            SettingsQuerySuggestionDays = values.QuerySuggestionDays,
            SettingsPopularityLookbackDays = values.PopularityLookbackDays,
            SettingsPopularityDocumentLimit = values.PopularityDocumentLimit,
            SettingsPopularitySuggestionQueries = values.PopularitySuggestionQueries,
            SettingsSynonymWindowSeconds = values.SynonymWindowSeconds,
            SettingsSynonymMinimumOccurrences = values.SynonymMinimumOccurrences
        };
    }
}

/// <summary>Reads one index's stored settings row, or nothing when it has none.</summary>
internal interface IStoredSearchSettingsSource
{
    /// <summary>Reads the stored values of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <returns>The stored values, or <see langword="null"/> when the index has no row.</returns>
    SearchSettingsValues? Get(string indexName);
}

/// <summary>
/// Reads the row from the database, straight, on every call.
/// </summary>
/// <remarks>
/// Deliberately uncached: <see cref="IOptionsMonitor{TOptions}"/> holds each index's built settings
/// until <see cref="XpSearchIndexSettingsInvalidator"/> drops that name, so this runs once per index
/// per save, and a cache in front of it can only serve the values the save just replaced.
/// </remarks>
internal sealed class InfoStoredSearchSettingsSource : IStoredSearchSettingsSource
{
    private readonly IInfoProvider<XpSearchSettingsInfo> rows;

    public InfoStoredSearchSettingsSource(IInfoProvider<XpSearchSettingsInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        this.rows = rows;
    }

    public SearchSettingsValues? Get(string indexName)
    {
        var row = rows.Get()
            .WhereEquals(nameof(XpSearchSettingsInfo.SettingsIndexName), indexName)
            .TopN(1)
            .FirstOrDefault();

        return row is null ? null : StoredSearchSettings.Read(row);
    }
}

/// <summary>
/// Builds one index's <see cref="XpSearchIndexSettings"/> (AR-2): the host's
/// <c>AddXpSearch(o =&gt; ...)</c> values first, then that index's stored row over them.
/// </summary>
/// <remarks>
/// A class implementing <see cref="IConfigureNamedOptions{TOptions}"/> is registered as an
/// <see cref="IConfigureOptions{TOptions}"/>, and the unnamed instance gets the code defaults alone.
/// A host with no database (unit tests, first start before the installer ran) keeps them too: a failed
/// read is logged at Debug and swallowed, because options binding runs on the first search and must
/// not take the request down.
/// </remarks>
internal sealed class XpSearchIndexSettingsSetup : IConfigureNamedOptions<XpSearchIndexSettings>
{
    private readonly IOptions<XpSearchOptions> defaults;
    private readonly IStoredSearchSettingsSource source;
    private readonly ILogger<XpSearchIndexSettingsSetup> logger;

    public XpSearchIndexSettingsSetup(
        IOptions<XpSearchOptions> defaults,
        IStoredSearchSettingsSource source,
        ILogger<XpSearchIndexSettingsSetup> logger)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(logger);

        this.defaults = defaults;
        this.source = source;
        this.logger = logger;
    }

    public void Configure(XpSearchIndexSettings options) =>
        Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

    public void Configure(string? name, XpSearchIndexSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.SetDefaultsFrom(defaults.Value);

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        try
        {
            source.Get(name)?.ApplyTo(options);
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "The stored search settings of index '{Index}' could not be read; the configured values are used.",
                name);
        }
    }
}

/// <summary>
/// Drops one index's built <see cref="XpSearchIndexSettings"/> and its cached responses when its
/// stored row is saved, so an administrator's change is live without an application restart and no
/// other index is disturbed.
/// </summary>
/// <remarks>
/// The trigger is the object type's own insert/update/delete event
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/handle-global-events),
/// which fires wherever the row is written from.
/// The response cache has to go with the settings: a request that omits <c>pageSize</c> computes the
/// same <c>SearchCacheKey</c> before and after the save - the settings shape the response below the
/// key - so a cached response would keep serving the old values for up to the old cache lifetime.
/// </remarks>
internal sealed class XpSearchIndexSettingsInvalidator
{
    private readonly IOptionsMonitorCache<XpSearchIndexSettings> cache;
    private readonly ISearchCache responses;
    private bool started;

    public XpSearchIndexSettingsInvalidator(IOptionsMonitorCache<XpSearchIndexSettings> cache, ISearchCache responses)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(responses);

        this.cache = cache;
        this.responses = responses;
    }

    /// <summary>Subscribes to the row's save events. Calling it twice subscribes once.</summary>
    internal void Start()
    {
        if (started)
        {
            return;
        }

        started = true;

        XpSearchSettingsInfo.TYPEINFO.Events.Insert.After += OnSaved;
        XpSearchSettingsInfo.TYPEINFO.Events.Update.After += OnSaved;
        XpSearchSettingsInfo.TYPEINFO.Events.Delete.After += OnSaved;
    }

    /// <summary>
    /// Forgets one index's settings, so the next <c>Get</c> reads its row again, and drops the
    /// responses answered with the old ones.
    /// </summary>
    /// <param name="indexName">Code name of the index whose row changed.</param>
    internal void Invalidate(string? indexName)
    {
        if (string.IsNullOrEmpty(indexName))
        {
            return;
        }

        cache.TryRemove(indexName);
        responses.Evict(indexName);
    }

    private void OnSaved(object? sender, ObjectEventArgs e) =>
        Invalidate((e?.Object as XpSearchSettingsInfo)?.SettingsIndexName);
}

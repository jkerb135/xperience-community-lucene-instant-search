using CMS.DataEngine;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace XpSearch.Core.Options;

/// <summary>
/// The global search settings as plain numbers (AR-1), free of the storage row: what an administrator
/// edits, what is stored, and what the options overlay writes onto <see cref="XpSearchOptions"/>.
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

    /// <summary>Reads the values off options, which is what a first start seeds the stored row with.</summary>
    /// <param name="options">The options to read.</param>
    /// <returns>The values.</returns>
    public static SearchSettingsValues From(XpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new SearchSettingsValues
        {
            CacheTtlSeconds = (int)Math.Max(0, options.CacheTtl.TotalSeconds),
            MaxQueryLength = options.MaxQueryLength,
            DefaultPageSize = options.DefaultPageSize,
            MaxPageSize = options.MaxPageSize,
            MaxFacetValues = options.MaxFacetValues,
            MaxResultWindow = options.MaxResultWindow,
            DefaultSuggestLimit = options.DefaultSuggestLimit,
            MaxSuggestLimit = options.MaxSuggestLimit,
            RetentionDays = options.Analytics.RetentionDays,
            RetentionBatchSize = options.Analytics.RetentionBatchSize,
            QuerySuggestionDays = options.Analytics.QuerySuggestionDays,
            PopularityLookbackDays = options.Analytics.PopularityLookbackDays,
            PopularityDocumentLimit = options.Analytics.PopularityDocumentLimit,
            PopularitySuggestionQueries = options.Analytics.PopularitySuggestionQueries,
            SynonymWindowSeconds = options.Analytics.SynonymWindowSeconds,
            SynonymMinimumOccurrences = options.Analytics.SynonymMinimumOccurrences
        };
    }

    /// <summary>Writes the values onto options, overwriting whatever the host's lambda configured.</summary>
    /// <param name="options">The options to overwrite.</param>
    public void ApplyTo(XpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.CacheTtl = TimeSpan.FromSeconds(Math.Max(0, CacheTtlSeconds));
        options.MaxQueryLength = MaxQueryLength;
        options.DefaultPageSize = DefaultPageSize;
        options.MaxPageSize = MaxPageSize;
        options.MaxFacetValues = MaxFacetValues;
        options.MaxResultWindow = MaxResultWindow;
        options.DefaultSuggestLimit = DefaultSuggestLimit;
        options.MaxSuggestLimit = MaxSuggestLimit;
        options.Analytics.RetentionDays = RetentionDays;
        options.Analytics.RetentionBatchSize = RetentionBatchSize;
        options.Analytics.QuerySuggestionDays = QuerySuggestionDays;
        options.Analytics.PopularityLookbackDays = PopularityLookbackDays;
        options.Analytics.PopularityDocumentLimit = PopularityDocumentLimit;
        options.Analytics.PopularitySuggestionQueries = PopularitySuggestionQueries;
        options.Analytics.SynonymWindowSeconds = SynonymWindowSeconds;
        options.Analytics.SynonymMinimumOccurrences = SynonymMinimumOccurrences;
    }
}

/// <summary>
/// Turns the stored settings row into <see cref="SearchSettingsValues"/> and back (AR-1). The one place
/// the column mapping lives: the installer's seeding and the administration's Settings page both use it.
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
    /// <returns>The row.</returns>
    public static XpSearchSettingsInfo NewRow(SearchSettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new XpSearchSettingsInfo
        {
            SettingsGuid = Guid.NewGuid(),
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

/// <summary>Reads the single stored settings row, or nothing when the installation has none yet.</summary>
internal interface IStoredSearchSettingsSource
{
    /// <summary>Reads the stored values.</summary>
    /// <returns>The stored values, or <see langword="null"/> when there is no row yet.</returns>
    SearchSettingsValues? Get();
}

/// <summary>
/// Reads the row from the database, straight, on every call.
/// </summary>
/// <remarks>
/// Deliberately uncached: <see cref="IOptionsMonitor{TOptions}"/> holds the built options until
/// <see cref="XpSearchSettingsChangeTokenSource"/> fires, so this runs at most once per save, and a
/// cache in front of it can only serve the values the save just replaced.
/// </remarks>
internal sealed class InfoStoredSearchSettingsSource : IStoredSearchSettingsSource
{
    private readonly IInfoProvider<XpSearchSettingsInfo> rows;

    public InfoStoredSearchSettingsSource(IInfoProvider<XpSearchSettingsInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        this.rows = rows;
    }

    public SearchSettingsValues? Get()
    {
        var row = rows.Get().TopN(1).FirstOrDefault();

        return row is null ? null : StoredSearchSettings.Read(row);
    }
}

/// <summary>
/// Loads the stored global settings into <see cref="XpSearchOptions"/> (AR-1). Registered after the
/// host's <c>AddXpSearch(o =&gt; ...)</c> lambda, so what an administrator saved wins over code.
/// </summary>
/// <remarks>
/// A host with no database (unit tests, first start before the installer ran) keeps the code-configured
/// values: a failed read is logged at Debug and swallowed, because options binding runs on the first
/// search and must not take the request down.
/// </remarks>
internal sealed class XpSearchStoredSettingsConfigureOptions : IConfigureOptions<XpSearchOptions>
{
    private readonly IStoredSearchSettingsSource source;
    private readonly ILogger<XpSearchStoredSettingsConfigureOptions> logger;

    public XpSearchStoredSettingsConfigureOptions(
        IStoredSearchSettingsSource source,
        ILogger<XpSearchStoredSettingsConfigureOptions> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(logger);

        this.source = source;
        this.logger = logger;
    }

    public void Configure(XpSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        SearchSettingsValues? values;

        try
        {
            values = source.Get();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The stored search settings could not be read; the configured values are used.");

            return;
        }

        values?.ApplyTo(options);
    }
}

/// <summary>
/// Makes <see cref="IOptionsMonitor{TOptions}"/> reload <see cref="XpSearchOptions"/> when the stored
/// settings row is saved, so an administrator's change is live without an application restart.
/// </summary>
/// <remarks>
/// The trigger is the object type's own insert/update event
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/handle-global-events),
/// which fires wherever the row is written from.
/// </remarks>
internal sealed class XpSearchSettingsChangeTokenSource : IOptionsChangeTokenSource<XpSearchOptions>, IDisposable
{
    private CancellationTokenSource changed = new();

    public XpSearchSettingsChangeTokenSource()
    {
        XpSearchSettingsInfo.TYPEINFO.Events.Insert.After += OnSaved;
        XpSearchSettingsInfo.TYPEINFO.Events.Update.After += OnSaved;
    }

    public string Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref changed).Token);

    public void Dispose()
    {
        XpSearchSettingsInfo.TYPEINFO.Events.Insert.After -= OnSaved;
        XpSearchSettingsInfo.TYPEINFO.Events.Update.After -= OnSaved;

        Volatile.Read(ref changed).Dispose();
    }

    private void OnSaved(object? sender, ObjectEventArgs e)
    {
        var previous = Interlocked.Exchange(ref changed, new CancellationTokenSource());

        previous.Cancel();
        previous.Dispose();
    }
}

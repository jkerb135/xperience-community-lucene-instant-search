using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Options;

[assembly: RegisterObjectType(typeof(XpSearchSettingsInfo), XpSearchSettingsInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Options;

/// <summary>
/// One index's search settings as an administrator edits them (AR-2). Everything on it is copied onto
/// that index's <see cref="XpSearchIndexSettings"/> by <see cref="XpSearchIndexSettingsSetup"/>.
/// </summary>
/// <remarks>
/// One row per index, written only by a save on the Search settings page. Kentico's own
/// <c>SettingsKeyInfo</c> is not used because the documented way to add settings of your own is a
/// custom object type with your own UI
/// (https://docs.kentico.com/guides/development/customizations-and-integrations/create-basic-module).
/// </remarks>
public class XpSearchSettingsInfo : AbstractInfo<XpSearchSettingsInfo, IInfoProvider<XpSearchSettingsInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.settings";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.Settings";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchSettingsInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchSettingsInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(SettingsID),
            null,
            nameof(SettingsGuid),
            null,
            null,
            null,
            null,
            null)
        {
            // The options overlay reads this row uncached - the options monitor is its cache - so nothing
            // here depends on the dummy cache keys today; a save touches them anyway, so anything that
            // does take a ForInfoObjects<T>().All() dependency on it later is invalidated
            // (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchSettingsInfo"/> class.</summary>
    public XpSearchSettingsInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchSettingsInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchSettingsInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int SettingsID
    {
        get => Integer(nameof(SettingsID));
        set => SetValue(nameof(SettingsID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid SettingsGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SettingsGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SettingsGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the settings belong to.</summary>
    [DatabaseField]
    public virtual string SettingsIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(SettingsIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SettingsIndexName), value);
    }

    /// <summary>Gets or sets how long an identical query is served from cache, in seconds.</summary>
    [DatabaseField]
    public virtual int SettingsCacheTtlSeconds
    {
        get => Integer(nameof(SettingsCacheTtlSeconds));
        set => SetValue(nameof(SettingsCacheTtlSeconds), value);
    }

    /// <summary>Gets or sets the maximum accepted length of the query text.</summary>
    [DatabaseField]
    public virtual int SettingsMaxQueryLength
    {
        get => Integer(nameof(SettingsMaxQueryLength));
        set => SetValue(nameof(SettingsMaxQueryLength), value);
    }

    /// <summary>Gets or sets the page size used when a request omits one.</summary>
    [DatabaseField]
    public virtual int SettingsDefaultPageSize
    {
        get => Integer(nameof(SettingsDefaultPageSize));
        set => SetValue(nameof(SettingsDefaultPageSize), value);
    }

    /// <summary>Gets or sets the server-side page size ceiling.</summary>
    [DatabaseField]
    public virtual int SettingsMaxPageSize
    {
        get => Integer(nameof(SettingsMaxPageSize));
        set => SetValue(nameof(SettingsMaxPageSize), value);
    }

    /// <summary>Gets or sets the maximum number of values returned per facet dimension.</summary>
    [DatabaseField]
    public virtual int SettingsMaxFacetValues
    {
        get => Integer(nameof(SettingsMaxFacetValues));
        set => SetValue(nameof(SettingsMaxFacetValues), value);
    }

    /// <summary>Gets or sets how deep paging may go.</summary>
    [DatabaseField]
    public virtual int SettingsMaxResultWindow
    {
        get => Integer(nameof(SettingsMaxResultWindow));
        set => SetValue(nameof(SettingsMaxResultWindow), value);
    }

    /// <summary>Gets or sets the number of suggestions returned when a request omits a limit.</summary>
    [DatabaseField]
    public virtual int SettingsDefaultSuggestLimit
    {
        get => Integer(nameof(SettingsDefaultSuggestLimit));
        set => SetValue(nameof(SettingsDefaultSuggestLimit), value);
    }

    /// <summary>Gets or sets the ceiling on the suggestion limit.</summary>
    [DatabaseField]
    public virtual int SettingsMaxSuggestLimit
    {
        get => Integer(nameof(SettingsMaxSuggestLimit));
        set => SetValue(nameof(SettingsMaxSuggestLimit), value);
    }

    /// <summary>Gets or sets how many days of search analytics are kept.</summary>
    [DatabaseField]
    public virtual int SettingsRetentionDays
    {
        get => Integer(nameof(SettingsRetentionDays));
        set => SetValue(nameof(SettingsRetentionDays), value);
    }

    /// <summary>Gets or sets how many rows the retention task deletes per batch.</summary>
    [DatabaseField]
    public virtual int SettingsRetentionBatchSize
    {
        get => Integer(nameof(SettingsRetentionBatchSize));
        set => SetValue(nameof(SettingsRetentionBatchSize), value);
    }

    /// <summary>Gets or sets how far back query suggestions count query volume, in days.</summary>
    [DatabaseField]
    public virtual int SettingsQuerySuggestionDays
    {
        get => Integer(nameof(SettingsQuerySuggestionDays));
        set => SetValue(nameof(SettingsQuerySuggestionDays), value);
    }

    /// <summary>Gets or sets how many days of clicks the popularity signal is computed from.</summary>
    [DatabaseField]
    public virtual int SettingsPopularityLookbackDays
    {
        get => Integer(nameof(SettingsPopularityLookbackDays));
        set => SetValue(nameof(SettingsPopularityLookbackDays), value);
    }

    /// <summary>Gets or sets how many documents per index the popularity signal keeps.</summary>
    [DatabaseField]
    public virtual int SettingsPopularityDocumentLimit
    {
        get => Integer(nameof(SettingsPopularityDocumentLimit));
        set => SetValue(nameof(SettingsPopularityDocumentLimit), value);
    }

    /// <summary>Gets or sets how many frequent queries are examined for a suggested boost rule.</summary>
    [DatabaseField]
    public virtual int SettingsPopularitySuggestionQueries
    {
        get => Integer(nameof(SettingsPopularitySuggestionQueries));
        set => SetValue(nameof(SettingsPopularitySuggestionQueries), value);
    }

    /// <summary>Gets or sets the reformulation window synonym mining uses, in seconds.</summary>
    [DatabaseField]
    public virtual int SettingsSynonymWindowSeconds
    {
        get => Integer(nameof(SettingsSynonymWindowSeconds));
        set => SetValue(nameof(SettingsSynonymWindowSeconds), value);
    }

    /// <summary>Gets or sets how often a reformulation has to happen before it is suggested.</summary>
    [DatabaseField]
    public virtual int SettingsSynonymMinimumOccurrences
    {
        get => Integer(nameof(SettingsSynonymMinimumOccurrences));
        set => SetValue(nameof(SettingsSynonymMinimumOccurrences), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);

    private int Integer(string column) =>
        ValidationHelper.GetInteger(GetValue(column), 0, CultureInfo.InvariantCulture);
}

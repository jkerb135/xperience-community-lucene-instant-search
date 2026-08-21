using System.Data;
using System.Globalization;

using CMS.DataEngine;
using CMS.Helpers;

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// One recorded write operation (spec §10.4: "Log every write operation with key prefix, index,
/// document count, and outcome"). The Search tuning application's Ingestion log page (spec §10.8)
/// lists these rows.
/// </summary>
public class XpSearchIngestionLogInfo : AbstractInfo<XpSearchIngestionLogInfo, IInfoProvider<XpSearchIngestionLogInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.ingestionlog";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.IngestionLog";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchIngestionLogInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchIngestionLogInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(LogID),
            null,
            nameof(LogGuid),
            null,
            null,
            null,
            null,
            null);

    /// <summary>Creates an empty instance of the <see cref="XpSearchIngestionLogInfo"/> class.</summary>
    public XpSearchIngestionLogInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchIngestionLogInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchIngestionLogInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int LogID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(LogID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid LogGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(LogGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogGuid), value);
    }

    /// <summary>Gets or sets the prefix of the API key behind the operation, or <c>in-process</c>.</summary>
    [DatabaseField]
    public virtual string LogKeyPrefix
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogKeyPrefix)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogKeyPrefix), value);
    }

    /// <summary>Gets or sets the code name of the index written to.</summary>
    [DatabaseField]
    public virtual string LogIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogIndexName), value);
    }

    /// <summary>Gets or sets what was asked for: <c>upsert</c>, <c>patch</c>, <c>delete</c>, <c>clear</c> or <c>rebuild</c>.</summary>
    [DatabaseField]
    public virtual string LogOperation
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogOperation)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogOperation), value);
    }

    /// <summary>Gets or sets how many documents the operation touched.</summary>
    [DatabaseField]
    public virtual int LogDocumentCount
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(LogDocumentCount)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogDocumentCount), value);
    }

    /// <summary>Gets or sets whether the operation was accepted.</summary>
    [DatabaseField]
    public virtual bool LogSucceeded
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(LogSucceeded)), false);
        set => SetValue(nameof(LogSucceeded), value);
    }

    /// <summary>Gets or sets the outcome description.</summary>
    [DatabaseField]
    public virtual string LogMessage
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogMessage)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogMessage), value);
    }

    /// <summary>Gets or sets when the operation happened, in UTC.</summary>
    [DatabaseField]
    public virtual DateTime LogCreatedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(LogCreatedAt)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogCreatedAt), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

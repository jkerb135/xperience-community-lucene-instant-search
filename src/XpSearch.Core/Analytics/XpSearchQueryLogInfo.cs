using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Analytics;

[assembly: RegisterObjectType(typeof(XpSearchQueryLogInfo), XpSearchQueryLogInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Analytics;

/// <summary>
/// One logged search (spec §9.2). Anonymous by design: no contact, no visitor identifier and nothing
/// else personal, which is why these rows are written whether or not the visitor consented to
/// tracking.
/// </summary>
public class XpSearchQueryLogInfo : AbstractInfo<XpSearchQueryLogInfo, IInfoProvider<XpSearchQueryLogInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.querylog";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.QueryLog";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchQueryLogInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchQueryLogInfo>),
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

    /// <summary>Creates an empty instance of the <see cref="XpSearchQueryLogInfo"/> class.</summary>
    public XpSearchQueryLogInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchQueryLogInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchQueryLogInfo(DataRow dr)
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

    /// <summary>
    /// Gets or sets the correlation id of the search, echoed to the caller as <c>queryId</c>. A click
    /// event carries it back, which is how <see cref="LogClickedPosition"/> finds its row.
    /// </summary>
    [DatabaseField]
    public virtual string LogQueryID
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogQueryID)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogQueryID), value);
    }

    /// <summary>Gets or sets the code name of the index that was searched.</summary>
    [DatabaseField]
    public virtual string LogIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogIndexName), value);
    }

    /// <summary>Gets or sets the searched text, normalized and lowercased.</summary>
    [DatabaseField]
    public virtual string LogQueryText
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogQueryText)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogQueryText), value);
    }

    /// <summary>Gets or sets how many documents matched.</summary>
    [DatabaseField]
    public virtual int LogResultCount
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(LogResultCount)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogResultCount), value);
    }

    /// <summary>Gets or sets when the search ran, in UTC.</summary>
    [DatabaseField]
    public virtual DateTime LogTimestamp
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(LogTimestamp)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogTimestamp), value);
    }

    /// <summary>Gets or sets the code name of the website channel the search came from, if any.</summary>
    [DatabaseField]
    public virtual string LogChannelName
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogChannelName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogChannelName), value);
    }

    /// <summary>Gets or sets the language the search asked for, if any.</summary>
    [DatabaseField]
    public virtual string LogLanguage
    {
        get => ValidationHelper.GetString(GetValue(nameof(LogLanguage)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogLanguage), value);
    }

    /// <summary>Gets or sets the one-based position of the clicked result, or zero when nothing was clicked.</summary>
    [DatabaseField]
    public virtual int LogClickedPosition
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(LogClickedPosition)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogClickedPosition), value);
    }

    /// <summary>Gets or sets the server-side processing time of the search, in milliseconds.</summary>
    [DatabaseField]
    public virtual int LogProcessingTimeMs
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(LogProcessingTimeMs)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(LogProcessingTimeMs), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

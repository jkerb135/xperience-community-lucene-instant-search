using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Popularity;

[assembly: RegisterObjectType(typeof(XpSearchPopularityScoreInfo), XpSearchPopularityScoreInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Popularity;

/// <summary>
/// One document's popularity score in one index (RK-1): the position-damped click mass the last
/// aggregation run found for it. The run replaces an index's rows wholesale, so popularity outside
/// the lookback window decays by being left out.
/// </summary>
public class XpSearchPopularityScoreInfo : AbstractInfo<XpSearchPopularityScoreInfo, IInfoProvider<XpSearchPopularityScoreInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.popularityscore";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.PopularityScore";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchPopularityScoreInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchPopularityScoreInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(ScoreID),
            null,
            nameof(ScoreGuid),
            null,
            null,
            null,
            null,
            null)
        {
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchPopularityScoreInfo"/> class.</summary>
    public XpSearchPopularityScoreInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchPopularityScoreInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchPopularityScoreInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int ScoreID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(ScoreID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid ScoreGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(ScoreGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the score belongs to.</summary>
    [DatabaseField]
    public virtual string ScoreIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(ScoreIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreIndexName), value);
    }

    /// <summary>Gets or sets the result id of the document.</summary>
    [DatabaseField]
    public virtual string ScoreDocumentID
    {
        get => ValidationHelper.GetString(GetValue(nameof(ScoreDocumentID)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreDocumentID), value);
    }

    /// <summary>Gets or sets the damped click mass.</summary>
    [DatabaseField]
    public virtual double ScoreValue
    {
        get => ValidationHelper.GetDouble(GetValue(nameof(ScoreValue)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreValue), value);
    }

    /// <summary>Gets or sets when the score was computed, in UTC.</summary>
    [DatabaseField]
    public virtual DateTime ScoreComputed
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(ScoreComputed)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ScoreComputed), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;

[assembly: RegisterObjectType(typeof(XpSearchStopwordListInfo), XpSearchStopwordListInfo.OBJECT_TYPE)]

namespace XpSearch.Admin.Persistence;

/// <summary>
/// The stopwords of one index, newline-separated. Spec §8.1 asks for a Stopwords edit page but
/// §8.2 defines no class for it; one row per index with a single text field is the smallest thing
/// that serves that page - see ADR-0014.
/// </summary>
public class XpSearchStopwordListInfo : AbstractInfo<XpSearchStopwordListInfo, IInfoProvider<XpSearchStopwordListInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.stopwordlist";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.StopwordList";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchStopwordListInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchStopwordListInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(StopwordListID),
            null,
            nameof(StopwordListGuid),
            null,
            null,
            null,
            null,
            null)
        {
            // The tuning cache depends on these objects, so every change has to touch their dummy
            // cache keys (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchStopwordListInfo"/> class.</summary>
    public XpSearchStopwordListInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchStopwordListInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchStopwordListInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int StopwordListID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(StopwordListID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(StopwordListID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid StopwordListGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(StopwordListGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(StopwordListGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the list belongs to.</summary>
    [DatabaseField]
    public virtual string StopwordListIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(StopwordListIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(StopwordListIndexName), value);
    }

    /// <summary>Gets or sets the stopwords, one per line.</summary>
    [DatabaseField]
    public virtual string StopwordListWords
    {
        get => ValidationHelper.GetString(GetValue(nameof(StopwordListWords)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(StopwordListWords), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

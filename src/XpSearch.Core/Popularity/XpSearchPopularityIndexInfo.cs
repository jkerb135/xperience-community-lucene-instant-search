using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Popularity;

[assembly: RegisterObjectType(typeof(XpSearchPopularityIndexInfo), XpSearchPopularityIndexInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Popularity;

/// <summary>
/// One index's popularity settings (RK-1): whether it opted in to the boost, and which run last
/// computed its signal.
/// </summary>
/// <remarks>
/// The opt-in is deliberately not one of the four variant-cloned tuning types: an experiment tests
/// tuning, not popularity, and both variants of an experiment see the same boost (ADR-0025).
/// </remarks>
public class XpSearchPopularityIndexInfo : AbstractInfo<XpSearchPopularityIndexInfo, IInfoProvider<XpSearchPopularityIndexInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.popularityindex";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.PopularityIndex";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchPopularityIndexInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchPopularityIndexInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(PopularityIndexID),
            null,
            nameof(PopularityIndexGuid),
            null,
            null,
            null,
            null,
            null)
        {
            // The signal is cached per index, so a task run and a toggle both have to touch the dummy
            // cache keys (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchPopularityIndexInfo"/> class.</summary>
    public XpSearchPopularityIndexInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchPopularityIndexInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchPopularityIndexInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int PopularityIndexID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(PopularityIndexID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(PopularityIndexID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid PopularityIndexGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(PopularityIndexGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(PopularityIndexGuid), value);
    }

    /// <summary>Gets or sets the code name of the index these settings belong to.</summary>
    [DatabaseField]
    public virtual string PopularityIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(PopularityIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(PopularityIndexName), value);
    }

    /// <summary>Gets or sets a value indicating whether the index opted in to the popularity boost. Off by default.</summary>
    [DatabaseField]
    public virtual bool PopularityIndexEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(PopularityIndexEnabled)), false);
        set => SetValue(nameof(PopularityIndexEnabled), value);
    }

    /// <summary>Gets or sets when the signal was last computed, in UTC. Its ticks are the signal version.</summary>
    [DatabaseField]
    public virtual DateTime PopularityIndexComputed
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(PopularityIndexComputed)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(PopularityIndexComputed), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}


using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Fuzzy;

[assembly: RegisterObjectType(typeof(XpSearchFuzzyIndexInfo), XpSearchFuzzyIndexInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Fuzzy;

/// <summary>
/// One index's typo tolerance setting (FZ-1): whether free-text terms also match near-spellings.
/// </summary>
/// <remarks>
/// Index-wide, not one of the variant-cloned tuning types: an experiment tests tuning, and both of its
/// variants see the same typo tolerance (the reasoning of ADR-0025, as for the popularity opt-in).
/// </remarks>
public class XpSearchFuzzyIndexInfo : AbstractInfo<XpSearchFuzzyIndexInfo, IInfoProvider<XpSearchFuzzyIndexInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.fuzzyindex";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.FuzzyIndex";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchFuzzyIndexInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchFuzzyIndexInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(FuzzyIndexID),
            null,
            nameof(FuzzyIndexGuid),
            null,
            null,
            null,
            null,
            null)
        {
            // The setting is cached per index and joins the response cache key, so the toggle has to
            // touch the dummy cache keys
            // (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchFuzzyIndexInfo"/> class.</summary>
    public XpSearchFuzzyIndexInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchFuzzyIndexInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchFuzzyIndexInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int FuzzyIndexID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(FuzzyIndexID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(FuzzyIndexID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid FuzzyIndexGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(FuzzyIndexGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(FuzzyIndexGuid), value);
    }

    /// <summary>Gets or sets the code name of the index these settings belong to.</summary>
    [DatabaseField]
    public virtual string FuzzyIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(FuzzyIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(FuzzyIndexName), value);
    }

    /// <summary>Gets or sets a value indicating whether the index opted in to typo tolerance. Off by default.</summary>
    [DatabaseField]
    public virtual bool FuzzyIndexEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(FuzzyIndexEnabled)), false);
        set => SetValue(nameof(FuzzyIndexEnabled), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

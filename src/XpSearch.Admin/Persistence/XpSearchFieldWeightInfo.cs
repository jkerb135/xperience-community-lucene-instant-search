using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;

[assembly: RegisterObjectType(typeof(XpSearchFieldWeightInfo), XpSearchFieldWeightInfo.OBJECT_TYPE)]

namespace XpSearch.Admin.Persistence;

/// <summary>
/// One per-field score multiplier (spec §8.2). A weight of 1.0 changes nothing.
/// </summary>
public class XpSearchFieldWeightInfo : AbstractInfo<XpSearchFieldWeightInfo, IInfoProvider<XpSearchFieldWeightInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.fieldweight";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.FieldWeight";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchFieldWeightInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchFieldWeightInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(WeightID),
            null,
            nameof(WeightGuid),
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

    /// <summary>Creates an empty instance of the <see cref="XpSearchFieldWeightInfo"/> class.</summary>
    public XpSearchFieldWeightInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchFieldWeightInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchFieldWeightInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int WeightID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(WeightID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(WeightID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid WeightGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(WeightGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(WeightGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the weight applies to.</summary>
    [DatabaseField]
    public virtual string WeightIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(WeightIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(WeightIndexName), value);
    }

    /// <summary>Gets or sets the schema field the weight applies to.</summary>
    [DatabaseField]
    public virtual string WeightFieldName
    {
        get => ValidationHelper.GetString(GetValue(nameof(WeightFieldName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(WeightFieldName), value);
    }

    /// <summary>Gets or sets the multiplier; 1.0 by default.</summary>
    [DatabaseField]
    public virtual decimal WeightValue
    {
        get => ValidationHelper.GetDecimal(GetValue(nameof(WeightValue)), 1m, CultureInfo.InvariantCulture);
        set => SetValue(nameof(WeightValue), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;

[assembly: RegisterObjectType(typeof(XpSearchSynonymInfo), XpSearchSynonymInfo.OBJECT_TYPE)]

namespace XpSearch.Admin.Persistence;

/// <summary>
/// One synonym group (spec §8.2). Terms are comma-separated in both the input and the output.
/// </summary>
public class XpSearchSynonymInfo : AbstractInfo<XpSearchSynonymInfo, IInfoProvider<XpSearchSynonymInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.synonym";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.Synonym";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchSynonymInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchSynonymInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(SynonymID),
            null,
            nameof(SynonymGuid),
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

    /// <summary>Creates an empty instance of the <see cref="XpSearchSynonymInfo"/> class.</summary>
    public XpSearchSynonymInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchSynonymInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchSynonymInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int SynonymID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SynonymID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid SynonymGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SynonymGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the group applies to.</summary>
    [DatabaseField]
    public virtual string SynonymIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymIndexName), value);
    }

    /// <summary>Gets or sets the direction; see <see cref="XpSearch.Core.Tuning.SynonymDirection"/>.</summary>
    [DatabaseField]
    public virtual int SynonymType
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SynonymType)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymType), value);
    }

    /// <summary>Gets or sets the comma-separated terms that trigger the expansion.</summary>
    [DatabaseField]
    public virtual string SynonymInput
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymInput)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymInput), value);
    }

    /// <summary>Gets or sets the comma-separated terms a one-way group expands to.</summary>
    [DatabaseField]
    public virtual string SynonymOutput
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymOutput)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymOutput), value);
    }

    /// <summary>Gets or sets whether the group is applied.</summary>
    [DatabaseField]
    public virtual bool SynonymEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(SynonymEnabled)), false);
        set => SetValue(nameof(SynonymEnabled), value);
    }

    /// <summary>
    /// Gets or sets the experiment this synonym group is the variant-B draft of, or
    /// <see langword="null"/> when it is live (XP-1). Every live read filters on it being null.
    /// </summary>
    [DatabaseField]
    public virtual int? SynonymExperimentID
    {
        get => GetValue(nameof(SynonymExperimentID)) as int?;
        set => SetValue(nameof(SynonymExperimentID), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

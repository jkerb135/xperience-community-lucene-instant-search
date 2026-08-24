using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;

[assembly: RegisterObjectType(typeof(XpSearchRuleInfo), XpSearchRuleInfo.OBJECT_TYPE)]

namespace XpSearch.Admin.Persistence;

/// <summary>
/// One relevance rule (spec §8.2). Read by <see cref="XpSearch.Core.Tuning.IRelevanceTuningSource"/>
/// on every query, through the cache spec §8.5 asks for.
/// </summary>
public class XpSearchRuleInfo : AbstractInfo<XpSearchRuleInfo, IInfoProvider<XpSearchRuleInfo>>, IInfoWithId, IInfoWithName
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.rule";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.Rule";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchRuleInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchRuleInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(RuleID),
            null,
            nameof(RuleGuid),
            null,
            nameof(RuleName),
            null,
            null,
            null)
        {
            // The tuning cache depends on these objects, so every change has to touch their dummy
            // cache keys (https://docs.kentico.com/documentation/developers-and-admins/development/caching/cache-dependencies).
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchRuleInfo"/> class.</summary>
    public XpSearchRuleInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchRuleInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchRuleInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int RuleID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(RuleID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid RuleGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(RuleGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the rule applies to.</summary>
    [DatabaseField]
    public virtual string RuleIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(RuleIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleIndexName), value);
    }

    /// <summary>Gets or sets the display name, which is what the explanation shows.</summary>
    [DatabaseField]
    public virtual string RuleName
    {
        get => ValidationHelper.GetString(GetValue(nameof(RuleName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleName), value);
    }

    /// <summary>Gets or sets whether the rule is considered at all.</summary>
    [DatabaseField]
    public virtual bool RuleEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(RuleEnabled)), false);
        set => SetValue(nameof(RuleEnabled), value);
    }

    /// <summary>
    /// Gets or sets the <c>if</c> of the rule, as the JSON object <see cref="RuleJson"/> describes.
    /// An empty value means the row is still in the pre-CR-4b flat shape; see
    /// <see cref="RuleStorageMigration"/>.
    /// </summary>
    [DatabaseField]
    public virtual string RuleConditions
    {
        get => ValidationHelper.GetString(GetValue(nameof(RuleConditions)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleConditions), value);
    }

    /// <summary>
    /// Gets or sets the <c>then</c> of the rule, as the JSON array <see cref="RuleJson"/> describes,
    /// in the order the consequences are applied.
    /// </summary>
    [DatabaseField]
    public virtual string RuleConsequences
    {
        get => ValidationHelper.GetString(GetValue(nameof(RuleConsequences)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RuleConsequences), value);
    }

    /// <summary>
    /// Gets or sets whether the rule was converted from the pre-CR-4b flat storage and has not been
    /// saved from the rule builder since. It only drives the "converted from the previous format"
    /// note the builder shows once; nothing about matching depends on it.
    /// </summary>
    [DatabaseField]
    public virtual bool RuleMigrated
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(RuleMigrated)), false);
        set => SetValue(nameof(RuleMigrated), value);
    }

    /// <summary>Gets or sets when the rule starts applying, in UTC. Null means "already".</summary>
    [DatabaseField]
    public virtual DateTime? RuleValidFrom
    {
        get => GetValue(nameof(RuleValidFrom)) as DateTime?;
        set => SetValue(nameof(RuleValidFrom), value);
    }

    /// <summary>Gets or sets when the rule stops applying, in UTC. Null means "forever".</summary>
    [DatabaseField]
    public virtual DateTime? RuleValidTo
    {
        get => GetValue(nameof(RuleValidTo)) as DateTime?;
        set => SetValue(nameof(RuleValidTo), value);
    }

    /// <summary>Gets or sets the conflict resolution order; lower runs first.</summary>
    [DatabaseField]
    public virtual int RulePriority
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(RulePriority)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(RulePriority), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

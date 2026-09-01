using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Admin.Persistence;

[assembly: RegisterObjectType(typeof(XpSearchExperimentInfo), XpSearchExperimentInfo.OBJECT_TYPE)]

namespace XpSearch.Admin.Persistence;

/// <summary>What stage of its life an experiment is in (XP-1). The numbers are the stored values.</summary>
public enum ExperimentState
{
    /// <summary>Being set up: its variant-B tuning is editable and no visitor sees it.</summary>
    Draft = 0,

    /// <summary>Splitting live traffic between the live tuning and its draft.</summary>
    Running = 1,

    /// <summary>Over. Its draft rows have been promoted to live or deleted.</summary>
    Concluded = 2
}

/// <summary>How an experiment ended (XP-1). The numbers are the stored values.</summary>
public enum ExperimentOutcome
{
    /// <summary>Not concluded yet.</summary>
    None = 0,

    /// <summary>Variant B replaced the live tuning.</summary>
    Promoted = 1,

    /// <summary>Variant B was thrown away and the live tuning kept.</summary>
    Discarded = 2
}

/// <summary>
/// One A/B test of two tunings of one index (amendment 2026-08-25). Variant A is the index's live
/// tuning; variant B is the set of tuning rows that carry this experiment's identifier.
/// </summary>
public class XpSearchExperimentInfo : AbstractInfo<XpSearchExperimentInfo, IInfoProvider<XpSearchExperimentInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.experiment";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.Experiment";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchExperimentInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchExperimentInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(ExperimentID),
            null,
            nameof(ExperimentGuid),
            null,
            // Display name column: EditSectionPage names the breadcrumb and the sidebar section from
            // the object's display name, which without this falls back to the code name and then the GUID.
            nameof(ExperimentDisplayName),
            null,
            null,
            null)
        {
            // The "which experiment is running on this index" lookup is cached per index and read on
            // every search, so starting or concluding one has to touch its dummy cache keys.
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchExperimentInfo"/> class.</summary>
    public XpSearchExperimentInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchExperimentInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchExperimentInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int ExperimentID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(ExperimentID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentID), value);
    }

    /// <summary>Gets or sets the object GUID. Bucketing hashes it, so two experiments split traffic differently.</summary>
    [DatabaseField]
    public virtual Guid ExperimentGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(ExperimentGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the experiment tests.</summary>
    [DatabaseField]
    public virtual string ExperimentIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(ExperimentIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentIndexName), value);
    }

    /// <summary>Gets or sets what the editor calls the experiment.</summary>
    [DatabaseField]
    public virtual string ExperimentDisplayName
    {
        get => ValidationHelper.GetString(GetValue(nameof(ExperimentDisplayName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentDisplayName), value);
    }

    /// <summary>Gets or sets the percentage of traffic sent to variant B, 1 to 99.</summary>
    [DatabaseField]
    public virtual int ExperimentSplitPercent
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(ExperimentSplitPercent)), 50, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentSplitPercent), value);
    }

    /// <summary>Gets or sets the state; see <see cref="ExperimentState"/>.</summary>
    [DatabaseField]
    public virtual int ExperimentState
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(ExperimentState)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentState), value);
    }

    /// <summary>Gets or sets when the experiment started splitting traffic, in UTC. Null until it is started.</summary>
    [DatabaseField]
    public virtual DateTime? ExperimentStarted
    {
        get => GetValue(nameof(ExperimentStarted)) as DateTime?;
        set => SetValue(nameof(ExperimentStarted), value);
    }

    /// <summary>Gets or sets when the experiment was concluded, in UTC. Null until it is.</summary>
    [DatabaseField]
    public virtual DateTime? ExperimentEnded
    {
        get => GetValue(nameof(ExperimentEnded)) as DateTime?;
        set => SetValue(nameof(ExperimentEnded), value);
    }

    /// <summary>Gets or sets how the experiment ended; see <see cref="ExperimentOutcome"/>.</summary>
    [DatabaseField]
    public virtual int ExperimentConcludedOutcome
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(ExperimentConcludedOutcome)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(ExperimentConcludedOutcome), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

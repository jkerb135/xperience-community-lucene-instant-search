using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Helpers;
using CMS.Modules;

using Microsoft.Extensions.Options;

namespace XpSearch.Core.Analytics;

/// <summary>
/// Creates the module and the data class the aggregate query log is stored in, the first time the
/// application starts. Mirrors <c>XpSearchIngestionModuleInstaller</c>, which follows the object type
/// pattern (https://docs.kentico.com/documentation/developers-and-admins/customization/object-types).
/// </summary>
/// <remarks>
/// The resource name is prefixed with <c>CMS.</c> so the class code generator skips the class: it is
/// the library's own storage, not project data a developer generates models for.
/// </remarks>
public sealed class XpSearchAnalyticsModuleInstaller
{
    /// <summary>Code name of the module the class belongs to.</summary>
    public const string ResourceName = "CMS.Integration.XpSearchAnalytics";

    /// <summary>The columns of <see cref="Options.XpSearchSettingsInfo"/> that carry a global setting (AR-1).</summary>
    private static readonly string[] SettingsColumns =
    [
        nameof(Options.XpSearchSettingsInfo.SettingsCacheTtlSeconds),
        nameof(Options.XpSearchSettingsInfo.SettingsMaxQueryLength),
        nameof(Options.XpSearchSettingsInfo.SettingsDefaultPageSize),
        nameof(Options.XpSearchSettingsInfo.SettingsMaxPageSize),
        nameof(Options.XpSearchSettingsInfo.SettingsMaxFacetValues),
        nameof(Options.XpSearchSettingsInfo.SettingsMaxResultWindow),
        nameof(Options.XpSearchSettingsInfo.SettingsDefaultSuggestLimit),
        nameof(Options.XpSearchSettingsInfo.SettingsMaxSuggestLimit),
        nameof(Options.XpSearchSettingsInfo.SettingsRetentionDays),
        nameof(Options.XpSearchSettingsInfo.SettingsRetentionBatchSize),
        nameof(Options.XpSearchSettingsInfo.SettingsQuerySuggestionDays),
        nameof(Options.XpSearchSettingsInfo.SettingsPopularityLookbackDays),
        nameof(Options.XpSearchSettingsInfo.SettingsPopularityDocumentLimit),
        nameof(Options.XpSearchSettingsInfo.SettingsPopularitySuggestionQueries),
        nameof(Options.XpSearchSettingsInfo.SettingsSynonymWindowSeconds),
        nameof(Options.XpSearchSettingsInfo.SettingsSynonymMinimumOccurrences)
    ];

    private readonly IInfoProvider<ResourceInfo> resources;
    private readonly IInfoProvider<Options.XpSearchSettingsInfo> settings;
    private readonly IOptions<Options.XpSearchOptions> options;

    /// <summary>Initializes a new instance of the <see cref="XpSearchAnalyticsModuleInstaller"/> class.</summary>
    /// <param name="resources">Provider of <see cref="ResourceInfo"/>, used to create the module.</param>
    /// <param name="settings">Provider of the global settings row (AR-1).</param>
    /// <param name="options">The code-configured options the settings row is seeded from.</param>
    public XpSearchAnalyticsModuleInstaller(
        IInfoProvider<ResourceInfo> resources,
        IInfoProvider<Options.XpSearchSettingsInfo> settings,
        IOptions<Options.XpSearchOptions> options)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        this.resources = resources;
        this.settings = settings;
        this.options = options;
    }

    /// <summary>The form definition of <see cref="XpSearchQueryLogInfo"/>.</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo QueryLogForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchQueryLogInfo.LogID));

        Add(form, nameof(XpSearchQueryLogInfo.LogGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchQueryLogInfo.LogQueryID), FieldDataType.Text, size: 64);
        Add(form, nameof(XpSearchQueryLogInfo.LogIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchQueryLogInfo.LogQueryText), FieldDataType.Text, size: 450);
        Add(form, nameof(XpSearchQueryLogInfo.LogResultCount), FieldDataType.Integer);
        Add(form, nameof(XpSearchQueryLogInfo.LogTimestamp), FieldDataType.DateTime);
        Add(form, nameof(XpSearchQueryLogInfo.LogChannelName), FieldDataType.Text, size: 100, allowEmpty: true);
        Add(form, nameof(XpSearchQueryLogInfo.LogLanguage), FieldDataType.Text, size: 50, allowEmpty: true);
        Add(form, nameof(XpSearchQueryLogInfo.LogClickedPosition), FieldDataType.Integer, allowEmpty: true);
        Add(form, nameof(XpSearchQueryLogInfo.LogProcessingTimeMs), FieldDataType.Integer);

        // XP-1. Nullable, so an upgraded installation keeps its existing rows: CombineWithForm adds
        // the columns to the installed class, and a nullable column needs no backfill.
        Add(form, nameof(XpSearchQueryLogInfo.LogExperimentID), FieldDataType.Integer, allowEmpty: true);
        Add(form, nameof(XpSearchQueryLogInfo.LogVariant), FieldDataType.Text, size: 1, allowEmpty: true);

        // RK-1, nullable for the same reason: rows logged before the upgrade have no clicked document.
        Add(form, nameof(XpSearchQueryLogInfo.LogClickedResultID), FieldDataType.Text, size: 200, allowEmpty: true);

        return form;
    }

    /// <summary>The form definition of <see cref="Popularity.XpSearchPopularityIndexInfo"/> (RK-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo PopularityIndexForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Popularity.XpSearchPopularityIndexInfo.PopularityIndexID));

        Add(form, nameof(Popularity.XpSearchPopularityIndexInfo.PopularityIndexGuid), FieldDataType.Guid);
        Add(form, nameof(Popularity.XpSearchPopularityIndexInfo.PopularityIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(Popularity.XpSearchPopularityIndexInfo.PopularityIndexEnabled), FieldDataType.Boolean);
        Add(form, nameof(Popularity.XpSearchPopularityIndexInfo.PopularityIndexComputed), FieldDataType.DateTime, allowEmpty: true);

        return form;
    }

    /// <summary>The form definition of <see cref="Fuzzy.XpSearchFuzzyIndexInfo"/> (FZ-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo FuzzyIndexForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Fuzzy.XpSearchFuzzyIndexInfo.FuzzyIndexID));

        Add(form, nameof(Fuzzy.XpSearchFuzzyIndexInfo.FuzzyIndexGuid), FieldDataType.Guid);
        Add(form, nameof(Fuzzy.XpSearchFuzzyIndexInfo.FuzzyIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(Fuzzy.XpSearchFuzzyIndexInfo.FuzzyIndexEnabled), FieldDataType.Boolean);

        return form;
    }

    /// <summary>The form definition of <see cref="Popularity.XpSearchPopularityScoreInfo"/> (RK-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo PopularityScoreForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Popularity.XpSearchPopularityScoreInfo.ScoreID));

        Add(form, nameof(Popularity.XpSearchPopularityScoreInfo.ScoreGuid), FieldDataType.Guid);
        Add(form, nameof(Popularity.XpSearchPopularityScoreInfo.ScoreIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(Popularity.XpSearchPopularityScoreInfo.ScoreDocumentID), FieldDataType.Text, size: 200);
        Add(form, nameof(Popularity.XpSearchPopularityScoreInfo.ScoreValue), FieldDataType.Double);
        Add(form, nameof(Popularity.XpSearchPopularityScoreInfo.ScoreComputed), FieldDataType.DateTime);

        return form;
    }

    /// <summary>The form definition of <see cref="Popularity.XpSearchPopularitySuggestionInfo"/> (RK-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo PopularitySuggestionForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionID));

        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionGuid), FieldDataType.Guid);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionQuery), FieldDataType.Text, size: 450);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionDocumentID), FieldDataType.Text, size: 200);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionClicks), FieldDataType.Integer);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionSharePercent), FieldDataType.Integer);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionComputed), FieldDataType.DateTime);
        Add(form, nameof(Popularity.XpSearchPopularitySuggestionInfo.SuggestionState), FieldDataType.Integer);

        return form;
    }

    /// <summary>The form definition of <see cref="Popularity.XpSearchSynonymSuggestionInfo"/> (SY-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo SynonymSuggestionForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionID));

        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionGuid), FieldDataType.Guid);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionFailed), FieldDataType.Text, size: 450);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionSucceeded), FieldDataType.Text, size: 450);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionOccurrences), FieldDataType.Integer);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionLastSeen), FieldDataType.DateTime);
        Add(form, nameof(Popularity.XpSearchSynonymSuggestionInfo.SynonymSuggestionState), FieldDataType.Integer);

        return form;
    }

    /// <summary>The form definition of <see cref="Options.XpSearchSettingsInfo"/> (AR-1).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo SettingsForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(Options.XpSearchSettingsInfo.SettingsID));

        Add(form, nameof(Options.XpSearchSettingsInfo.SettingsGuid), FieldDataType.Guid);

        foreach (string column in SettingsColumns)
        {
            Add(form, column, FieldDataType.Integer);
        }

        return form;
    }

    /// <summary>Creates the module and its class if they are not there yet, and adds missing fields if they are.</summary>
    public void Install()
    {
        var resource = resources.Get()
            .WhereEquals(nameof(ResourceInfo.ResourceName), ResourceName)
            .FirstOrDefault() ?? new ResourceInfo();

        resource.ResourceDisplayName = "Kentico Integration - Xperience Search analytics";
        resource.ResourceName = ResourceName;
        resource.ResourceDescription = "Storage for the anonymous aggregate search query log and the popularity signal computed from it.";
        resource.ResourceIsInDevelopment = false;

        if (resource.HasChanged)
        {
            resources.Set(resource);
        }

        InstallClass(resource, XpSearchQueryLogInfo.TYPEINFO, "XpSearch query log", QueryLogForm());
        InstallClass(resource, Popularity.XpSearchPopularityIndexInfo.TYPEINFO, "XpSearch popularity index", PopularityIndexForm());
        InstallClass(resource, Popularity.XpSearchPopularityScoreInfo.TYPEINFO, "XpSearch popularity score", PopularityScoreForm());
        InstallClass(resource, Popularity.XpSearchPopularitySuggestionInfo.TYPEINFO, "XpSearch popularity suggestion", PopularitySuggestionForm());
        InstallClass(resource, Popularity.XpSearchSynonymSuggestionInfo.TYPEINFO, "XpSearch synonym suggestion", SynonymSuggestionForm());
        InstallClass(resource, Fuzzy.XpSearchFuzzyIndexInfo.TYPEINFO, "XpSearch typo tolerance", FuzzyIndexForm());
        InstallClass(resource, Options.XpSearchSettingsInfo.TYPEINFO, "XpSearch settings", SettingsForm());

        SeedSettings();
    }

    /// <summary>
    /// Writes the single settings row the first time, from the effective code-configured options. An
    /// existing row is left alone - the administration owns it from then on - except for columns an
    /// upgrade added, which read as 0 and are given the configured value.
    /// </summary>
    private void SeedSettings()
    {
        var stored = settings.Get().TopN(1).FirstOrDefault();
        var seed = Options.StoredSearchSettings.NewRow(Options.SearchSettingsValues.From(options.Value));

        if (stored is null)
        {
            settings.Set(seed);

            return;
        }

        bool repaired = false;

        // A column added by a later upgrade exists but has no value. Zero is a legal value only for the
        // cache lifetime, so every other column reading 0 is a column nobody ever wrote.
        foreach (string column in SettingsColumns.Where(column =>
            !string.Equals(column, nameof(Options.XpSearchSettingsInfo.SettingsCacheTtlSeconds), StringComparison.Ordinal)))
        {
            if (ValidationHelper.GetInteger(stored.GetValue(column), 0) <= 0)
            {
                stored.SetValue(column, seed.GetValue(column));
                repaired = true;
            }
        }

        if (repaired)
        {
            settings.Set(stored);
        }
    }

    private static void Add(FormInfo form, string name, string dataType, int size = 0, bool allowEmpty = false)
    {
        form.AddFormItem(
            new FormFieldInfo
            {
                Name = name,
                AllowEmpty = allowEmpty,
                Visible = true,
                Enabled = true,
                Precision = 0,
                Size = size,
                DataType = dataType,
            },
            -1);
    }

    private static void InstallClass(ResourceInfo resource, ObjectTypeInfo typeInfo, string displayName, FormInfo form)
    {
        var dataClass = DataClassInfoProvider.GetDataClassInfo(typeInfo.ObjectClassName) ?? DataClassInfo.New(typeInfo.ObjectType);

        dataClass.ClassName = typeInfo.ObjectClassName;
        dataClass.ClassTableName = typeInfo.ObjectClassName.Replace(".", "_", StringComparison.Ordinal);
        dataClass.ClassDisplayName = displayName;
        dataClass.ClassType = ClassType.OTHER;
        dataClass.ClassResourceID = resource.ResourceID;

        // An existing class keeps whatever an upgrade already added; only missing fields are merged in.
        if (dataClass.ClassID > 0)
        {
            var existing = new FormInfo(dataClass.ClassFormDefinition);
            existing.CombineWithForm(form, new CombineWithFormSettings());
            dataClass.ClassFormDefinition = existing.GetXmlDefinition();
        }
        else
        {
            dataClass.ClassFormDefinition = form.GetXmlDefinition();
        }

        if (dataClass.HasChanged)
        {
            DataClassInfoProvider.SetDataClassInfo(dataClass);
        }
    }
}

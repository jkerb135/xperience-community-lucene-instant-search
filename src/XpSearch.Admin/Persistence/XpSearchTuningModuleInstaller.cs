using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Modules;

namespace XpSearch.Admin.Persistence;

/// <summary>
/// Creates the module and the four data classes the Search tuning application stores its data in,
/// the first time the application starts. Mirrors
/// <c>XpSearch.Ingestion.Persistence.XpSearchIngestionModuleInstaller</c>, which is in turn modelled
/// on <c>LuceneModuleInstaller</c>
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/object-types).
/// </summary>
/// <remarks>
/// The resource is separate from the ingestion one on purpose: two installers that write the same
/// <see cref="ResourceInfo"/> would race each other on the first start. The name is prefixed with
/// <c>CMS.</c> so the class code generator skips these classes - they are the library's own storage,
/// not project data a developer generates models for.
/// </remarks>
public sealed class XpSearchTuningModuleInstaller
{
    /// <summary>Code name of the module the classes belong to.</summary>
    public const string ResourceName = "CMS.Integration.XpSearchTuning";

    private readonly IInfoProvider<ResourceInfo> resources;
    private readonly IInfoProvider<XpSearchRuleInfo> rules;

    /// <summary>Initializes a new instance of the <see cref="XpSearchTuningModuleInstaller"/> class.</summary>
    /// <param name="resources">Provider of <see cref="ResourceInfo"/>, used to create the module.</param>
    /// <param name="rules">Provider of rule objects, used by the one-time storage migration.</param>
    public XpSearchTuningModuleInstaller(IInfoProvider<ResourceInfo> resources, IInfoProvider<XpSearchRuleInfo> rules)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(rules);

        this.resources = resources;
        this.rules = rules;
    }

    /// <summary>The form definition of <see cref="XpSearchRuleInfo"/> (spec §8.2).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo RuleForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchRuleInfo.RuleID));

        Add(form, nameof(XpSearchRuleInfo.RuleGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchRuleInfo.RuleIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchRuleInfo.RuleName), FieldDataType.Text, size: 200);
        Add(form, nameof(XpSearchRuleInfo.RuleEnabled), FieldDataType.Boolean);

        // The if/then of the rule, one JSON column each (ADR-0022 addendum). They replace the flat
        // columns of ADR-0014, which RuleStorageMigration converts and then removes; a new install
        // never has those columns at all. Both allow empty: a row is written before the builder can
        // have filled them, and an empty "if" is exactly the marker the migration keys on.
        Add(form, nameof(XpSearchRuleInfo.RuleConditions), FieldDataType.LongText, allowEmpty: true);
        Add(form, nameof(XpSearchRuleInfo.RuleConsequences), FieldDataType.LongText, allowEmpty: true);
        Add(form, nameof(XpSearchRuleInfo.RuleMigrated), FieldDataType.Boolean);
        Add(form, nameof(XpSearchRuleInfo.RuleValidFrom), FieldDataType.DateTime, allowEmpty: true);
        Add(form, nameof(XpSearchRuleInfo.RuleValidTo), FieldDataType.DateTime, allowEmpty: true);
        Add(form, nameof(XpSearchRuleInfo.RulePriority), FieldDataType.Integer);

        return form;
    }

    /// <summary>The form definition of <see cref="XpSearchSynonymInfo"/> (spec §8.2).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo SynonymForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchSynonymInfo.SynonymID));

        Add(form, nameof(XpSearchSynonymInfo.SynonymGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchSynonymInfo.SynonymIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchSynonymInfo.SynonymType), FieldDataType.Integer);
        Add(form, nameof(XpSearchSynonymInfo.SynonymInput), FieldDataType.LongText);
        Add(form, nameof(XpSearchSynonymInfo.SynonymOutput), FieldDataType.LongText, allowEmpty: true);
        Add(form, nameof(XpSearchSynonymInfo.SynonymEnabled), FieldDataType.Boolean);

        return form;
    }

    /// <summary>The form definition of <see cref="XpSearchFieldWeightInfo"/> (spec §8.2).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo FieldWeightForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchFieldWeightInfo.WeightID));

        Add(form, nameof(XpSearchFieldWeightInfo.WeightGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchFieldWeightInfo.WeightIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchFieldWeightInfo.WeightFieldName), FieldDataType.Text, size: 200);
        Add(form, nameof(XpSearchFieldWeightInfo.WeightValue), FieldDataType.Decimal, size: 18, precision: 4);

        return form;
    }

    /// <summary>The form definition of <see cref="XpSearchStopwordListInfo"/>.</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo StopwordListForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchStopwordListInfo.StopwordListID));

        Add(form, nameof(XpSearchStopwordListInfo.StopwordListGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchStopwordListInfo.StopwordListIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchStopwordListInfo.StopwordListWords), FieldDataType.LongText, allowEmpty: true);

        return form;
    }

    /// <summary>Creates the module and its classes if they are not there yet, and adds missing fields if they are.</summary>
    public void Install()
    {
        var resource = resources.Get()
            .WhereEquals(nameof(ResourceInfo.ResourceName), ResourceName)
            .FirstOrDefault() ?? new ResourceInfo();

        resource.ResourceDisplayName = "Kentico Integration - Xperience Search tuning";
        resource.ResourceName = ResourceName;
        resource.ResourceDescription = "Storage for search relevance rules, synonyms, stopwords and field weights.";
        resource.ResourceIsInDevelopment = false;

        if (resource.HasChanged)
        {
            resources.Set(resource);
        }

        InstallClass(resource, XpSearchRuleInfo.TYPEINFO, "XpSearch rule", RuleForm());
        InstallClass(resource, XpSearchSynonymInfo.TYPEINFO, "XpSearch synonym", SynonymForm());
        InstallClass(resource, XpSearchFieldWeightInfo.TYPEINFO, "XpSearch field weight", FieldWeightForm());
        InstallClass(resource, XpSearchStopwordListInfo.TYPEINFO, "XpSearch stopword list", StopwordListForm());

        // The rule class now has both shapes' columns on an upgraded installation, so this is the
        // first moment the conversion can run - and the last before a page reads a rule.
        RuleStorageMigration.Run(rules);
    }

    private static void Add(FormInfo form, string name, string dataType, int size = 0, int precision = 0, bool allowEmpty = false)
    {
        form.AddFormItem(
            new FormFieldInfo
            {
                Name = name,
                AllowEmpty = allowEmpty,
                Visible = true,
                Enabled = true,
                Precision = precision,
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

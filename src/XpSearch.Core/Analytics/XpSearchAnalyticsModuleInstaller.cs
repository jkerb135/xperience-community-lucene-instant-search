using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Modules;

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

    private readonly IInfoProvider<ResourceInfo> resources;

    /// <summary>Initializes a new instance of the <see cref="XpSearchAnalyticsModuleInstaller"/> class.</summary>
    /// <param name="resources">Provider of <see cref="ResourceInfo"/>, used to create the module.</param>
    public XpSearchAnalyticsModuleInstaller(IInfoProvider<ResourceInfo> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        this.resources = resources;
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
        resource.ResourceDescription = "Storage for the anonymous aggregate search query log.";
        resource.ResourceIsInDevelopment = false;

        if (resource.HasChanged)
        {
            resources.Set(resource);
        }

        InstallClass(resource, XpSearchQueryLogInfo.TYPEINFO, "XpSearch query log", QueryLogForm());
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

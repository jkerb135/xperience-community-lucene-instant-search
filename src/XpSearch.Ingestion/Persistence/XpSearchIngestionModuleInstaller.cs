using CMS.DataEngine;
using CMS.FormEngine;
using CMS.Modules;

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// Creates the module and the three data classes the ingestion API stores its data in, the first time
/// the application starts. Modelled on <c>LuceneModuleInstaller</c>, which is how the Lucene
/// integration installs its own object types
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/object-types).
/// </summary>
/// <remarks>
/// The resource name is prefixed with <c>CMS.</c> so the class code generator skips these classes:
/// they are the library's own storage, not project data a developer generates models for.
/// </remarks>
public sealed class XpSearchIngestionModuleInstaller
{
    /// <summary>Code name of the module the classes belong to.</summary>
    public const string ResourceName = "CMS.Integration.XpSearchIngestion";

    private readonly IInfoProvider<ResourceInfo> resources;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionModuleInstaller"/> class.</summary>
    /// <param name="resources">Provider of <see cref="ResourceInfo"/>, used to create the module.</param>
    public XpSearchIngestionModuleInstaller(IInfoProvider<ResourceInfo> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        this.resources = resources;
    }

    /// <summary>The form definition of <see cref="XpSearchExternalDocumentInfo"/>.</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo ExternalDocumentForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchExternalDocumentInfo.DocumentID));

        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentSource), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentKey), FieldDataType.Text, size: 450);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentBody), FieldDataType.LongText);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentHash), FieldDataType.Text, size: 64);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentCreatedAt), FieldDataType.DateTime);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentUpdatedAt), FieldDataType.DateTime);
        Add(form, nameof(XpSearchExternalDocumentInfo.DocumentStatus), FieldDataType.Integer);

        return form;
    }

    /// <summary>The form definition of <see cref="XpSearchApiKeyInfo"/> (spec §10.4).</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo ApiKeyForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchApiKeyInfo.KeyID));

        Add(form, nameof(XpSearchApiKeyInfo.KeyGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchApiKeyInfo.KeyName), FieldDataType.Text, size: 200);
        Add(form, nameof(XpSearchApiKeyInfo.KeyHash), FieldDataType.Text, size: 200);
        Add(form, nameof(XpSearchApiKeyInfo.KeyPrefix), FieldDataType.Text, size: 16);
        Add(form, nameof(XpSearchApiKeyInfo.KeyScopes), FieldDataType.LongText);
        Add(form, nameof(XpSearchApiKeyInfo.KeyEnabled), FieldDataType.Boolean);
        Add(form, nameof(XpSearchApiKeyInfo.KeyExpiresAt), FieldDataType.DateTime, allowEmpty: true);
        Add(form, nameof(XpSearchApiKeyInfo.KeyLastUsedAt), FieldDataType.DateTime, allowEmpty: true);

        return form;
    }

    /// <summary>The form definition of <see cref="XpSearchIngestionLogInfo"/>.</summary>
    /// <returns>The fields, on top of the primary key the basic definition creates.</returns>
    public static FormInfo IngestionLogForm()
    {
        var form = FormHelper.GetBasicFormDefinition(nameof(XpSearchIngestionLogInfo.LogID));

        Add(form, nameof(XpSearchIngestionLogInfo.LogGuid), FieldDataType.Guid);
        Add(form, nameof(XpSearchIngestionLogInfo.LogKeyPrefix), FieldDataType.Text, size: 16);
        Add(form, nameof(XpSearchIngestionLogInfo.LogIndexName), FieldDataType.Text, size: 100);
        Add(form, nameof(XpSearchIngestionLogInfo.LogOperation), FieldDataType.Text, size: 20);
        Add(form, nameof(XpSearchIngestionLogInfo.LogDocumentCount), FieldDataType.Integer);
        Add(form, nameof(XpSearchIngestionLogInfo.LogSucceeded), FieldDataType.Boolean);
        Add(form, nameof(XpSearchIngestionLogInfo.LogMessage), FieldDataType.LongText);
        Add(form, nameof(XpSearchIngestionLogInfo.LogCreatedAt), FieldDataType.DateTime);

        return form;
    }

    /// <summary>Creates the module and its classes if they are not there yet, and adds missing fields if they are.</summary>
    public void Install()
    {
        var resource = resources.Get()
            .WhereEquals(nameof(ResourceInfo.ResourceName), ResourceName)
            .FirstOrDefault() ?? new ResourceInfo();

        resource.ResourceDisplayName = "Kentico Integration - Xperience Search ingestion";
        resource.ResourceName = ResourceName;
        resource.ResourceDescription = "Storage for externally pushed search documents, ingestion API keys and the ingestion log.";
        resource.ResourceIsInDevelopment = false;

        if (resource.HasChanged)
        {
            resources.Set(resource);
        }

        InstallClass(resource, XpSearchExternalDocumentInfo.TYPEINFO, "XpSearch external document", ExternalDocumentForm());
        InstallClass(resource, XpSearchApiKeyInfo.TYPEINFO, "XpSearch API key", ApiKeyForm());
        InstallClass(resource, XpSearchIngestionLogInfo.TYPEINFO, "XpSearch ingestion log", IngestionLogForm());
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

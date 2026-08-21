using System.Data;
using System.Globalization;

using CMS.DataEngine;
using CMS.Helpers;

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// One externally pushed document, persisted so the database - not Lucene - is the source of truth
/// (ADR-0005). Defined as a custom module class, which is how Xperience integrations store structured
/// data (https://docs.kentico.com/documentation/developers-and-admins/customization/object-types).
/// </summary>
public class XpSearchExternalDocumentInfo : AbstractInfo<XpSearchExternalDocumentInfo, IInfoProvider<XpSearchExternalDocumentInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.externaldocument";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.ExternalDocument";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchExternalDocumentInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchExternalDocumentInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(DocumentID),
            null,
            nameof(DocumentGuid),
            null,
            null,
            null,
            null,
            null)
        {
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchExternalDocumentInfo"/> class.</summary>
    public XpSearchExternalDocumentInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchExternalDocumentInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchExternalDocumentInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int DocumentID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(DocumentID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid DocumentGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(DocumentGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the document belongs to.</summary>
    [DatabaseField]
    public virtual string DocumentIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(DocumentIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentIndexName), value);
    }

    /// <summary>Gets or sets the document's provenance, written to the reserved <c>_source</c> attribute.</summary>
    [DatabaseField]
    public virtual string DocumentSource
    {
        get => ValidationHelper.GetString(GetValue(nameof(DocumentSource)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentSource), value);
    }

    /// <summary>Gets or sets the caller-owned identifier, unique within the index.</summary>
    [DatabaseField]
    public virtual string DocumentKey
    {
        get => ValidationHelper.GetString(GetValue(nameof(DocumentKey)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentKey), value);
    }

    /// <summary>Gets or sets the document body as pushed: a JSON object of attributes.</summary>
    [DatabaseField]
    public virtual string DocumentBody
    {
        get => ValidationHelper.GetString(GetValue(nameof(DocumentBody)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentBody), value);
    }

    /// <summary>Gets or sets the hash of <see cref="DocumentBody"/>.</summary>
    [DatabaseField]
    public virtual string DocumentHash
    {
        get => ValidationHelper.GetString(GetValue(nameof(DocumentHash)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentHash), value);
    }

    /// <summary>Gets or sets when the document was first pushed.</summary>
    [DatabaseField]
    public virtual DateTime DocumentCreatedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(DocumentCreatedAt)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentCreatedAt), value);
    }

    /// <summary>Gets or sets when the document was last pushed.</summary>
    [DatabaseField]
    public virtual DateTime DocumentUpdatedAt
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(DocumentUpdatedAt)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentUpdatedAt), value);
    }

    /// <summary>Gets or sets whether the row has reached Lucene: 0 pending, 1 indexed.</summary>
    [DatabaseField]
    public virtual int DocumentStatus
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(DocumentStatus)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(DocumentStatus), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

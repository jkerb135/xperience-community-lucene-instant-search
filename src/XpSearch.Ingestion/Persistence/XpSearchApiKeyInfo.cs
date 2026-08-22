using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Ingestion.Persistence;

// Registers the object type with the system. Without it Xperience never adds
// IInfoProvider<XpSearchApiKeyInfo> to the container and the application fails DI validation
// at startup: https://docs.kentico.com/documentation/developers-and-admins/customization/object-types/object-type-configuration
[assembly: RegisterObjectType(typeof(XpSearchApiKeyInfo), XpSearchApiKeyInfo.OBJECT_TYPE)]

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// An ingestion API key (spec §10.4). The plaintext key is never stored: only its PBKDF2 hash and the
/// prefix that identifies it in the admin UI and in the ingestion log.
/// </summary>
public class XpSearchApiKeyInfo : AbstractInfo<XpSearchApiKeyInfo, IInfoProvider<XpSearchApiKeyInfo>>, IInfoWithId, IInfoWithName
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.apikey";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.ApiKey";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchApiKeyInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchApiKeyInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(KeyID),
            null,
            nameof(KeyGuid),
            null,
            nameof(KeyName),
            null,
            null,
            null)
        {
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchApiKeyInfo"/> class.</summary>
    public XpSearchApiKeyInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchApiKeyInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchApiKeyInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int KeyID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(KeyID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid KeyGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(KeyGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyGuid), value);
    }

    /// <summary>Gets or sets the display name of the key.</summary>
    [DatabaseField]
    public virtual string KeyName
    {
        get => ValidationHelper.GetString(GetValue(nameof(KeyName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyName), value);
    }

    /// <summary>Gets or sets the PBKDF2 hash of the key. Never the key itself.</summary>
    [DatabaseField]
    public virtual string KeyHash
    {
        get => ValidationHelper.GetString(GetValue(nameof(KeyHash)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyHash), value);
    }

    /// <summary>Gets or sets the first characters of the key, for identification.</summary>
    [DatabaseField]
    public virtual string KeyPrefix
    {
        get => ValidationHelper.GetString(GetValue(nameof(KeyPrefix)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyPrefix), value);
    }

    /// <summary>Gets or sets the scopes as JSON: <c>{"indexes":["products"],"ops":["write","delete"]}</c>.</summary>
    [DatabaseField]
    public virtual string KeyScopes
    {
        get => ValidationHelper.GetString(GetValue(nameof(KeyScopes)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(KeyScopes), value);
    }

    /// <summary>Gets or sets whether the key is usable.</summary>
    [DatabaseField]
    public virtual bool KeyEnabled
    {
        get => ValidationHelper.GetBoolean(GetValue(nameof(KeyEnabled)), false);
        set => SetValue(nameof(KeyEnabled), value);
    }

    /// <summary>Gets or sets when the key stops working. Null never expires.</summary>
    [DatabaseField]
    public virtual DateTime? KeyExpiresAt
    {
        get => GetValue(nameof(KeyExpiresAt)) as DateTime?;
        set => SetValue(nameof(KeyExpiresAt), value);
    }

    /// <summary>Gets or sets when the key was last used. Written back at most once per throttle window.</summary>
    [DatabaseField]
    public virtual DateTime? KeyLastUsedAt
    {
        get => GetValue(nameof(KeyLastUsedAt)) as DateTime?;
        set => SetValue(nameof(KeyLastUsedAt), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

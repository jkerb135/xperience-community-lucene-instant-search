using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Popularity;

[assembly: RegisterObjectType(typeof(XpSearchPopularitySuggestionInfo), XpSearchPopularitySuggestionInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Popularity;

/// <summary>What a human did with a suggested boost rule (RK-1).</summary>
public enum PopularitySuggestionState
{
    /// <summary>Nobody has answered it yet; it shows in the Suggestions listing.</summary>
    Pending = 0,

    /// <summary>Turned into an ordinary rule. It never resurfaces for that query and document.</summary>
    Approved = 1,

    /// <summary>Turned down. It never resurfaces for that query and document.</summary>
    Dismissed = 2
}

/// <summary>
/// One suggested boost rule (RK-1): a frequent query whose clicks one document clearly wins. Nothing
/// here changes a search - only a human approving it does, and that writes an ordinary rule.
/// </summary>
public class XpSearchPopularitySuggestionInfo : AbstractInfo<XpSearchPopularitySuggestionInfo, IInfoProvider<XpSearchPopularitySuggestionInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.popularitysuggestion";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.PopularitySuggestion";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchPopularitySuggestionInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchPopularitySuggestionInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(SuggestionID),
            null,
            nameof(SuggestionGuid),
            null,
            null,
            null,
            null,
            null)
        {
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchPopularitySuggestionInfo"/> class.</summary>
    public XpSearchPopularitySuggestionInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchPopularitySuggestionInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchPopularitySuggestionInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int SuggestionID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SuggestionID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid SuggestionGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SuggestionGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the suggestion belongs to.</summary>
    [DatabaseField]
    public virtual string SuggestionIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(SuggestionIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionIndexName), value);
    }

    /// <summary>Gets or sets the query the suggestion is about.</summary>
    [DatabaseField]
    public virtual string SuggestionQuery
    {
        get => ValidationHelper.GetString(GetValue(nameof(SuggestionQuery)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionQuery), value);
    }

    /// <summary>Gets or sets the result id of the document that wins the query's clicks.</summary>
    [DatabaseField]
    public virtual string SuggestionDocumentID
    {
        get => ValidationHelper.GetString(GetValue(nameof(SuggestionDocumentID)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionDocumentID), value);
    }

    /// <summary>Gets or sets how many clicks the document took on the query.</summary>
    [DatabaseField]
    public virtual int SuggestionClicks
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SuggestionClicks)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionClicks), value);
    }

    /// <summary>Gets or sets the document's share of the query's damped click mass, in whole percent.</summary>
    [DatabaseField]
    public virtual int SuggestionSharePercent
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SuggestionSharePercent)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionSharePercent), value);
    }

    /// <summary>Gets or sets when the suggestion was computed, in UTC.</summary>
    [DatabaseField]
    public virtual DateTime SuggestionComputed
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SuggestionComputed)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionComputed), value);
    }

    /// <summary>Gets or sets what a human did with the suggestion. See <see cref="PopularitySuggestionState"/>.</summary>
    [DatabaseField]
    public virtual int SuggestionState
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SuggestionState)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SuggestionState), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

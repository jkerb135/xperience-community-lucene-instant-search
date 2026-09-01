using System.Data;
using System.Globalization;

using CMS;
using CMS.DataEngine;
using CMS.Helpers;

using XpSearch.Core.Popularity;

[assembly: RegisterObjectType(typeof(XpSearchSynonymSuggestionInfo), XpSearchSynonymSuggestionInfo.OBJECT_TYPE)]

namespace XpSearch.Core.Popularity;

/// <summary>
/// One mined synonym candidate (SY-1): a query that got no click, and the query visitors searched
/// right afterwards that did. Nothing here changes a search - only a human approving it does, and
/// that writes an ordinary synonym group.
/// </summary>
public class XpSearchSynonymSuggestionInfo : AbstractInfo<XpSearchSynonymSuggestionInfo, IInfoProvider<XpSearchSynonymSuggestionInfo>>, IInfoWithId
{
    /// <summary>Object type identifier.</summary>
    public const string OBJECT_TYPE = "xpsearch.synonymsuggestion";

    /// <summary>Code name of the module class, and the name of its database table with the dot replaced.</summary>
    public const string CLASS_NAME = "XpSearch.SynonymSuggestion";

    /// <summary>Type information.</summary>
    public static readonly ObjectTypeInfo TYPEINFO;

    static XpSearchSynonymSuggestionInfo() =>
        TYPEINFO = new ObjectTypeInfo(
            typeof(IInfoProvider<XpSearchSynonymSuggestionInfo>),
            OBJECT_TYPE,
            CLASS_NAME,
            nameof(SynonymSuggestionID),
            null,
            nameof(SynonymSuggestionGuid),
            null,
            null,
            null,
            null,
            null)
        {
            TouchCacheDependencies = true,
        };

    /// <summary>Creates an empty instance of the <see cref="XpSearchSynonymSuggestionInfo"/> class.</summary>
    public XpSearchSynonymSuggestionInfo()
        : base(TYPEINFO)
    {
    }

    /// <summary>Creates an instance of the <see cref="XpSearchSynonymSuggestionInfo"/> class from a data row.</summary>
    /// <param name="dr">Data row with the object data.</param>
    public XpSearchSynonymSuggestionInfo(DataRow dr)
        : base(TYPEINFO, dr)
    {
    }

    /// <summary>Gets or sets the primary key.</summary>
    [DatabaseField]
    public virtual int SynonymSuggestionID
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SynonymSuggestionID)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionID), value);
    }

    /// <summary>Gets or sets the object GUID.</summary>
    [DatabaseField]
    public virtual Guid SynonymSuggestionGuid
    {
        get => ValidationHelper.GetGuid(GetValue(nameof(SynonymSuggestionGuid)), default, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionGuid), value);
    }

    /// <summary>Gets or sets the code name of the index the suggestion belongs to.</summary>
    [DatabaseField]
    public virtual string SynonymSuggestionIndexName
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymSuggestionIndexName)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionIndexName), value);
    }

    /// <summary>Gets or sets the query that got no click.</summary>
    [DatabaseField]
    public virtual string SynonymSuggestionFailed
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymSuggestionFailed)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionFailed), value);
    }

    /// <summary>Gets or sets the following query that did get a click.</summary>
    [DatabaseField]
    public virtual string SynonymSuggestionSucceeded
    {
        get => ValidationHelper.GetString(GetValue(nameof(SynonymSuggestionSucceeded)), string.Empty, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionSucceeded), value);
    }

    /// <summary>Gets or sets how often the pair happened in the window.</summary>
    [DatabaseField]
    public virtual int SynonymSuggestionOccurrences
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SynonymSuggestionOccurrences)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionOccurrences), value);
    }

    /// <summary>Gets or sets when the pair last happened, in UTC.</summary>
    [DatabaseField]
    public virtual DateTime SynonymSuggestionLastSeen
    {
        get => ValidationHelper.GetDateTime(GetValue(nameof(SynonymSuggestionLastSeen)), DateTimeHelper.ZERO_TIME, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionLastSeen), value);
    }

    /// <summary>Gets or sets what a human did with the suggestion. See <see cref="PopularitySuggestionState"/>.</summary>
    [DatabaseField]
    public virtual int SynonymSuggestionState
    {
        get => ValidationHelper.GetInteger(GetValue(nameof(SynonymSuggestionState)), 0, CultureInfo.InvariantCulture);
        set => SetValue(nameof(SynonymSuggestionState), value);
    }

    /// <inheritdoc />
    protected override void DeleteObject() => Provider.Delete(this);

    /// <inheritdoc />
    protected override void SetObject() => Provider.Set(this);
}

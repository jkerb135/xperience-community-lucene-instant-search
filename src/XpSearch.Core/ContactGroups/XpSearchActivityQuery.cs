using CMS.Activities;
using CMS.DataEngine;

namespace XpSearch.Core.ContactGroups;

/// <summary>
/// Builds the activity query the search contact group conditions match on.
/// </summary>
internal static class XpSearchActivityQuery
{
    /// <summary>
    /// Returns the ids of contacts that performed one of the rule's activities with a value
    /// containing <paramref name="text"/>. An empty text matches any search.
    /// </summary>
    /// <remarks>
    /// The comparison is a SQL <c>LIKE</c>, so it is as case sensitive as the database collation -
    /// case insensitive on a default Xperience database.
    /// </remarks>
    public static ObjectQuery<ActivityInfo> ContactIds(
        IInfoProvider<ActivityInfo> provider,
        XpSearchContactGroupRule rule,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(rule);

        var query = provider.Get()
            .Column(nameof(ActivityInfo.ActivityContactID))
            .WhereIn(nameof(ActivityInfo.ActivityType), (ICollection<string>)rule.ActivityTypes.ToList());

        var searched = text?.Trim();

        return string.IsNullOrEmpty(searched)
            ? query
            : query.WhereContains(nameof(ActivityInfo.ActivityValue), searched);
    }
}

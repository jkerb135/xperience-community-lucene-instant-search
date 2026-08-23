namespace XpSearch.Core.Analytics;

/// <summary>
/// The four custom activity types the library logs (spec §9.1). The code names are what
/// <c>ICustomActivityLogger.Log</c> is called with and what the built-in <em>Contact has performed
/// custom activity</em> contact group condition matches on. All four carry the searched text as the
/// activity value.
/// </summary>
public static class XpSearchActivityTypes
{
    /// <summary>Code name of the activity logged when a search returned at least one result.</summary>
    public const string Query = "xpsearch_query";

    /// <summary>Code name of the activity logged when a search returned nothing.</summary>
    public const string NoResults = "xpsearch_noresults";

    /// <summary>Code name of the activity logged when a visitor clicks a result.</summary>
    public const string Click = "xpsearch_click";

    /// <summary>Code name of the activity logged when the developer signals a goal after a search.</summary>
    public const string Conversion = "xpsearch_conversion";

    /// <summary>Gets the four types with the display name and description they are created with.</summary>
    public static IReadOnlyList<XpSearchActivityType> All { get; } =
    [
        new(Query, "Search", "The visitor ran a search that returned at least one result. The activity value is the searched text."),
        new(NoResults, "Search without results", "The visitor ran a search that returned no results. The activity value is the searched text."),
        new(Click, "Search result click", "The visitor opened a search result. The activity value is the searched text; the result id is in the comment and its position in the item detail ID."),
        new(Conversion, "Search conversion", "A goal was reached after a search. The activity value is the searched text; the result id is in the comment.")
    ];
}

/// <summary>One activity type this library registers.</summary>
/// <param name="CodeName">Code name used when logging, for example <c>xpsearch_query</c>.</param>
/// <param name="DisplayName">Name shown in the administration.</param>
/// <param name="Description">What the type tracks, shown to marketers in the administration.</param>
public sealed record XpSearchActivityType(string CodeName, string DisplayName, string Description);

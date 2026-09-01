using CMS.Activities;
using CMS.ContactManagement;
using CMS.DataEngine;
using CMS.Helpers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using XpSearch.Core.Analytics;

namespace XpSearch.Core.Personalization;

/// <summary>One search the current contact ran, as the "searched for" condition needs it (PS-1).</summary>
/// <param name="Query">The searched text, the activity's value.</param>
/// <param name="When">When the activity was logged, in the database's own clock.</param>
public sealed record RecentSearch(string Query, DateTime When);

/// <summary>Supplies the searches the current visitor's contact ran, once per request.</summary>
public interface IRecentSearchProvider
{
    /// <summary>Gets the current contact's most recent searches, newest first.</summary>
    /// <returns>The searches, or an empty list when there is no contact, no consent or no activity.</returns>
    IReadOnlyList<RecentSearch> GetRecentSearches();
}

/// <summary>
/// Decides whether the visitor searched for a term recently. Pure, so the whole condition can be
/// tested without a database.
/// </summary>
public static class SearchedFor
{
    /// <summary>Default length of the window, in days.</summary>
    public const int DefaultDays = 30;

    /// <summary>Whether one of the searches contains the term inside the window.</summary>
    /// <param name="searches">The contact's recent searches.</param>
    /// <param name="term">The term to look for; a blank term matches nothing, because a condition that matches everyone is a misconfiguration.</param>
    /// <param name="days">Length of the window in days; anything below one is treated as one.</param>
    /// <param name="now">The current time, on the same clock as <see cref="RecentSearch.When"/>.</param>
    /// <returns><see langword="true"/> when the visitor searched for the term inside the window.</returns>
    public static bool Matches(IEnumerable<RecentSearch> searches, string? term, int days, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(searches);

        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        string wanted = term.Trim();
        var since = now.AddDays(-Math.Max(days, 1));

        return searches.Any(search =>
            search.When >= since
            && search.Query.Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The production provider: the current contact's search activities, read once per HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// The gates are <c>ContactGroupResolver</c>'s, for the same reasons: nothing is read below the
/// <em>Visitor</em> cookie level, and the contact is recognized with
/// <see cref="ICurrentContactProvider.GetExistingContact"/> so that rendering a page never creates
/// an anonymous contact. No contact, no consent or no activity all answer "no searches", which makes
/// every condition false - the original variant, which is also what a crawler sees.
/// </para>
/// <para>
/// A page can carry many personalized widgets, so the answer is memoized on
/// <see cref="HttpContext.Items"/>: one activity read per request, however many conditions evaluate.
/// The day window of each condition is applied in memory over the newest
/// <see cref="MaxSearches"/> searches (see KNOWN-LIMITATIONS).
/// </para>
/// </remarks>
public sealed class RecentSearchProvider : IRecentSearchProvider
{
    /// <summary>How many of the contact's newest searches are read.</summary>
    public const int MaxSearches = 100;

    private static readonly object ItemsKey = new();

    private readonly ICurrentContactProvider contacts;
    private readonly ICurrentCookieLevelProvider cookieLevelProvider;
    private readonly IInfoProvider<ActivityInfo> activities;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ILogger<RecentSearchProvider> logger;

    /// <summary>Initializes a new instance of the <see cref="RecentSearchProvider"/> class.</summary>
    /// <param name="contacts">Recognizes the visitor's contact.</param>
    /// <param name="cookieLevelProvider">Supplies the current visitor's cookie level.</param>
    /// <param name="activities">Provider of logged activities.</param>
    /// <param name="httpContextAccessor">Gives access to the request the answer is memoized on.</param>
    /// <param name="logger">Logger.</param>
    public RecentSearchProvider(
        ICurrentContactProvider contacts,
        ICurrentCookieLevelProvider cookieLevelProvider,
        IInfoProvider<ActivityInfo> activities,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RecentSearchProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(cookieLevelProvider);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        this.contacts = contacts;
        this.cookieLevelProvider = cookieLevelProvider;
        this.activities = activities;
        this.httpContextAccessor = httpContextAccessor;
        this.logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<RecentSearch> GetRecentSearches()
    {
        var request = httpContextAccessor.HttpContext;

        if (request is not null
            && request.Items.TryGetValue(ItemsKey, out object? memoized)
            && memoized is IReadOnlyList<RecentSearch> already)
        {
            return already;
        }

        var resolved = Resolve();

        if (request is not null)
        {
            request.Items[ItemsKey] = resolved;
        }

        return resolved;
    }

    private IReadOnlyList<RecentSearch> Resolve()
    {
        try
        {
            if (cookieLevelProvider.GetCurrentCookieLevel() < Kentico.Web.Mvc.CookieLevel.Visitor.Level)
            {
                logger.LogDebug("Not reading search activities: the visitor has not consented to tracking.");

                return [];
            }

            if (contacts.GetExistingContact() is not { ContactID: > 0 } contact)
            {
                return [];
            }

            return activities.Get()
                .Columns(nameof(ActivityInfo.ActivityValue), nameof(ActivityInfo.ActivityCreated))
                .WhereEquals(nameof(ActivityInfo.ActivityContactID), contact.ContactID)
                .WhereIn(
                    nameof(ActivityInfo.ActivityType),
                    new[] { XpSearchActivityTypes.Query, XpSearchActivityTypes.NoResults })
                .OrderByDescending(nameof(ActivityInfo.ActivityCreated))
                .TopN(MaxSearches)
                .GetEnumerableTypedResult()
                .Select(activity => new RecentSearch(activity.ActivityValue ?? string.Empty, activity.ActivityCreated))
                .ToList();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The visitor's search activities could not be read; the search condition is false.");

            return [];
        }
    }
}

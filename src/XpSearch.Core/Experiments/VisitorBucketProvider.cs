using CMS.Helpers;

using Kentico.Web.Mvc;

using Microsoft.AspNetCore.Http;

namespace XpSearch.Core.Experiments;

/// <summary>
/// Supplies the opaque id the visitor is bucketed by, from the <c>xpsearch_bucket</c> cookie
/// (XP-1), assigning one when the response can still carry it.
/// </summary>
public interface IVisitorBucketProvider
{
    /// <summary>Gets the visitor's bucket id.</summary>
    /// <returns>The id, or <see langword="null"/> when none exists and none can be assigned.</returns>
    string? GetBucketId();
}

/// <summary>
/// The read-or-assign logic of the bucket cookie, shared by the search experiment resolver (XP-1)
/// and the "search A/B bucket" personalization condition (PS-1).
/// </summary>
/// <remarks>
/// A cookie can only be assigned while the response has not started (appending one afterwards
/// throws, and the pipeline does run while a server-rendered widget is streaming - DX-2) and only to
/// a visitor at the <c>Essential</c> level or above. Neither caller may bucket on a throwaway id
/// instead: that would flip the same visitor between variants request by request.
/// </remarks>
public sealed class VisitorBucketProvider : IVisitorBucketProvider
{
    private readonly ICookieAccessor cookies;
    private readonly ICurrentCookieLevelProvider cookieLevelProvider;
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>Initializes a new instance of the <see cref="VisitorBucketProvider"/> class.</summary>
    /// <param name="cookies">Reads and writes the bucket cookie.</param>
    /// <param name="cookieLevelProvider">Supplies the visitor's current cookie level.</param>
    /// <param name="httpContextAccessor">Gives access to the response the cookie would be written to.</param>
    public VisitorBucketProvider(
        ICookieAccessor cookies,
        ICurrentCookieLevelProvider cookieLevelProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        ArgumentNullException.ThrowIfNull(cookieLevelProvider);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        this.cookies = cookies;
        this.cookieLevelProvider = cookieLevelProvider;
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <summary>Tells whether a bucket cookie can still be appended to this response.</summary>
    /// <param name="context">The current HTTP context, if any.</param>
    /// <returns><see langword="true"/> when appending a Set-Cookie header is still allowed.</returns>
    public static bool CanAssignCookie(HttpContext? context) => context is not null && !context.Response.HasStarted;

    /// <inheritdoc />
    public string? GetBucketId()
    {
        if (cookies.Get(ExperimentBucketing.CookieName) is { Length: > 0 } existing)
        {
            return existing;
        }

        if (!CanAssignCookie(httpContextAccessor.HttpContext)
            || cookieLevelProvider.GetCurrentCookieLevel() < Kentico.Web.Mvc.CookieLevel.Essential.Level)
        {
            return null;
        }

        string assigned = ExperimentBucketing.NewBucketId();

        cookies.Set(
            ExperimentBucketing.CookieName,
            assigned,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.Add(ExperimentBucketing.CookieLifetime),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });

        return assigned;
    }
}

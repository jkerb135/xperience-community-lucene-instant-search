using CMS.Activities;
using CMS.Helpers;

using Microsoft.Extensions.Logging;

namespace XpSearch.Core.Analytics;

/// <summary>
/// The production <see cref="ISearchActivityLogger"/>: hands the activity to Xperience's
/// <see cref="ICustomActivityLogger"/> when the visitor may be tracked, and does nothing at all when
/// they may not.
/// </summary>
/// <remarks>
/// <para>
/// Consent gate: custom activities on website channels are only logged for visitors who consented to
/// tracking and whose cookie level is <em>Visitor</em> or higher
/// (https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/set-up-activities,
/// https://docs.kentico.com/documentation/developers-and-admins/data-protection/consent-development).
/// The level is read with <see cref="ICurrentCookieLevelProvider"/> and compared to
/// <c>CookieLevel.Visitor.Level</c>, the same check the documented <c>CurrentContactCanBeTracked</c>
/// sample makes
/// (https://docs.kentico.com/guides/development/activities-and-marketing/enable-activity-tracking).
/// </para>
/// <para>
/// Analytics is best-effort: every failure - including a missing request context - is swallowed and
/// logged at Debug, because a search must never fail because of its own instrumentation.
/// </para>
/// </remarks>
public sealed class SearchActivityLogger : ISearchActivityLogger
{
    private readonly ICustomActivityLogger activityLogger;
    private readonly ICurrentCookieLevelProvider cookieLevelProvider;
    private readonly ILogger<SearchActivityLogger> logger;

    /// <summary>Initializes a new instance of the <see cref="SearchActivityLogger"/> class.</summary>
    /// <param name="activityLogger">Xperience's custom activity logger.</param>
    /// <param name="cookieLevelProvider">Supplies the current visitor's cookie level.</param>
    /// <param name="logger">Logger.</param>
    public SearchActivityLogger(
        ICustomActivityLogger activityLogger,
        ICurrentCookieLevelProvider cookieLevelProvider,
        ILogger<SearchActivityLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(activityLogger);
        ArgumentNullException.ThrowIfNull(cookieLevelProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.activityLogger = activityLogger;
        this.cookieLevelProvider = cookieLevelProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public void LogSearch(string query, int total) =>
        Log(total > 0 ? XpSearchActivityTypes.Query : XpSearchActivityTypes.NoResults, query);

    /// <inheritdoc />
    public void LogClick(string query, string resultId, int position) =>
        Log(XpSearchActivityTypes.Click, $"{query} | {resultId} | {position}");

    /// <inheritdoc />
    public void LogConversion(string query, string resultId) =>
        Log(XpSearchActivityTypes.Conversion, $"{query} | {resultId}");

    private void Log(string codeName, string value)
    {
        try
        {
            if (!CanBeTracked())
            {
                logger.LogDebug("Skipping the {Activity} activity: the visitor has not consented to tracking.", codeName);

                return;
            }

            var type = XpSearchActivityTypes.All.First(activity => string.Equals(activity.CodeName, codeName, StringComparison.Ordinal));

            activityLogger.Log(codeName, new CustomActivityData { ActivityTitle = type.DisplayName, ActivityValue = value });
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "The {Activity} search activity could not be logged.", codeName);
        }
    }

    private bool CanBeTracked() => cookieLevelProvider.GetCurrentCookieLevel() >= Kentico.Web.Mvc.CookieLevel.Visitor.Level;
}

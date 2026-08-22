using System.Globalization;

using CMS.Membership;

using Kentico.Xperience.Admin.Base;

using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;
using XpSearch.Admin.UIPages.Analytics;
using XpSearch.Core.Analytics;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "analytics",
    uiPageType: typeof(AnalyticsDashboardPage),
    name: "Analytics",
    templateName: "@yourco/xperience-search-admin/AnalyticsDashboard",
    order: 600)]

namespace XpSearch.Admin.UIPages.Analytics;

/// <summary>Initial state of the analytics dashboard client template.</summary>
public class AnalyticsDashboardClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code names of every registered index.</summary>
    public IEnumerable<string> IndexNames { get; set; } = [];

    /// <summary>Gets or sets the index selected when the page opens, or an empty string for every index.</summary>
    public string SelectedIndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets today's date in UTC, as <c>yyyy-MM-dd</c>, so the presets agree with the server.</summary>
    public string Today { get; set; } = string.Empty;
}

/// <summary>
/// The analytics dashboard (spec §9.3): the six reports of the aggregate query log for one index and
/// date range, with a "Create rule" action on every zero-result row.
/// </summary>
/// <remarks>
/// A custom client template, because no built-in template can express six reports, a date range and a
/// chart on one screen
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages).
/// </remarks>
[UIEvaluatePermission(SystemPermissions.VIEW)]
public class AnalyticsDashboardPage : Page<AnalyticsDashboardClientProperties>
{
    /// <summary>Most rows a report will return.</summary>
    public const int MaxLimit = 100;

    private readonly ILuceneIndexManager indexManager;
    private readonly ISearchAnalyticsService analytics;
    private readonly IPageLinkGenerator pageLinkGenerator;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="AnalyticsDashboardPage"/> class.</summary>
    /// <param name="indexManager">The integration's index registry, used to fill the index selector.</param>
    /// <param name="analytics">Produces the reports.</param>
    /// <param name="pageLinkGenerator">Generates the URL the "Create rule" action navigates to.</param>
    /// <param name="time">Clock, so the default range is the server's idea of today.</param>
    public AnalyticsDashboardPage(
        ILuceneIndexManager indexManager,
        ISearchAnalyticsService analytics,
        IPageLinkGenerator pageLinkGenerator,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(indexManager);
        ArgumentNullException.ThrowIfNull(analytics);
        ArgumentNullException.ThrowIfNull(pageLinkGenerator);
        ArgumentNullException.ThrowIfNull(time);

        this.indexManager = indexManager;
        this.analytics = analytics;
        this.pageLinkGenerator = pageLinkGenerator;
        this.time = time;
    }

    /// <inheritdoc />
    public override Task<AnalyticsDashboardClientProperties> ConfigureTemplateProperties(AnalyticsDashboardClientProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        properties.IndexNames = [.. indexManager.GetAllIndices()
            .Select(index => index.IndexName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
        properties.SelectedIndexName = string.Empty;
        properties.Today = time.GetUtcNow().UtcDateTime.ToString(AnalyticsReportDto.DateFormat, CultureInfo.InvariantCulture);

        return Task.FromResult(properties);
    }

    /// <summary>Loads every report for one index and date range.</summary>
    /// <param name="request">What to report on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reports.</returns>
    [PageCommand(Permission = SystemPermissions.VIEW)]
    public async Task<ICommandResponse<AnalyticsReportDto>> Load(AnalyticsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryParseDay(request.From, out var from) || !TryParseDay(request.To, out var to))
        {
            return ResponseFrom(AnalyticsReportDto.Failed("Enter both dates as yyyy-MM-dd."));
        }

        if (to < from)
        {
            return ResponseFrom(AnalyticsReportDto.Failed("The end of the range is before its start."));
        }

        var report = await analytics.GetReportAsync(
            new SearchAnalyticsQuery(
                request.IndexName ?? string.Empty,
                from.ToDateTime(TimeOnly.MinValue),
                to.ToDateTime(TimeOnly.MaxValue),
                Math.Clamp(request.Limit, 1, MaxLimit)),
            cancellationToken)
            .ConfigureAwait(false);

        return ResponseFrom(AnalyticsReportDto.From(report));
    }

    /// <summary>Opens the rule create page with the zero-result query pre-filled as the pattern.</summary>
    /// <param name="request">The zero-result row the action was invoked on.</param>
    /// <returns>A navigation to the create page.</returns>
    [PageCommand(Permission = SystemPermissions.CREATE)]
    public Task<INavigateResponse> CreateRule(CreateRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string path = pageLinkGenerator.GetPath<ZeroResultRuleCreatePage>(new PageParameterValues
        {
            { typeof(ZeroResultRuleCreatePage), RuleSeed.Encode(request.IndexName ?? string.Empty, request.Query ?? string.Empty) }
        });

        return Task.FromResult(NavigateTo(path));
    }

    private static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, AnalyticsReportDto.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}

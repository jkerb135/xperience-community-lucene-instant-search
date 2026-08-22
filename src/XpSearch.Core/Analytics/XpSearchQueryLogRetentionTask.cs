using CMS.Scheduler;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using XpSearch.Core.Analytics;
using XpSearch.Core.Options;

[assembly: RegisterScheduledTask(XpSearchQueryLogRetentionTask.Identifier, typeof(XpSearchQueryLogRetentionTask))]

namespace XpSearch.Core.Analytics;

/// <summary>
/// Deletes query log rows older than <c>XpSearchOptions.Analytics.RetentionDays</c> (spec §9.2).
/// </summary>
/// <remarks>
/// Registration only makes the task selectable; the task <em>configuration</em> - its schedule and
/// enabled state - has to be created once in the administration's <em>Scheduled tasks</em>
/// application, which is the only documented way to configure one
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/scheduled-tasks).
/// See <c>docs/guides/analytics.md</c> for the steps.
/// </remarks>
public sealed class XpSearchQueryLogRetentionTask : IScheduledTask
{
    /// <summary>The identifier the task is registered and selected under.</summary>
    public const string Identifier = "XpSearch.QueryLogRetention";

    private readonly IQueryLogStore store;
    private readonly XpSearchOptions options;
    private readonly ILogger<XpSearchQueryLogRetentionTask> logger;

    /// <summary>Initializes a new instance of the <see cref="XpSearchQueryLogRetentionTask"/> class.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="options">The configured search options.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchQueryLogRetentionTask(
        IQueryLogStore store,
        IOptions<XpSearchOptions> options,
        ILogger<XpSearchQueryLogRetentionTask> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledTaskExecutionResult> Execute(ScheduledTaskConfigurationInfo task, CancellationToken cancellationToken)
    {
        var analytics = options.Analytics;
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, analytics.RetentionDays));
        int batchSize = Math.Max(1, analytics.RetentionBatchSize);
        int deleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            int batch = await store.DeleteOlderThanAsync(cutoff, batchSize, cancellationToken).ConfigureAwait(false);
            deleted += batch;

            if (batch < batchSize)
            {
                break;
            }
        }

        logger.LogInformation("Deleted {Deleted} search query log rows older than {Cutoff:u}.", deleted, cutoff);

        return new ScheduledTaskExecutionResult($"Deleted {deleted} query log rows older than {cutoff:u}.");
    }
}

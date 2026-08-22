using CMS.Base;
using CMS.Core;

namespace XpSearch.Core.Analytics;

/// <summary>One piece of query log work: either a new row, or a click to record on an existing one.</summary>
/// <param name="Entry">The row to append, or <see langword="null"/> for a click.</param>
/// <param name="ClickedQueryId">Correlation id of the search that was clicked, or <see langword="null"/>.</param>
/// <param name="ClickedPosition">One-based position of the clicked result.</param>
public sealed record QueryLogWorkItem(QueryLogEntry? Entry, string? ClickedQueryId, int ClickedPosition)
{
    /// <summary>Work that appends one logged search.</summary>
    /// <param name="entry">The row to append.</param>
    /// <returns>The work item.</returns>
    public static QueryLogWorkItem Append(QueryLogEntry entry) => new(entry, null, 0);

    /// <summary>Work that records a click on an already logged search.</summary>
    /// <param name="queryId">Correlation id of the search.</param>
    /// <param name="position">One-based position of the clicked result.</param>
    /// <returns>The work item.</returns>
    public static QueryLogWorkItem Click(string queryId, int position) => new(null, queryId, position);
}

/// <summary>Hands query log work to the background worker so a search never waits for the database.</summary>
public interface IQueryLogQueue
{
    /// <summary>Queues one piece of work.</summary>
    /// <param name="item">The work to do.</param>
    void Enqueue(QueryLogWorkItem item);
}

/// <summary>
/// Writes the query log on Xperience's background worker thread, the pattern the platform prescribes
/// for integration batches and the one the Lucene integration itself uses
/// (https://docs.kentico.com/guides/development/customizations-and-integrations/tools-for-integrations,
/// "ThreadQueueWorker"); spec §9.2 asks for it by name so logging never blocks a search response.
/// </summary>
/// <remarks>
/// The worker is instantiated by <c>ThreadWorker&lt;T&gt;.Current</c>, not by the container, so the
/// store is resolved through <c>CMS.Core.Service</c> - the same thing <c>LuceneQueueWorker</c> does.
/// Queued items are lost if the process dies before they are written; an analytics aggregate is not
/// worth persisting a queue for (ADR-0015).
/// </remarks>
public class XpSearchQueryLogQueueWorker : ThreadQueueWorker<QueryLogWorkItem, XpSearchQueryLogQueueWorker>
{
    /// <summary>Initializes a new instance of the <see cref="XpSearchQueryLogQueueWorker"/> class.</summary>
    /// <remarks>Called by <c>ThreadWorker&lt;T&gt;.Current</c>; do not call it directly.</remarks>
    public XpSearchQueryLogQueueWorker()
    {
    }

    /// <inheritdoc />
    protected override int DefaultInterval => 10_000;

    /// <summary>Adds an item to the worker's queue.</summary>
    /// <param name="item">The work to do.</param>
    public static void EnqueueItem(QueryLogWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Current.Enqueue(item, ensureThread: true);
    }

    /// <summary>Gets how many items are waiting.</summary>
    /// <returns>The queue length.</returns>
    public static int Waiting() => Current.ItemsInQueue;

    /// <summary>Applies one work item to a store. This is the whole of what the worker thread does.</summary>
    /// <param name="store">Where the query log lives.</param>
    /// <param name="item">The work to do.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the work is done.</returns>
    public static async Task ProcessAsync(IQueryLogStore store, QueryLogWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(item);

        if (item.Entry is not null)
        {
            await store.AppendAsync(item.Entry, cancellationToken).ConfigureAwait(false);

            return;
        }

        if (!string.IsNullOrWhiteSpace(item.ClickedQueryId))
        {
            await store.SetClickedPositionAsync(item.ClickedQueryId, item.ClickedPosition, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override void Finish() => RunProcess();

    /// <inheritdoc />
    protected override void ProcessItem(QueryLogWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var store = Service.Resolve<IQueryLogStore>()
            ?? throw new InvalidOperationException("IQueryLogStore is not registered. Call services.AddXpSearch().");

        // The worker thread is synchronous by contract; the store is async, so this is where the two meet.
        ProcessAsync(store, item, CancellationToken.None).GetAwaiter().GetResult();
    }
}

/// <summary>The production <see cref="IQueryLogQueue"/>: hands work to <see cref="XpSearchQueryLogQueueWorker"/>.</summary>
public sealed class ThreadQueueQueryLogQueue : IQueryLogQueue
{
    /// <inheritdoc />
    public void Enqueue(QueryLogWorkItem item) => XpSearchQueryLogQueueWorker.EnqueueItem(item);
}

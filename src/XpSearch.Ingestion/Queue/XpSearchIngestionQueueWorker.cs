using CMS.Base;
using CMS.Core;

using XpSearch.Ingestion.Abstractions;

namespace XpSearch.Ingestion.Queue;

/// <summary>
/// Processes queued index writes on Xperience's background worker thread, the pattern the platform
/// prescribes for integration batches and the one the Lucene integration itself uses
/// (https://docs.kentico.com/guides/development/customizations-and-integrations/tools-for-integrations,
/// "ThreadQueueWorker").
/// </summary>
/// <remarks>
/// The worker is instantiated by <c>ThreadWorker&lt;T&gt;.Current</c>, not by the container, so its
/// dependencies are resolved through <c>CMS.Core.Service</c> - the same thing <c>LuceneQueueWorker</c>
/// does. Nothing is lost when the process dies mid-queue: the rows are already committed and
/// <c>XpSearchIngestionModule</c> re-queues them on the next start (ADR-0005).
/// </remarks>
public class XpSearchIngestionQueueWorker : ThreadQueueWorker<IngestionWorkItem, XpSearchIngestionQueueWorker>
{
    /// <summary>
    /// The <c>operation</c> ingestion log entries carry when background index work fails. The index
    /// status reads them, which is what makes its health agree across a web farm (WF-1).
    /// </summary>
    public const string FailureOperation = "index";

    private static int failures;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionQueueWorker"/> class.</summary>
    /// <remarks>Called by <c>ThreadWorker&lt;T&gt;.Current</c>; do not call it directly.</remarks>
    public XpSearchIngestionQueueWorker()
    {
    }

    /// <inheritdoc />
    protected override int DefaultInterval => 5_000;

    /// <summary>Adds an item to the worker's queue.</summary>
    /// <param name="item">The work to do.</param>
    public static void EnqueueItem(IngestionWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Current.Enqueue(item, ensureThread: true);
    }

    /// <summary>Gets how many work items have failed in a row without one succeeding since.</summary>
    /// <returns>The consecutive failure count.</returns>
    public static int Failures() => Volatile.Read(ref failures);

    /// <summary>Builds the ingestion log row that stands for one failed work item.</summary>
    /// <param name="item">The work that failed.</param>
    /// <param name="failure">Why it failed.</param>
    /// <param name="at">When it failed, in UTC.</param>
    /// <returns>The row.</returns>
    public static IngestionLogEntry FailureEntry(IngestionWorkItem item, Exception failure, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(failure);

        return new IngestionLogEntry(
            "background",
            item.IndexName,
            FailureOperation,
            item.Ids.Count,
            false,
            $"{item.Operation} failed: {failure.Message}",
            at);
    }

    /// <inheritdoc />
    protected override void Finish() => RunProcess();

    /// <inheritdoc />
    protected override void ProcessItem(IngestionWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var processor = Service.Resolve<IIngestionWorkProcessor>()
            ?? throw new InvalidOperationException("IIngestionWorkProcessor is not registered. Call services.AddXpSearchIngestion().");

        try
        {
            // The worker thread is synchronous by contract; every path below it is async, so this is
            // the one place the two meet.
            processor.ProcessAsync(item, CancellationToken.None).GetAwaiter().GetResult();

            Interlocked.Exchange(ref failures, 0);
        }
        catch (Exception exception)
        {
            // Counted and recorded, then rethrown so ThreadQueueWorker logs it as it always has. The
            // row stays Pending and is re-queued on the next application start, so the failure is
            // recoverable - but until the window passes the index status reports it as degraded.
            Interlocked.Increment(ref failures);
            Record(item, exception);

            throw;
        }
    }

    /// <summary>
    /// Writes the failure to the ingestion log, which is where the index status reads health from: a
    /// static counter would only be true on the instance that happened to run the work (WF-1).
    /// </summary>
    private static void Record(IngestionWorkItem item, Exception failure)
    {
        try
        {
            Service.ResolveOptional<IIngestionLog>()?
                .WriteAsync(FailureEntry(item, failure, DateTime.UtcNow), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            // The audit row is best effort; the failure it describes is about to be rethrown anyway.
        }
    }
}

/// <summary>
/// The production <see cref="IIngestionQueue"/>: hands work to <see cref="XpSearchIngestionQueueWorker"/>.
/// </summary>
public sealed class ThreadQueueIngestionQueue : IIngestionQueue
{
    /// <inheritdoc />
    public int FailedCount => XpSearchIngestionQueueWorker.Failures();

    /// <inheritdoc />
    public void Enqueue(IngestionWorkItem item) => XpSearchIngestionQueueWorker.EnqueueItem(item);
}

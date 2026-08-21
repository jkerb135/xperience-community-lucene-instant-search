using CMS;
using CMS.Base;
using CMS.Core;
using CMS.DataEngine;

using Microsoft.Extensions.DependencyInjection;

using XpSearch.Ingestion.Abstractions;
using XpSearch.Ingestion.Persistence;
using XpSearch.Ingestion.Queue;

[assembly: RegisterModule(typeof(XpSearchIngestionModule))]

namespace XpSearch.Ingestion.Persistence;

/// <summary>
/// Installs the ingestion object types and starts the ingestion queue when the application starts
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/run-code-on-application-startup).
/// </summary>
public class XpSearchIngestionModule : Module
{
    private IServiceProvider? services;

    /// <summary>Initializes a new instance of the <see cref="XpSearchIngestionModule"/> class.</summary>
    public XpSearchIngestionModule()
        : base("XpSearchIngestion")
    {
    }

    /// <summary>
    /// Re-queues every document that was persisted but never reached Lucene. This is what makes a
    /// restart mid-queue a delay rather than data loss (ADR-0005); it is public so a host - or a test
    /// simulating a restart - can run it on demand.
    /// </summary>
    /// <param name="store">Where documents are persisted.</param>
    /// <param name="queue">The ingestion queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many documents were re-queued.</returns>
    public static async Task<int> RequeuePendingAsync(IExternalDocumentStore store, IIngestionQueue queue, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(queue);

        var pending = await store.ListPendingAsync(cancellationToken).ConfigureAwait(false);

        foreach (var group in pending.GroupBy(record => record.IndexName, StringComparer.OrdinalIgnoreCase))
        {
            queue.Enqueue(IngestionWorkItem.New(group.Key, IngestionOperation.Upsert, group.Select(record => record.Id).ToList()));
        }

        return pending.Count;
    }

    /// <inheritdoc />
    protected override void OnInit(ModuleInitParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        base.OnInit(parameters);

        services = parameters.Services;

        // The classes have to exist before anything reads them, and the database is only reachable
        // once the application is initialized - the same sequencing LuceneSearchModule uses.
        ApplicationEvents.Initialized.Execute += Initialize;

        RequestEvents.RunEndRequestTasks.Execute += (_, _) => ThreadWorker<XpSearchIngestionQueueWorker>.Current.EnsureRunningThread();
    }

    private void Initialize(object? sender, EventArgs e)
    {
        var provider = services!;

        provider.GetRequiredService<XpSearchIngestionModuleInstaller>().Install();

        var store = provider.GetService<IExternalDocumentStore>();
        var queue = provider.GetService<IIngestionQueue>();

        if (store is not null && queue is not null)
        {
            RequeuePendingAsync(store, queue, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}

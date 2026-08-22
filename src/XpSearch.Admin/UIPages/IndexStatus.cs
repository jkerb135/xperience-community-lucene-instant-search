using System.Globalization;
using System.Text;

using Kentico.Xperience.Admin.Base;
using Kentico.Xperience.Admin.Base.FormAnnotations;
using Kentico.Xperience.Admin.Base.Forms;
using Kentico.Xperience.Admin.Base.Forms.Internal;

using Kentico.Xperience.Lucene.Core;
using Kentico.Xperience.Lucene.Core.Indexing;

using XpSearch.Admin.UIPages;
using XpSearch.Ingestion.Abstractions;

[assembly: UIPage(
    parentType: typeof(SearchTuningApplication),
    slug: "index-status",
    uiPageType: typeof(IndexStatusPage),
    name: "Index status",
    templateName: TemplateNames.EDIT,
    order: 600)]

namespace XpSearch.Admin.UIPages;

/// <summary>The index status page's form: what every index holds, and which one to rebuild.</summary>
public class IndexStatusModel
{
    /// <summary>Gets or sets the read-only report of every index.</summary>
    [TextAreaComponent(Label = "Indexes", Order = 1)]
    public string Report { get; set; } = string.Empty;

    /// <summary>Gets or sets the index the rebuild command applies to.</summary>
    [RequiredValidationRule]
    [DropDownComponent(Label = "Index to rebuild", Order = 2)]
    public string IndexName { get; set; } = string.Empty;
}

/// <summary>
/// Document counts by source, the last external write and a rebuild trigger (spec §10.8).
/// </summary>
/// <remarks>
/// The built-in listing template can only list a registered object type, and index status is derived
/// from <c>ILuceneIndexManager</c> and the ingestion store rather than stored in a table. Rather than
/// write a React listing, the page reports every index in a read-only text area and uses the edit
/// template's submit action as the rebuild trigger. See ADR-0014.
/// </remarks>
public class IndexStatusPage : TuningEditPage<IndexStatusModel>
{
    private readonly IXpSearchIndexer indexer;
    private readonly ILuceneClient client;
    private readonly IIngestionLog log;

    /// <summary>Initializes a new instance of the <see cref="IndexStatusPage"/> class.</summary>
    /// <param name="formItemCollectionProvider">Builds the form components.</param>
    /// <param name="formDataBinder">Binds the submitted values.</param>
    /// <param name="indexManager">The integration's index registry.</param>
    /// <param name="pageLinkGenerator">Generates admin URLs.</param>
    /// <param name="indexer">Reads document counts and health per index.</param>
    /// <param name="client">The integration's index writer, decorated so a rebuild replays external documents.</param>
    /// <param name="log">Records the rebuild in the ingestion log.</param>
    public IndexStatusPage(
        IFormItemCollectionProvider formItemCollectionProvider,
        IFormDataBinder formDataBinder,
        ILuceneIndexManager indexManager,
        IPageLinkGenerator pageLinkGenerator,
        IXpSearchIndexer indexer,
        ILuceneClient client,
        IIngestionLog log)
        : base(formItemCollectionProvider, formDataBinder, indexManager, pageLinkGenerator)
    {
        this.indexer = indexer;
        this.client = client;
        this.log = log;
    }

    /// <inheritdoc />
    public override Task ConfigurePage()
    {
        PageConfiguration.Headline = "Index status";
        PageConfiguration.SubmitConfiguration.Label = "Rebuild index";
        PageConfiguration.SubmitConfiguration.ConfirmationConfiguration = new ConfirmationConfiguration
        {
            Title = "Rebuild the index?",
            Detail = "The index is emptied and written again. Search results are incomplete until it finishes.",
            Button = "Rebuild"
        };

        return base.ConfigurePage();
    }

    /// <inheritdoc />
    protected override IndexStatusModel CreateModel() =>
        new()
        {
            Report = BuildReport(),
            IndexName = IndexNames().FirstOrDefault() ?? string.Empty
        };

    /// <inheritdoc />
    protected override async Task<ICollection<IFormItem>> GetFormItems()
    {
        var items = await base.GetFormItems();

        foreach (var report in items.OfType<TextAreaComponent>())
        {
            report.Properties.EditMode = FormEditMode.ReadOnly;
        }

        return items;
    }

    /// <inheritdoc />
    protected override async Task<string> PersistAsync(IndexStatusModel submitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submitted);

        // The registered ILuceneClient is the ingestion package's decorator, so externally pushed
        // documents are replayed after the integration wipes the index (spec §10.2).
        await client.Rebuild(submitted.IndexName, cancellationToken).ConfigureAwait(false);

        await log.WriteAsync(
            new IngestionLogEntry("admin-ui", submitted.IndexName, "rebuild", 0, true, "Rebuild triggered from the Search tuning application.", DateTime.UtcNow),
            cancellationToken)
            .ConfigureAwait(false);

        return $"Rebuild of '{submitted.IndexName}' triggered.";
    }

    private string BuildReport()
    {
        var report = new StringBuilder();

        foreach (string index in IndexNames())
        {
            var status = indexer.GetStatusAsync(index, CancellationToken.None).GetAwaiter().GetResult();

            report.Append(CultureInfo.InvariantCulture, $"{index}: {status.Documents?.Total ?? 0} documents, health {status.Health}");

            if (status.LastWrite is { } lastWrite)
            {
                report.Append(CultureInfo.InvariantCulture, $", last external write {lastWrite:u}");
            }

            report.AppendLine();

            foreach ((string source, long count) in status.Documents?.BySource ?? [])
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    {source}: {count}");
            }
        }

        return report.Length == 0 ? "No search indexes are registered." : report.ToString();
    }
}

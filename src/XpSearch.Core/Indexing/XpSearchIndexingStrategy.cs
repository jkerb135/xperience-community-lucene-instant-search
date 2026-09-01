using System.Globalization;

using CMS.ContentEngine;
using CMS.DataEngine;
using CMS.Helpers;
using CMS.Websites;

using Kentico.Xperience.Lucene.Core;
using Kentico.Xperience.Lucene.Core.Indexing;

using Lucene.Net.Documents;
using Lucene.Net.Facet;

using Microsoft.Extensions.Logging;

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// The indexing strategy customers derive from. It indexes every content type an index covers without
/// per-type code: fields are auto-detected from the content type definition, and every Xperience
/// taxonomy field becomes a facet dimension (spec §4.5).
/// </summary>
/// <remarks>
/// Register it like any other strategy - <c>.RegisterStrategy&lt;MyStrategy&gt;("MyStrategy")</c> on
/// <c>AddKenticoLucene</c> - and pick it in the admin Search application. Override
/// <see cref="ContributeAsync"/> to add fields of your own with the same encoding the base mapping
/// uses; use <see cref="XpSearchIndexingOptions"/> to exclude or re-flag a detected field, or to
/// flatten a linked reusable item into the document (spec §10.7).
/// </remarks>
public class XpSearchIndexingStrategy : DefaultLuceneIndexingStrategy
{
    private readonly IContentQueryExecutor executor;
    private readonly IWebPageUrlRetriever urlRetriever;
    private readonly ITaxonomyRetriever taxonomyRetriever;
    private readonly ITagAncestrySource tagAncestry;
    private readonly IContentTypeFieldSource fieldSource;
    private readonly ILuceneIndexAccessor accessor;
    private readonly IIndexSchemaProvider schemaProvider;
    private readonly XpSearchIndexingOptions options;
    private readonly ILogger<XpSearchIndexingStrategy> logger;

    // Every facetable field of every index this strategy serves is registered the first time the
    // configuration is asked for; mapping registers anything the schema did not know about.
    private readonly FacetsConfig facetsConfig = new();
    private readonly HashSet<string> registeredDimensions = new(StringComparer.Ordinal);

    private bool schemaDimensionsRegistered;

    // One warning per colliding (content type, field) pair, however many documents hit it.
    private readonly HashSet<string> reportedCollisions = new(StringComparer.OrdinalIgnoreCase);

    // Likewise for a contributed field the schema does not declare.
    private readonly HashSet<string> reportedUndeclared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="XpSearchIndexingStrategy"/> class.</summary>
    /// <param name="executor">Executes the untyped content query that loads the item's field values.</param>
    /// <param name="urlRetriever">Resolves the web page URL stored on the document.</param>
    /// <param name="taxonomyRetriever">Resolves tag identifiers to tag code names and titles.</param>
    /// <param name="tagAncestry">Resolves a tag's ancestors, which are indexed alongside it so counts roll up.</param>
    /// <param name="fieldSource">Detects the searchable fields of a content type.</param>
    /// <param name="accessor">The Lucene seam, used to find the indexes this strategy is configured for.</param>
    /// <param name="schemaProvider">Supplies those indexes' detected schema, which the facet dimensions come from.</param>
    /// <param name="options">Per-field overrides and linked-item flattening supplied by the developer.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchIndexingStrategy(
        IContentQueryExecutor executor,
        IWebPageUrlRetriever urlRetriever,
        ITaxonomyRetriever taxonomyRetriever,
        ITagAncestrySource tagAncestry,
        IContentTypeFieldSource fieldSource,
        ILuceneIndexAccessor accessor,
        IIndexSchemaProvider schemaProvider,
        XpSearchIndexingOptions options,
        ILogger<XpSearchIndexingStrategy> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(urlRetriever);
        ArgumentNullException.ThrowIfNull(taxonomyRetriever);
        ArgumentNullException.ThrowIfNull(tagAncestry);
        ArgumentNullException.ThrowIfNull(fieldSource);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.executor = executor;
        this.urlRetriever = urlRetriever;
        this.taxonomyRetriever = taxonomyRetriever;
        this.tagAncestry = tagAncestry;
        this.fieldSource = fieldSource;
        this.accessor = accessor;
        this.schemaProvider = schemaProvider;
        this.options = options;
        this.logger = logger;
    }

    /// <summary>Composes the stable identifier a result is addressed by.</summary>
    /// <param name="itemGuid">The item GUID the integration indexes the document under.</param>
    /// <param name="languageName">Code name of the language variant.</param>
    /// <returns>The result <c>id</c> value.</returns>
    /// <remarks>
    /// The Lucene integration deletes and replaces documents by the pair (item GUID, language), so the
    /// same pair is what uniquely addresses one indexed document.
    /// </remarks>
    public static string ComposeResultId(string itemGuid, string languageName) => $"{itemGuid}:{languageName}";

    /// <inheritdoc />
    /// <remarks>
    /// An item that cannot be mapped is logged and skipped rather than allowed to throw: the Lucene
    /// integration maps a whole batch in one task, so one bad document would otherwise cost the rebuild.
    /// </remarks>
    public override async Task<Document?> MapToLuceneDocumentOrNull(IIndexEventItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Same guard as the base strategy: secured content never enters a public index.
        if (item.IsSecured)
        {
            return null;
        }

        try
        {
            return await Map(item, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Skipping {ContentType} {ItemGuid} ({LanguageName}): the item could not be mapped to a document.",
                item.ContentTypeName,
                item.ItemGuid,
                item.LanguageName);

            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every facetable field of every index this strategy is registered for is a multi-valued
    /// dimension, derived from the detected schema the first time the configuration is asked for. The
    /// Lucene client asks for it before it builds a batch, so a fresh index - or one whose documents
    /// this process has not mapped yet - still gets a configuration that accepts a document with more
    /// than one tag in a dimension, which <c>FacetsConfig.Build</c> otherwise refuses for the whole
    /// batch. Dimensions discovered while mapping are added to the same instance as a fallback, so a
    /// field the schema cannot see (one added by <see cref="ContributeAsync"/>, say) still works.
    /// </remarks>
    public override FacetsConfig FacetsConfigFactory()
    {
        RegisterSchemaDimensions();

        return facetsConfig;
    }

    /// <summary>
    /// Adds anything the auto-detected mapping cannot know about to the document of one item. The base
    /// implementation does nothing.
    /// </summary>
    /// <param name="context">The item, its loaded data, its detected schema, and the helpers that write a value the way the base mapping does.</param>
    /// <param name="document">The document being built. The same one <paramref name="context"/> writes into.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the contribution has been made.</returns>
    /// <remarks>
    /// Runs after the item's own fields and after any <see cref="XpSearchIndexingOptions.FlattenLinkedItems"/>
    /// registration, so an override sees - and can replace - everything the base mapping produced.
    /// Throwing from here skips the document; it is logged, not propagated.
    /// </remarks>
    protected virtual Task ContributeAsync(IndexingContext context, Document document, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>Adds one value to a document with the encoding its field kind implies.</summary>
    internal async Task AddValue(Document document, SchemaField field, object? value, string languageName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(field);

        switch (field.Kind)
        {
            case SearchFieldKind.Taxonomy:
                await AddTags(document, field, ToTagReferences(value), languageName, cancellationToken).ConfigureAwait(false);
                break;

            case SearchFieldKind.Text:
                // Rich text arrives as HTML; the platform stripper is used rather than a regex so
                // entities and comments are handled: CMS.Helpers.HTMLHelper.StripTags.
                AddText(document, field.LuceneName, HTMLHelper.StripTags(value as string, true, " "), field.Sortable);
                break;

            case SearchFieldKind.Keyword:
                AddKeyword(document, field, value as string);
                break;

            case SearchFieldKind.Number when TryGetNumber(value, out double number):
                document.Add(new DoubleField(field.LuceneName, number, Field.Store.YES));
                document.Add(new DoubleDocValuesField(field.LuceneName, number));
                break;

            case SearchFieldKind.Date when value is DateTime date:
                long epochSeconds = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeSeconds();
                document.Add(new Int64Field(field.LuceneName, epochSeconds, Field.Store.YES));
                document.Add(new NumericDocValuesField(field.LuceneName, epochSeconds));
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Converts the raw value of a Taxonomy column to the tag references it stands for.
    /// </summary>
    /// <remarks>
    /// The untyped <see cref="IContentQueryDataContainer"/> hands back the column as it is stored - a
    /// JSON string - so <c>GetValue&lt;IEnumerable&lt;TagReference&gt;&gt;</c> throws
    /// <see cref="InvalidCastException"/>. The registered data type is what converts it: the field data
    /// type <c>taxonomy</c> is registered with the C# type <c>IEnumerable&lt;TagReference&gt;</c> and a
    /// conversion function, and the system "performs value conversions (and object mapping for complex
    /// types) when transferring data to and from the database" through it - see
    /// https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/data-types
    /// and https://docs.kentico.com/documentation/developers-and-admins/customization/field-editor/add-custom-data-types.
    /// <see cref="DataTypeManager.ConvertToSystemType"/> applies that registered conversion for any
    /// content type, which is what indexing needs: there are no generated classes for content types we
    /// have never seen.
    /// </remarks>
    private static IEnumerable<TagReference> ToTagReferences(object? value) => value switch
    {
        null => [],
        IEnumerable<TagReference> references => references,
        _ => DataTypeManager.ConvertToSystemType(
            TypeEnum.Field,
            FieldDataType.Taxonomy,
            value,
            CultureInfo.InvariantCulture,
            nullIfDefault: false) as IEnumerable<TagReference> ?? []
    };

    private static void AddText(Document document, string name, string? value, bool sortable)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        document.Add(new TextField(name, value, Field.Store.YES));

        if (sortable)
        {
            document.Add(new SortedDocValuesField(name + LuceneFieldNames.SortSuffix, new Lucene.Net.Util.BytesRef(value)));
        }
    }

    private static void AddKeyword(Document document, SchemaField field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        document.Add(new StringField(field.LuceneName, value, Field.Store.YES));

        if (field.Sortable)
        {
            document.Add(new SortedDocValuesField(LuceneFieldNames.SortFieldName(field), new Lucene.Net.Util.BytesRef(value)));
        }
    }

    private static bool TryGetNumber(object? raw, out double value)
    {
        value = 0;

        if (raw is null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private async Task<Document?> Map(IIndexEventItemModel item, CancellationToken cancellationToken)
    {
        var data = await LoadItem(item, cancellationToken).ConfigureAwait(false);

        if (data is null)
        {
            logger.LogDebug("No content found for {ContentType} {ItemGuid}; skipping.", item.ContentTypeName, item.ItemGuid);
            return null;
        }

        var document = new Document
        {
            new StringField(BaseDocumentProperties.ID, ComposeResultId(item.ItemGuid.ToString(), item.LanguageName), Field.Store.YES),
            new FacetField(BaseDocumentProperties.CONTENT_TYPE_NAME, item.ContentTypeName),
            new FacetField(BaseDocumentProperties.LANGUAGE_NAME, item.LanguageName),

            // Provenance marker (spec §10.2): pushed documents carry their own source, so a scoped
            // clear and the ingestion status counts can separate Xperience content from the rest.
            // Written once per document, before any flattened or contributed field is added. The
            // term is what the status counts and a scoped clear read; the facet field is what makes
            // "_source" countable and drillable, as the schema declares it to be.
            new StringField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource, Field.Store.YES),
            new FacetField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource)
        };

        AddText(document, IndexSchemaProvider.TitleField, item.Name, sortable: true);

        if (item is IndexEventWebPageItemModel webPage)
        {
            // Retrieve returns the app-relative "~/..." form, which is not valid on the wire.
            var url = await urlRetriever
                .Retrieve(webPage.WebPageItemTreePath, webPage.WebsiteChannelName, webPage.LanguageName, forPreview: false)
                .ConfigureAwait(false);

            document.Add(new StringField(BaseDocumentProperties.URL, WebUrl.ToRootRelative(url?.RelativePath), Field.Store.YES));
        }

        var fields = fieldSource.GetFields(item.ContentTypeName);
        var written = new HashSet<string>(fields.Select(field => field.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (!await TryAddFrom(document, field, data, item, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        if (!await Flatten(document, data, item, written, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // What the schema will report for this content type: its detected fields, whatever was
        // flattened onto it, the fields declared with AddField, and the base fields every document
        // carries. Anything else the hook writes is indexed but never projected.
        written.UnionWith(options.ContributedFieldsOf(item.ContentTypeName).Select(field => field.Name));

        foreach (var field in IndexSchemaProvider.BaseFields())
        {
            written.Add(field.Name);
            written.Add(field.LuceneName);
        }

        await ContributeAsync(new IndexingContext(this, document, item, data, fields, written), document, cancellationToken).ConfigureAwait(false);

        return document;
    }

    /// <summary>
    /// Warns once about a field written from the contribution hook that the index schema does not
    /// declare: it reaches the Lucene document but no result attribute, because attributes are
    /// projected from the schema.
    /// </summary>
    internal void ReportUndeclaredField(string contentTypeName, string fieldName, IReadOnlySet<string> declared)
    {
        if (declared.Contains(fieldName) || !reportedUndeclared.Add(contentTypeName + "." + fieldName))
        {
            return;
        }

        logger.LogWarning(
            "Field {FieldName} is written to the documents of {ContentType} by a contribution hook but is not part of the index schema, " +
            "so it is indexed and never returned. Declare it at startup: {Registration}",
            fieldName,
            contentTypeName,
            $"indexing.AddField(\"{contentTypeName}\", new SchemaField(\"{fieldName}\", ...))");
    }

    /// <summary>Adds one detected field, reporting the field by name when it is what failed.</summary>
    private async Task<bool> TryAddFrom(
        Document document,
        SchemaField field,
        IContentQueryDataContainer data,
        IIndexEventItemModel item,
        CancellationToken cancellationToken)
    {
        try
        {
            await AddValue(document, field, data.GetValue<object>(field.Name), item.LanguageName, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Skipping {ContentType} {ItemGuid} ({LanguageName}): field {FieldName} could not be indexed.",
                item.ContentTypeName,
                item.ItemGuid,
                item.LanguageName,
                field.Name);

            return false;
        }
    }

    /// <summary>
    /// Indexes the fields of the items linked from the configured fields onto the parent's document
    /// (spec §10.7).
    /// </summary>
    /// <remarks>
    /// Linked items are read off the container with <c>TryGetLinkedItems</c>, the documented way to
    /// reach the levels <c>WithLinkedItems</c> loaded when the result is mapped by hand - see
    /// https://docs.kentico.com/documentation/developers-and-admins/api/content-item-api/reference-content-item-query.
    /// Each linked item's own <c>ContentTypeName</c> decides which fields are detected, so a field that
    /// accepts several content types needs no extra configuration here.
    /// </remarks>
    private async Task<bool> Flatten(
        Document document,
        IContentQueryDataContainer data,
        IIndexEventItemModel item,
        HashSet<string> written,
        CancellationToken cancellationToken)
    {
        foreach (var link in options.FlattenedLinksOf(item.ContentTypeName))
        {
            if (!data.TryGetLinkedItems(link.LinkedFieldName, out var linkedItems))
            {
                logger.LogDebug(
                    "No linked items loaded for {ContentType}.{FieldName} on {ItemGuid}; nothing to flatten.",
                    item.ContentTypeName,
                    link.LinkedFieldName,
                    item.ItemGuid);

                continue;
            }

            foreach (var linkedItem in linkedItems)
            {
                foreach (var field in fieldSource.GetFields(linkedItem.ContentTypeName))
                {
                    if (!written.Add(field.Name))
                    {
                        ReportCollision(item.ContentTypeName, link.LinkedFieldName, field.Name);
                        continue;
                    }

                    if (!await TryAddFrom(document, field, linkedItem, item, cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void ReportCollision(string contentTypeName, string linkedFieldName, string fieldName)
    {
        if (!reportedCollisions.Add(contentTypeName + "." + fieldName))
        {
            return;
        }

        logger.LogWarning(
            "Field {FieldName} is defined both by content type {ContentType} and by an item linked from its {LinkedFieldName} field. " +
            "The content type's own value is indexed and the linked item's is ignored.",
            fieldName,
            contentTypeName,
            linkedFieldName);
    }

    /// <summary>Adds resolved tag references to a document as a facet dimension.</summary>
    internal async Task AddTags(Document document, SchemaField field, IEnumerable<TagReference> references, string languageName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(field);

        var identifiers = (references ?? []).Select(reference => reference.Identifier).Distinct().ToList();

        if (identifiers.Count == 0)
        {
            return;
        }

        RegisterDimension(field.LuceneName);

        // Tag identifiers are GUIDs; the code name is what a facet filter refers to and the title is
        // what a visitor would type. See the tag selector in the admin form component reference.
        var tags = await taxonomyRetriever.RetrieveTags(identifiers, languageName, cancellationToken).ConfigureAwait(false);

        // One dimension value may be reached twice - two tags sharing an ancestor, or a document
        // tagged with both a parent and its child - and Lucene rejects a repeated facet path.
        var written = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            // The dimension stays flat (ADR-0018): every ancestor is written as a value in its own
            // right, so counts roll up and a drill-down on a parent matches its descendants without
            // the query side knowing anything about the hierarchy. Ancestors go first, so their
            // taxonomy ordinals stay below their descendants' and a tie in the top-N cut favours
            // the more general value.
            var ancestors = tagAncestry.AncestorsOf(tag.Identifier);

            for (int i = 0; i < ancestors.Count; i++)
            {
                WriteTag(document, field, ancestors[i].Name, ancestors[i].Title, PathOf(ancestors, i), written);
            }

            WriteTag(document, field, tag.Name, tag.Title ?? tag.Name, PathOf(ancestors, ancestors.Count), written);
        }
    }

    /// <summary>Writes one taxonomy value - a tag or one of its ancestors - the way the base mapping does.</summary>
    /// <param name="document">The document being built.</param>
    /// <param name="field">The taxonomy field the value belongs to.</param>
    /// <param name="value">The tag code name.</param>
    /// <param name="title">The tag title.</param>
    /// <param name="path">Code names of the value's ancestors, root first, excluding the value itself.</param>
    /// <param name="written">Code names already written for this field on this document.</param>
    internal static void WriteTag(
        Document document,
        SchemaField field,
        string value,
        string title,
        IReadOnlyList<string> path,
        HashSet<string> written)
    {
        if (!written.Add(value))
        {
            return;
        }

        document.Add(new FacetField(field.LuceneName, value));
        document.Add(new StringField(field.LuceneName, value, Field.Store.YES));
        document.Add(new TextField(LuceneFieldNames.SearchFieldName(field), title, Field.Store.NO));

        // Code name, ancestry and title, verbatim and un-analyzed, so the query side can read the
        // whole taxonomy out of the term dictionary in one pass and hand a facet value both its
        // label and its path.
        document.Add(new StringField(
            LuceneFieldNames.LabelFieldName(field),
            LuceneFieldNames.ComposeLabel(value, title, path),
            Field.Store.NO));
    }

    private static string[] PathOf(IReadOnlyList<TagAncestor> ancestors, int count)
    {
        var path = new string[count];

        for (int i = 0; i < count; i++)
        {
            path[i] = ancestors[i].Name;
        }

        return path;
    }

    /// <summary>Registers every facetable field of the indexes this strategy serves, once.</summary>
    private void RegisterSchemaDimensions()
    {
        if (schemaDimensionsRegistered)
        {
            return;
        }

        // One attempt per instance: a schema that cannot be read must not be retried for every
        // document, and the mapping fallback still registers what the documents actually carry.
        schemaDimensionsRegistered = true;

        foreach (string indexName in accessor.IndexNamesForStrategy(GetType()))
        {
            try
            {
                // FacetsConfigFactory is a synchronous integration API and the schema comes from the
                // database. ASP.NET Core has no synchronization context to deadlock against.
                var schema = schemaProvider.GetSchemaAsync(indexName, CancellationToken.None).GetAwaiter().GetResult();

                foreach (var field in schema.Fields.Where(field => field.Facetable))
                {
                    RegisterDimension(field.LuceneName);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "The schema of index {Index} could not be read, so its facet dimensions are only registered as documents are mapped.",
                    indexName);
            }
        }
    }

    private void RegisterDimension(string dimension)
    {
        if (registeredDimensions.Add(dimension))
        {
            // A taxonomy field always holds a set, so its dimension must accept several values per
            // document; FacetsConfig.Build throws otherwise. Single-valued dimensions lose nothing by
            // being declared multi-valued - counting reads the taxonomy either way.
            facetsConfig.SetMultiValued(dimension, true);
        }
    }

    private async Task<IContentQueryDataContainer?> LoadItem(IIndexEventItemModel item, CancellationToken cancellationToken)
    {
        var builder = new ContentItemQueryBuilder();
        int depth = options.LinkedItemsDepth;

        if (item is IndexEventWebPageItemModel webPage)
        {
            builder.ForContentType(
                item.ContentTypeName,
                config => config
                    .WithLinkedItems(depth)
                    .ForWebsite(webPage.WebsiteChannelName)
                    .Where(where => where.WhereEquals(nameof(IWebPageContentQueryDataContainer.WebPageItemGUID), webPage.ItemGuid)));
        }
        else
        {
            builder.ForContentType(
                item.ContentTypeName,
                config => config
                    .WithLinkedItems(depth)
                    .Where(where => where.WhereEquals(nameof(IContentQueryDataContainer.ContentItemGUID), item.ItemGuid)));
        }

        builder.InLanguage(item.LanguageName);

        var result = await executor
            .GetResult(builder, container => container, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.FirstOrDefault();
    }
}

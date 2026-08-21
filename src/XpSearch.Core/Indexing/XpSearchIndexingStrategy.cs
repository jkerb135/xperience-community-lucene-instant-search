using System.Globalization;

using CMS.ContentEngine;
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
/// <see cref="MapToLuceneDocumentOrNull"/> and call <c>base</c> to add fields of your own; use
/// <see cref="XpSearchIndexingOptions"/> to exclude or re-flag a detected field.
/// </remarks>
public class XpSearchIndexingStrategy : DefaultLuceneIndexingStrategy
{
    private readonly IContentQueryExecutor executor;
    private readonly IWebPageUrlRetriever urlRetriever;
    private readonly ITaxonomyRetriever taxonomyRetriever;
    private readonly IContentTypeFieldSource fieldSource;
    private readonly ILogger<XpSearchIndexingStrategy> logger;

    // Shared with FacetsConfigFactory: dimensions are registered as they are discovered while mapping,
    // which happens before the client asks for the config and builds the documents.
    private readonly FacetsConfig facetsConfig = new();
    private readonly HashSet<string> registeredDimensions = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="XpSearchIndexingStrategy"/> class.</summary>
    /// <param name="executor">Executes the untyped content query that loads the item's field values.</param>
    /// <param name="urlRetriever">Resolves the web page URL stored on the document.</param>
    /// <param name="taxonomyRetriever">Resolves tag identifiers to tag code names and titles.</param>
    /// <param name="fieldSource">Detects the searchable fields of a content type.</param>
    /// <param name="logger">Logger.</param>
    public XpSearchIndexingStrategy(
        IContentQueryExecutor executor,
        IWebPageUrlRetriever urlRetriever,
        ITaxonomyRetriever taxonomyRetriever,
        IContentTypeFieldSource fieldSource,
        ILogger<XpSearchIndexingStrategy> logger)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(urlRetriever);
        ArgumentNullException.ThrowIfNull(taxonomyRetriever);
        ArgumentNullException.ThrowIfNull(fieldSource);
        ArgumentNullException.ThrowIfNull(logger);

        this.executor = executor;
        this.urlRetriever = urlRetriever;
        this.taxonomyRetriever = taxonomyRetriever;
        this.fieldSource = fieldSource;
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
    public override async Task<Document?> MapToLuceneDocumentOrNull(IIndexEventItemModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Same guard as the base strategy: secured content never enters a public index.
        if (item.IsSecured)
        {
            return null;
        }

        var data = await LoadItem(item).ConfigureAwait(false);

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
            new StringField(LuceneFieldNames.SourceField, LuceneFieldNames.XperienceSource, Field.Store.YES)
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

        foreach (var field in fieldSource.GetFields(item.ContentTypeName))
        {
            await AddField(document, field, data, item.LanguageName).ConfigureAwait(false);
        }

        return document;
    }

    /// <inheritdoc />
    public override FacetsConfig FacetsConfigFactory() => facetsConfig;

    private async Task AddField(Document document, SchemaField field, IContentQueryDataContainer data, string languageName)
    {
        switch (field.Kind)
        {
            case SearchFieldKind.Taxonomy:
                await AddTaxonomy(document, field, data, languageName).ConfigureAwait(false);
                break;

            case SearchFieldKind.Text:
                // Rich text arrives as HTML; the platform stripper is used rather than a regex so
                // entities and comments are handled: CMS.Helpers.HTMLHelper.StripTags.
                AddText(document, field.Name, HTMLHelper.StripTags(data.GetValue<string>(field.Name), true, " "), field.Sortable);
                break;

            case SearchFieldKind.Keyword:
                AddKeyword(document, field, data.GetValue<string>(field.Name));
                break;

            case SearchFieldKind.Number when TryGetNumber(data, field.Name, out double number):
                document.Add(new DoubleField(field.Name, number, Field.Store.YES));
                document.Add(new DoubleDocValuesField(field.Name, number));
                break;

            case SearchFieldKind.Date when data.GetValue<DateTime?>(field.Name) is { } date:
                long epochSeconds = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeSeconds();
                document.Add(new Int64Field(field.Name, epochSeconds, Field.Store.YES));
                document.Add(new NumericDocValuesField(field.Name, epochSeconds));
                break;

            default:
                break;
        }
    }

    private async Task AddTaxonomy(Document document, SchemaField field, IContentQueryDataContainer data, string languageName)
    {
        var references = data.GetValue<IEnumerable<TagReference>>(field.Name);
        var identifiers = (references ?? []).Select(reference => reference.Identifier).Distinct().ToList();

        if (identifiers.Count == 0)
        {
            return;
        }

        RegisterDimension(field.Name);

        // Tag identifiers are GUIDs; the code name is what a facet filter refers to and the title is
        // what a visitor would type. See the tag selector in the admin form component reference.
        var tags = await taxonomyRetriever.RetrieveTags(identifiers, languageName).ConfigureAwait(false);

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            document.Add(new FacetField(field.Name, tag.Name));
            document.Add(new StringField(field.Name, tag.Name, Field.Store.YES));
            document.Add(new TextField(LuceneFieldNames.SearchFieldName(field), tag.Title ?? tag.Name, Field.Store.NO));

            // The pair, verbatim and un-analyzed, so the query side can read every code name's
            // title straight out of the term dictionary and put it in the facet value's label.
            document.Add(new StringField(
                LuceneFieldNames.LabelFieldName(field),
                LuceneFieldNames.ComposeLabel(tag.Name, tag.Title ?? tag.Name),
                Field.Store.NO));
        }
    }

    private void RegisterDimension(string dimension)
    {
        if (registeredDimensions.Add(dimension))
        {
            // A taxonomy field always holds a set, so its dimension must accept several values per
            // document; FacetsConfig.Build throws otherwise.
            facetsConfig.SetMultiValued(dimension, true);
        }
    }

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

        document.Add(new StringField(field.Name, value, Field.Store.YES));

        if (field.Sortable)
        {
            document.Add(new SortedDocValuesField(LuceneFieldNames.SortFieldName(field), new Lucene.Net.Util.BytesRef(value)));
        }
    }

    private static bool TryGetNumber(IContentQueryDataContainer data, string name, out double value)
    {
        value = 0;
        object? raw = data.GetValue<object>(name);

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

    private async Task<IContentQueryDataContainer?> LoadItem(IIndexEventItemModel item)
    {
        var builder = new ContentItemQueryBuilder();

        if (item is IndexEventWebPageItemModel webPage)
        {
            builder.ForContentType(
                item.ContentTypeName,
                config => config
                    .WithLinkedItems(1)
                    .ForWebsite(webPage.WebsiteChannelName)
                    .Where(where => where.WhereEquals(nameof(IWebPageContentQueryDataContainer.WebPageItemGUID), webPage.ItemGuid)));
        }
        else
        {
            builder.ForContentType(
                item.ContentTypeName,
                config => config
                    .WithLinkedItems(1)
                    .Where(where => where.WhereEquals(nameof(IContentQueryDataContainer.ContentItemGUID), item.ItemGuid)));
        }

        builder.InLanguage(item.LanguageName);

        var result = await executor
            .GetResult(builder, container => container, cancellationToken: default)
            .ConfigureAwait(false);

        return result.FirstOrDefault();
    }
}

using XpSearch.Core.Abstractions;

namespace XpSearch.Core.Indexing;

/// <summary>
/// The naming conventions shared by <see cref="XpSearchIndexingStrategy"/> (which writes the fields)
/// and the query pipeline (which reads them). Both sides must agree, so neither hard-codes a suffix.
/// </summary>
public static class LuceneFieldNames
{
    /// <summary>Suffix of the doc-values field that makes a text or keyword attribute sortable.</summary>
    public const string SortSuffix = "_sort";

    /// <summary>Suffix of the analyzed field that makes taxonomy tag titles free-text searchable.</summary>
    public const string TextSuffix = "_text";

    /// <summary>Gets the doc-values field a sort on the given attribute reads.</summary>
    /// <param name="field">The schema field, which must be sortable.</param>
    /// <returns>The Lucene field name to build a <c>SortField</c> over.</returns>
    public static string SortFieldName(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // Numbers and dates sort straight off their numeric doc values; strings need a separate
        // SortedDocValuesField because the indexed field is analyzed or facet-encoded.
        return field.Kind is SearchFieldKind.Number or SearchFieldKind.Date
            ? field.Name
            : field.Name + SortSuffix;
    }

    /// <summary>Gets the analyzed field free-text queries match for the given attribute.</summary>
    /// <param name="field">The schema field, which must be searchable.</param>
    /// <returns>The Lucene field name to add to the query parser.</returns>
    public static string SearchFieldName(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // A taxonomy attribute stores tag code names verbatim for retrieval and drill-down; the
        // human-readable titles live in a parallel analyzed field.
        return field.Kind == SearchFieldKind.Taxonomy ? field.Name + TextSuffix : field.Name;
    }
}

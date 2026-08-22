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

    /// <summary>
    /// Reserved field carrying a document's provenance (spec §10.2). Every document written by this
    /// library has one, so a rebuild of Xperience content and a scoped clear of one external source
    /// can tell each other's documents apart.
    /// </summary>
    public const string SourceField = "_source";

    /// <summary>Value of <see cref="SourceField"/> on documents the Lucene integration indexes from Xperience content.</summary>
    public const string XperienceSource = "xperience";

    /// <summary>Suffix of the analyzed field that makes taxonomy tag titles free-text searchable.</summary>
    public const string TextSuffix = "_text";

    /// <summary>Suffix of the indexed field that pairs a taxonomy tag code name with its title.</summary>
    public const string LabelSuffix = "_label";

    /// <summary>Separates the code name from the title inside a label term. ASCII unit separator: never part of either.</summary>
    public const char LabelSeparator = '\u001f';

    /// <summary>Gets the field whose terms map the code names of a taxonomy dimension to their titles.</summary>
    /// <param name="field">The schema field, which must be a taxonomy dimension.</param>
    /// <returns>The Lucene field name to enumerate terms of.</returns>
    public static string LabelFieldName(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.LuceneName + LabelSuffix;
    }

    /// <summary>Composes one label term out of a tag code name and its title.</summary>
    /// <param name="value">The tag code name, which is what a facet filter refers to.</param>
    /// <param name="title">The tag title, which is what a facet list displays.</param>
    /// <returns>The term to index in <see cref="LabelFieldName"/>.</returns>
    public static string ComposeLabel(string value, string title) => $"{value}{LabelSeparator}{title}";

    /// <summary>Splits a label term back into its code name and title.</summary>
    /// <param name="term">A term of a label field.</param>
    /// <returns>The code name and the title, both <see langword="null"/> when the term is malformed.</returns>
    public static (string? Value, string? Title) SplitLabel(string term)
    {
        int at = term?.IndexOf(LabelSeparator) ?? -1;

        return at > 0 && at < term!.Length - 1 ? (term[..at], term[(at + 1)..]) : (null, null);
    }

    /// <summary>Gets the doc-values field a sort on the given attribute reads.</summary>
    /// <param name="field">The schema field, which must be sortable.</param>
    /// <returns>The Lucene field name to build a <c>SortField</c> over.</returns>
    public static string SortFieldName(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // Numbers and dates sort straight off their numeric doc values; strings need a separate
        // SortedDocValuesField because the indexed field is analyzed or facet-encoded.
        return field.Kind is SearchFieldKind.Number or SearchFieldKind.Date
            ? field.LuceneName
            : field.LuceneName + SortSuffix;
    }

    /// <summary>Gets the analyzed field free-text queries match for the given attribute.</summary>
    /// <param name="field">The schema field, which must be searchable.</param>
    /// <returns>The Lucene field name to add to the query parser.</returns>
    public static string SearchFieldName(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        // A taxonomy attribute stores tag code names verbatim for retrieval and drill-down; the
        // human-readable titles live in a parallel analyzed field.
        return field.Kind == SearchFieldKind.Taxonomy ? field.LuceneName + TextSuffix : field.LuceneName;
    }
}

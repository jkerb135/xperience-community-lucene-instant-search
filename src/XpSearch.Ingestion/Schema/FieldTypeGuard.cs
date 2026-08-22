using Lucene.Net.Index;

using XpSearch.Core.Abstractions;
using XpSearch.Ingestion.Contract;

namespace XpSearch.Ingestion.Schema;

/// <summary>
/// Detects a schema field whose type no longer matches the way the index already encodes it
/// (spec §10.3: "Changing a field's type requires a rebuild. Detect and say so plainly").
/// </summary>
public interface IFieldTypeGuard
{
    /// <summary>Checks the fields a write touches against the live index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="schema">The current schema.</param>
    /// <param name="fieldNames">The attribute names the write carries.</param>
    /// <returns>One error per field whose declared type contradicts the index, empty when all agree.</returns>
    IReadOnlyList<IngestionError> Check(string indexName, IndexSchema schema, IEnumerable<string> fieldNames);
}

/// <summary>
/// Reads the index's own field infos and compares each field's encoding with what the schema now
/// declares. Numeric fields carry numeric doc values; text, keyword, boolean and taxonomy fields do
/// not - which is exactly the difference that silently breaks sorting and range filters when a
/// field's type changes under an index that was never rebuilt.
/// </summary>
public sealed class FieldTypeGuard : IFieldTypeGuard
{
    private readonly ILuceneIndexAccessor accessor;

    /// <summary>Initializes a new instance of the <see cref="FieldTypeGuard"/> class.</summary>
    /// <param name="accessor">The Lucene reader seam.</param>
    public FieldTypeGuard(ILuceneIndexAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);

        this.accessor = accessor;
    }

    /// <inheritdoc />
    public IReadOnlyList<IngestionError> Check(string indexName, IndexSchema schema, IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fieldNames);

        var names = fieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (names.Count == 0 || !accessor.Exists(indexName))
        {
            return [];
        }

        return accessor.UseSearcher(indexName, searcher =>
        {
            var errors = new List<IngestionError>();
            var infos = MultiFields.GetMergedFieldInfos(searcher.IndexReader);

            foreach (string name in names)
            {
                var field = schema.Find(name);
                var info = field is null ? null : infos.FieldInfo(field.LuceneName);

                if (field is null || info is null)
                {
                    continue;
                }

                bool declaredNumeric = field.Kind is SearchFieldKind.Number or SearchFieldKind.Date;
                bool indexedNumeric = info.DocValuesType == DocValuesType.NUMERIC;

                if (declaredNumeric != indexedNumeric)
                {
                    errors.Add(new IngestionError
                    {
                        Field = field.Name,
                        Message = $"Field '{field.Name}' is declared as {DocumentValidator.Describe(field.Kind)} but the index already stores it as " +
                            $"{(indexedNumeric ? "a number" : "text")}. Changing a field's type requires a rebuild: POST /api/xpsearch/admin/indexes/{indexName}/rebuild.",
                    });
                }
            }

            return (IReadOnlyList<IngestionError>)errors;
        });
    }
}

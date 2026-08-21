using Lucene.Net.Documents;
using Lucene.Net.Facet;

namespace XpSearch.FacetSpike;

/// <summary>Bits both backends share: the non-facet document fields, facet harvesting and disk accounting.</summary>
internal static class SpikeIo
{
    internal const string IdField = "id";
    internal const string TitleField = "title";
    internal const string ContentField = "content";

    /// <summary>topN large enough to return every value of any dimension in this corpus.</summary>
    internal const int AllValues = 10_000;

    internal static Document BaseDocument(Doc doc) =>
    [
        new StringField(IdField, doc.Id, Field.Store.YES),
        new TextField(TitleField, doc.Title, Field.Store.NO),
        new TextField(ContentField, doc.Content, Field.Store.NO)
    ];

    internal static FacetCounts Collect(Facets facets, IReadOnlyList<string> dims, int topN)
    {
        var counts = new FacetCounts();
        foreach (string dim in dims)
        {
            var values = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = facets.GetTopChildren(topN, dim);
            if (result is not null)
            {
                foreach (var labelValue in result.LabelValues)
                {
                    values[labelValue.Label] = (int)labelValue.Value;
                }
            }

            counts[dim] = values;
        }

        return counts;
    }

    internal static long DirectorySize(string path) =>
        Directory.Exists(path)
            ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

    internal static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }
}

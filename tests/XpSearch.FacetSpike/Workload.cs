using Lucene.Net.Index;
using Lucene.Net.Search;

namespace XpSearch.FacetSpike;

/// <summary>A faceted query and the class it belongs to in the report.</summary>
internal sealed record FacetQuery(string Class, Query Query);

/// <summary>A drill-sideways query: base query plus one facet filter.</summary>
internal sealed record DrillQuery(Query Base, string Dim, string Value);

/// <summary>
/// Deterministic query workload. Built once per corpus size and replayed verbatim against both
/// backends so latency differences are attributable to the facet implementation only.
/// </summary>
internal static class Workload
{
    /// <summary>Query terms are drawn from the Zipf head so every term query actually matches documents.</summary>
    private const int TermPoolSize = 200;

    internal const string MatchAll = "match-all";
    internal const string SingleTerm = "single-term";
    internal const string TwoTermOr = "two-term-or";

    private static Query TermQuery(string word) => new Lucene.Net.Search.TermQuery(new Term(SpikeIo.ContentField, word));

    internal static IReadOnlyList<FacetQuery> Warmup() => [.. Build(new Random(11), 7).Take(20)];

    /// <summary>100 match-all, 100 single-term, 100 two-term OR.</summary>
    internal static IReadOnlyList<FacetQuery> Faceted() => Build(new Random(1337), 100);

    private static IReadOnlyList<FacetQuery> Build(Random random, int perClass)
    {
        var queries = new List<FacetQuery>(perClass * 3);

        for (int i = 0; i < perClass; i++)
        {
            queries.Add(new FacetQuery(MatchAll, new MatchAllDocsQuery()));
        }

        for (int i = 0; i < perClass; i++)
        {
            queries.Add(new FacetQuery(SingleTerm, TermQuery(Word(random))));
        }

        for (int i = 0; i < perClass; i++)
        {
            var or = new BooleanQuery
            {
                { TermQuery(Word(random)), Occur.SHOULD },
                { TermQuery(Word(random)), Occur.SHOULD }
            };
            queries.Add(new FacetQuery(TwoTermOr, or));
        }

        return queries;
    }

    /// <summary>100 drill-sideways queries: a term query plus a contentType or tags filter.</summary>
    internal static IReadOnlyList<DrillQuery> Drills()
    {
        var random = new Random(2024);
        var drills = new List<DrillQuery>(100);
        for (int i = 0; i < 100; i++)
        {
            bool byContentType = i % 2 == 0;
            drills.Add(new DrillQuery(
                TermQuery(Word(random)),
                byContentType ? Dims.ContentType : Dims.Tags,
                byContentType
                    ? Corpus.ContentTypes[random.Next(Corpus.ContentTypes.Length)]
                    : Corpus.Tags[random.Next(Corpus.Tags.Length)]));
        }

        return drills;
    }

    /// <summary>30 fixed queries used by the A/B correctness proof.</summary>
    internal static IReadOnlyList<Query> Verification()
    {
        var random = new Random(99);
        var queries = new List<Query>(30);
        for (int i = 0; i < 5; i++)
        {
            queries.Add(new MatchAllDocsQuery());
        }

        for (int i = 0; i < 13; i++)
        {
            queries.Add(TermQuery(Word(random)));
        }

        for (int i = 0; i < 12; i++)
        {
            queries.Add(new BooleanQuery
            {
                { TermQuery(Word(random)), Occur.SHOULD },
                { TermQuery(Word(random)), Occur.SHOULD }
            });
        }

        return queries;
    }

    private static string Word(Random random) => Corpus.Vocabulary[random.Next(TermPoolSize)];
}

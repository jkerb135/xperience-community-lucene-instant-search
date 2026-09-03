using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using XpSearch.Core.Caching;
using XpSearch.Core.Contract;
using XpSearch.Core.Facets;
using XpSearch.Core.Highlighting;
using XpSearch.Core.Indexing;
using XpSearch.Core.Options;
using XpSearch.Core.Pipeline;
using XpSearch.Core.Pipeline.Stages;
using XpSearch.Core.Search;

namespace XpSearch.Bench;

/// <summary>
/// PF-1, spec §12: measures the real query pipeline over synthetic 10k / 100k / 1M corpora and
/// writes the dated result document the sizing guide is built from.
/// </summary>
/// <remarks>
/// Release configuration, single process, single threaded, minutes long. It is a tool, not a test:
/// nothing here runs as part of the suites.
/// </remarks>
internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Attributes counted on every faceted request: the two synthetic dimensions plus the two every document carries.</summary>
    private static readonly string[] FacetAttributes =
        [BenchIndex.SectionAttribute, BenchIndex.TopicAttribute, IndexSchemaProvider.ContentTypeAttribute, IndexSchemaProvider.LanguageAttribute];

    private static readonly string[] HighlightFields = [IndexSchemaProvider.TitleAttribute, BenchIndex.BodyAttribute];

    internal static async Task<int> Main(string[] args)
    {
        long[] sizes = [.. Arg(args, "--sizes", "10k,100k,1m")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseSize)];
        int runs = int.Parse(Arg(args, "--runs", "3"), Inv);
        int iterations = int.Parse(Arg(args, "--iterations", "100"), Inv);

        string root = Path.Combine(Path.GetTempPath(), "xpsearch-bench");
        var builds = new Dictionary<long, BuildResult>();
        var latencies = new Dictionary<(long Size, string Workload), List<Stats>>();
        var totals = new Dictionary<(long Size, string Workload), long>();
        var order = new List<string>();

        try
        {
            foreach (long size in sizes)
            {
                await RunSizeAsync(size, root, runs, iterations, builds, latencies, totals, order).ConfigureAwait(false);
            }
        }
        finally
        {
            Cleanup(root);
        }

        string report = BuildReport(sizes, order, runs, iterations, builds, latencies, totals);
        Console.WriteLine();
        Console.WriteLine(report);

        string outPath = Arg(args, "--out", DefaultOutPath());
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        await File.WriteAllTextAsync(outPath, report).ConfigureAwait(false);
        Console.WriteLine(string.Create(Inv, $"written: {outPath}"));

        return 0;
    }

    private static async Task RunSizeAsync(
        long size,
        string root,
        int runs,
        int iterations,
        Dictionary<long, BuildResult> builds,
        Dictionary<(long, string), List<Stats>> latencies,
        Dictionary<(long, string), long> totals,
        List<string> order)
    {
        string path = Path.Combine(root, size.ToString(Inv));
        Cleanup(path);

        Console.WriteLine(string.Create(Inv, $"# corpus {size:N0} docs - building index at {path}"));

        using var index = new BenchIndex(path);
        var build = index.Build(Corpus.Generate(size));
        builds[size] = build;

        Console.WriteLine(string.Create(Inv, $"  built in {build.BuildMs / 1000:F1} s ({size / (build.BuildMs / 1000):N0} docs/s), {(build.MainBytes + build.TaxonomyBytes) / (1024.0 * 1024):N0} MB on disk, reader open {build.ReaderOpenMs:F1} ms"));

        var options = new XpSearchOptions();
        var wrapped = new StaticOptionsMonitor<XpSearchOptions>(options);
        var tuning = new BenchTuningSource();
        var schema = new StaticSchemaProvider();

        var plain = BuildPipeline(index, schema, wrapped, tuning, fuzzy: false);
        var fuzzy = BuildPipeline(index, schema, wrapped, tuning, fuzzy: true);
        var cached = new CachedSearchPipeline(
            plain,
            new MemorySearchCache(),
            wrapped,
            new NoContactGroups(),
            new NoExperiment(),
            new NoJournal(),
            new NoPopularity(),
            new FixedTypoToleranceSource(false));

        var suggest = new DocumentSuggestService(index, schema, new NoQuerySuggestions(), wrapped, NullLogger<DocumentSuggestService>.Instance);

        var workloads = BuildWorkloads(plain, fuzzy, cached, suggest, iterations);

        if (order.Count == 0)
        {
            order.AddRange(workloads.Select(w => w.Name));
        }

        // Warm-up: first-touch costs (analyzer, doc-values loading, JIT of the stage chain) belong to
        // process start, not to a query.
        foreach (var workload in workloads)
        {
            for (int i = 0; i < 10; i++)
            {
                await workload.Run(i).ConfigureAwait(false);
            }
        }

        for (int run = 1; run <= runs; run++)
        {
            foreach (var workload in workloads)
            {
                Console.Write(string.Create(Inv, $"  run {run}/{runs} {workload.Name} ... "));
                GC.Collect();
                GC.WaitForPendingFinalizers();

                var stats = await Measure.RunAsync(workload, iterations).ConfigureAwait(false);

                if (!latencies.TryGetValue((size, workload.Name), out var list))
                {
                    list = [];
                    latencies[(size, workload.Name)] = list;
                }

                list.Add(stats);
                totals[(size, workload.Name)] = await MatchCountAsync(workload).ConfigureAwait(false);
                Console.WriteLine(string.Create(Inv, $"p50 {stats.P50:F2} ms, p95 {stats.P95:F2} ms"));
            }
        }
    }

    /// <summary>The stage chain <c>AddXpSearch</c> composes, minus the stages that need a live Xperience request.</summary>
    private static SearchPipeline BuildPipeline(
        BenchIndex index,
        StaticSchemaProvider schema,
        Microsoft.Extensions.Options.IOptionsMonitor<XpSearchOptions> options,
        BenchTuningSource tuning,
        bool fuzzy) =>
        new(
            index,
            schema,
            [
                new NormalizeRequestStage(options),
                new QueryRewriteStage(tuning, TimeProvider.System),
                new SynonymExpansionStage(tuning),
                new StopwordRemovalStage(),
                new BuildQueryStage(new FixedTypoToleranceSource(fuzzy)),
                new FacetFilterStage(),
                new NumericFilterStage(),
                new BoostRulesStage(),
                new ExecuteSearchStage(index),
                new PinnedAndBuriedStage(index),
                new CollectFacetsStage(new TaxonomyFacetProvider(index), options),
                new HighlightStage(new LuceneHighlighter()),
                new ProjectResponseStage()
            ]);

    private static IReadOnlyList<Workload> BuildWorkloads(
        ISearchPipeline plain,
        ISearchPipeline fuzzy,
        ISearchPipeline cached,
        DocumentSuggestService suggest,
        int iterations)
    {
        // Query texts vary per iteration, so no cache anywhere - Lucene's or ours - can answer twice.
        string[] first = QueryTerms(iterations, 1);
        string[] second = QueryTerms(iterations, 2);
        string[] prefixes = [.. first.Select(term => term[..3])];

        // The cache-hit row is the one workload that repeats a request on purpose.
        var repeated = Request(first[0]);

        return
        [
            new Workload("match-all + facets", i => Search(plain, Request(string.Empty))),
            new Workload("single-term", i => Search(plain, Request(first[i]))),
            new Workload("single-term, no highlight", i => Search(plain, NoHighlight(first[i]))),
            new Workload("two-term OR", i => Search(plain, Request(first[i] + " " + second[i]))),
            new Workload("term + facet filter + numeric range", i => Search(plain, Filtered(first[i]))),
            new Workload("single-term, sorted by price", i => Search(plain, Sorted(first[i]))),
            new Workload("match-all, deep page (rank 10,000)", i => Search(plain, DeepPage())),
            new Workload("single-term, fuzzy on", i => Search(fuzzy, Request(first[i]))),
            new Workload("single-term, fuzzy on, no highlight", i => Search(fuzzy, NoHighlight(first[i]))),
            new Workload("suggest prefix (Documents mode)", async i =>
                await suggest.SuggestAsync(new SuggestRequest { Index = BenchIndex.IndexName, Query = prefixes[i] }, CancellationToken.None).ConfigureAwait(false)),
            new Workload("cache hit (same request)", i => Search(cached, repeated))
        ];
    }

    private static async Task<object?> Search(ISearchPipeline pipeline, SearchRequest request) =>
        await pipeline.ExecuteAsync(request, CancellationToken.None).ConfigureAwait(false);

    private static SearchRequest Request(string query) => new()
    {
        Index = BenchIndex.IndexName,
        Query = query,
        Facets = FacetAttributes,
        Highlight = new HighlightOptions { Fields = HighlightFields },
        PageSize = 20
    };

    /// <summary>
    /// The fuzzy row with highlighting off, which is what isolates the highlighter's cost on a
    /// multi-term query - the dominant term of the fuzzy row (see the results doc).
    /// </summary>
    private static SearchRequest NoHighlight(string query)
    {
        var request = Request(query);
        request.Highlight = null;

        return request;
    }

    private static SearchRequest Filtered(string query)
    {
        var request = Request(query);
        request.Filters = new Filters
        {
            Facets = [new FacetFilter { Attribute = BenchIndex.SectionAttribute, Values = [Corpus.Section(0), Corpus.Section(1)] }],
            Numeric = [new NumericFilter { Attribute = BenchIndex.PriceAttribute, Operator = NumericOperator.Gte, Value = 250 }]
        };

        return request;
    }

    private static SearchRequest Sorted(string query)
    {
        var request = Request(query);
        request.Sort = BenchIndex.PriceAttribute + SortKeyParser.DescendingSuffix;

        return request;
    }

    private static SearchRequest DeepPage()
    {
        var request = Request(string.Empty);
        request.Page = 500;

        return request;
    }

    /// <summary>
    /// How many documents the workload's query actually matched, so a row can be read as a real
    /// measurement rather than as the cost of finding nothing.
    /// </summary>
    private static async Task<long> MatchCountAsync(Workload workload)
    {
        var result = await workload.Run(0).ConfigureAwait(false);

        return result is SearchResponse response ? response.Total : -1;
    }

    /// <summary>
    /// One query term per iteration, drawn from the frequent half of the vocabulary so match counts
    /// vary from a few hundred documents to a large fraction of the corpus.
    /// </summary>
    private static string[] QueryTerms(int count, int seed)
    {
        var random = new Random(seed);

        return [.. Enumerable.Range(0, count).Select(_ => Corpus.Vocabulary[random.Next(500)])];
    }

    private static long ParseSize(string value)
    {
        string text = value.Trim().ToLowerInvariant();
        long multiplier = text.EndsWith('m') ? 1_000_000 : text.EndsWith('k') ? 1_000 : 1;

        return multiplier == 1 ? long.Parse(text, Inv) : long.Parse(text[..^1], Inv) * multiplier;
    }

    private static string Arg(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);

        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    private static void Cleanup(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Walks up from the binary to the repository root, which is the directory holding CHANGELOG.md.</summary>
    private static string DefaultOutPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
        {
            dir = dir.Parent;
        }

        string name = string.Create(Inv, $"perf-results-{DateTime.Now:yyyy-MM-dd}.md");

        return dir is null ? Path.Combine(AppContext.BaseDirectory, name) : Path.Combine(dir.FullName, "docs", "internal", name);
    }

    private static string BuildReport(
        long[] sizes,
        List<string> order,
        int runs,
        int iterations,
        Dictionary<long, BuildResult> builds,
        Dictionary<(long Size, string Workload), List<Stats>> latencies,
        Dictionary<(long Size, string Workload), long> totals)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PF-1 - pipeline performance results (spec §12)");
        sb.AppendLine();
        sb.AppendLine(Inv, $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `XpSearch.Bench`, Release configuration.");
        sb.AppendLine(Inv, $"Every latency cell is the **median of {runs} runs** of {iterations} queries, with `[min-max]` across those runs alongside.");
        sb.AppendLine("Percentiles are nearest-rank within a run: a reported value is always a measured value.");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine("| Item | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(Inv, $"| CPU | {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown"} ({Environment.ProcessorCount} logical cores) |");
        sb.AppendLine(Inv, $"| RAM (available to runtime) | {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024):F1} GB |");
        sb.AppendLine(Inv, $"| OS | {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}) |");
        sb.AppendLine(Inv, $"| .NET | {RuntimeInformation.FrameworkDescription} |");
        sb.AppendLine(Inv, $"| Lucene.Net | {LuceneVersionString()} |");
        sb.AppendLine(Inv, $"| Index storage | `FSDirectory` under `{Path.GetTempPath()}` (disk type is not discoverable from managed code - see Caveats) |");
        sb.AppendLine();

        sb.AppendLine("## What is being measured");
        sb.AppendLine();
        sb.AppendLine("The **product pipeline**, not raw Lucene: `SearchPipeline` with the stage chain `AddXpSearch` composes -");
        sb.AppendLine("normalize → query rewrite → synonym expansion → stopwords → build query → facet filters → numeric filters →");
        sb.AppendLine("boost rules → execute (`DrillSideways`) → pinned/buried → collect facets → highlight → project. Tuning is");
        sb.AppendLine("present but light, the way a modest site configures it: 5 two-way synonym groups, one always-on boost rule");
        sb.AppendLine("(`contentType:Article`, ×1.5) and non-default field weights (`title` ×3). Every request asks for facet counts on");
        sb.AppendLine(Inv, $"four dimensions (`{string.Join("`, `", FacetAttributes)}`) and highlighted snippets for `title` and `body`, page size 20.");
        sb.AppendLine();
        sb.AppendLine("**The headline numbers are uncached.** Every iteration of every row but the last uses a different query text, so");
        sb.AppendLine("neither the response cache nor any Lucene-side reuse can answer twice. The single cache-hit row is there for");
        sb.AppendLine("contrast only.");
        sb.AppendLine();
        sb.AppendLine(Inv, $"Corpus: deterministic (`Random({Corpus.Seed})`), a Zipf-distributed 5,000-token vocabulary, titles of 3-6 words,");
        sb.AppendLine("bodies of 50-500 words skewed short, a ~10-value facet dimension (`section`), a ~1,000-value one (`topic`, 1-3 per");
        sb.AppendLine("document), a `price` number, a language and a content type. About 2% of documents carry one of five high-frequency");
        sb.AppendLine("marker terms so match counts vary across the workload.");
        sb.AppendLine();

        sb.AppendLine("## Index build, size and reader open");
        sb.AppendLine();
        sb.AppendLine("Corpus generation and indexing in one pass, stock `IndexWriterConfig`, single-threaded, one commit at the end.");
        sb.AppendLine("Measured once per size (rebuilding a 1M index three times measures the disk, not the library).");
        sb.AppendLine();
        sb.AppendLine("| Docs | Build + commit | Throughput (docs/s) | Main index (MB) | Taxonomy (MB) | Total (MB) | Bytes/doc | Cold reader open (ms) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (long size in sizes)
        {
            var b = builds[size];
            double totalMb = (b.MainBytes + b.TaxonomyBytes) / (1024.0 * 1024);
            sb.AppendLine(Inv, $"| {size:N0} | {FormatDuration(b.BuildMs)} | {size / (b.BuildMs / 1000):N0} | {b.MainBytes / (1024.0 * 1024):N1} | {b.TaxonomyBytes / (1024.0 * 1024):N2} | {totalMb:N1} | {(b.MainBytes + b.TaxonomyBytes) / size:N0} | {b.ReaderOpenMs:F2} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Query latency (ms)");
        sb.AppendLine();
        sb.AppendLine("| Docs | Workload | Matched docs | p50 | p95 | max |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (long size in sizes)
        {
            foreach (string workload in order)
            {
                var value = latencies[(size, workload)];
                long matched = totals[(size, workload)];
                sb.AppendLine(Inv, $"| {size:N0} | {workload} | {(matched < 0 ? "n/a" : matched.ToString("N0", Inv))} | {Measure.Agg(value.Select(s => s.P50))} | {Measure.Agg(value.Select(s => s.P95))} | {Measure.Agg(value.Select(s => s.Max))} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Caveats");
        sb.AppendLine();
        sb.AppendLine("- Synthetic corpus. Term frequencies are realistic in shape (Zipf), but real content has phrases, stopwords, a much longer tail and far more varied document lengths.");
        sb.AppendLine("- `FSDirectory` on the local temp disk. **The disk type is not discoverable from managed code**, so the build and reader-open numbers carry whatever this machine's storage is; treat them as a machine-specific floor, not a portable constant.");
        sb.AppendLine("- Single process, single threaded, no concurrent query load. A production site serves queries concurrently; these numbers are per-query service time, not throughput.");
        sb.AppendLine("- The searcher is opened once and reused, which is what the integration's cached searcher lease does. Nothing here measures index writes competing with reads.");
        sb.AppendLine("- Journaling, contact-group resolution, experiments and the popularity signal are stubbed out: they are database round-trips on a real site and would measure SQL, not this library.");
        sb.AppendLine("- The cache-hit row measures `CachedSearchPipeline` over an in-memory dictionary. On a real site the cache is Xperience's `IProgressiveCache`, which is slower than a dictionary but still nowhere near a search.");
        sb.AppendLine("- The two `no highlight` rows are the same requests with `highlight` omitted. Subtracting them from the rows above isolates what `HighlightStage` costs on an ordinary query and on a fuzzy (multi-term) one; they are here because those two costs are not remotely the same.");
        sb.AppendLine("- The build row includes corpus generation, which is part of the same loop and cannot be subtracted. Real content is read from the database instead, which is slower - treat the build number as a floor.");
        return sb.ToString();
    }

    private static string FormatDuration(double ms) =>
        ms < 10_000 ? string.Create(Inv, $"{ms:N0} ms") : string.Create(Inv, $"{ms / 1000:N1} s");

    private static string LuceneVersionString() =>
        typeof(Lucene.Net.Index.IndexWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Lucene.Net.Index.IndexWriter).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace XpSearch.FacetSpike;

/// <summary>SP-1 faceting spike: measures option A (taxonomy sidecar) against option B (SortedSet DocValues).</summary>
internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal static int Main(string[] args)
    {
        int[] sizes = Arg(args, "--sizes", "10000,100000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, Inv))
            .ToArray();
        int runs = int.Parse(Arg(args, "--runs", "3"), Inv);
        bool verify = !args.Contains("--skip-verify", StringComparer.Ordinal);

        string root = Path.Combine(Path.GetTempPath(), "xpsearch-facet-spike");
        SpikeIo.ResetDirectory(root);

        var results = new Dictionary<(int Size, string Backend), List<RunResult>>();
        var backends = new (string Name, Func<string, IFacetBackend> Factory)[]
        {
            ("A (taxonomy)", path => new TaxonomyBackend(path)),
            ("B (docvalues)", path => new SortedSetBackend(path))
        };

        try
        {
            foreach (int size in sizes)
            {
                Console.WriteLine(string.Create(Inv, $"# corpus {size:N0} docs (Random(42))"));
                var docs = Corpus.Generate(size);

                if (verify)
                {
                    Verify.AssertBackendsAgree(docs, root);
                }

                for (int run = 1; run <= runs; run++)
                {
                    foreach (var (name, factory) in backends)
                    {
                        Console.WriteLine(string.Create(Inv, $"  run {run}/{runs} {name} ..."));
                        var result = Measure.Run(factory, Path.Combine(root, "bench"), docs);
                        if (!results.TryGetValue((size, name), out var list))
                        {
                            list = [];
                            results[(size, name)] = list;
                        }

                        list.Add(result);
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        string report = BuildReport(sizes, backends.Select(b => b.Name).ToArray(), runs, results, verify);
        Console.WriteLine();
        Console.WriteLine(report);

        string outPath = Arg(args, "--out", DefaultOutPath());
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, report);
        Console.WriteLine(string.Create(Inv, $"written: {outPath}"));

        Directory.Delete(root, recursive: true);
        return 0;
    }

    private static string Arg(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    private static string DefaultOutPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !string.Equals(dir.Name, "xperience-search", StringComparison.Ordinal))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? Path.Combine(AppContext.BaseDirectory, "spike-faceting-results.md")
            : Path.Combine(dir.FullName, "docs", "internal", "spike-faceting-results.md");
    }

    private static string BuildReport(
        int[] sizes,
        string[] backends,
        int runs,
        Dictionary<(int Size, string Backend), List<RunResult>> results,
        bool verified)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# SP-1 - faceting spike results (spec 4.5 / 13.1)");
        sb.AppendLine();
        sb.AppendLine(Inv, $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by `XpSearch.FacetSpike`, Release configuration.");
        sb.AppendLine(Inv, $"Every cell is the **median of {runs} runs** in a single process, with `[min-max]` alongside.");
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
        sb.AppendLine(Inv, $"| Index storage | `FSDirectory` under `{Path.GetTempPath()}` (disk type not discoverable from managed code; see Caveats) |");
        sb.AppendLine();
        sb.AppendLine("## Correctness proof");
        sb.AppendLine();
        sb.AppendLine(verified
            ? "PASS - for 30 fixed queries (5 match-all, 13 single-term, 12 two-term OR) A and B produced identical counts for `contentType`, `language` and `tags`, and B's flat `a/b/c` counts equalled A's `category` leaf-path counts. Run with `--skip-verify` to skip it on repeat runs."
            : "SKIPPED (`--skip-verify`). The numbers below are only meaningful alongside a verified run.");
        sb.AppendLine();

        sb.AppendLine("## Index build and on-disk size");
        sb.AppendLine();
        sb.AppendLine("| Docs | Backend | Build + commit (ms) | Main index (MB) | Taxonomy (MB) | Total (MB) | First reader open (ms) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
        {
            foreach (string backend in backends)
            {
                var r = results[(size, backend)];
                sb.AppendLine(Inv, $"| {size:N0} | {backend} | {Agg(r, x => x.BuildMs)} | {Agg(r, x => Mb(x.MainBytes))} | {Agg(r, x => Mb(x.TaxonomyBytes))} | {Agg(r, x => Mb(x.MainBytes + x.TaxonomyBytes))} | {Agg(r, x => x.InitialOpenMs)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Query latency (ms)");
        sb.AppendLine();
        sb.AppendLine("300 faceted queries (100 match-all, 100 single-term, 100 two-term OR), top-10 counts for `contentType`, `language`, `tags` and `category`; then 100 drill-sideways queries (term query + one `contentType`/`tags` filter, sideways counts for all four dimensions). 20 warm-up queries discarded.");
        sb.AppendLine();
        sb.AppendLine("| Docs | Backend | Class | p50 | p95 | p99 | Total |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
        {
            foreach (string backend in backends)
            {
                var r = results[(size, backend)];
                Row(sb, size, backend, "match-all", r, x => x.MatchAll);
                Row(sb, size, backend, "single-term", r, x => x.SingleTerm);
                Row(sb, size, backend, "two-term OR", r, x => x.TwoTermOr);
                Row(sb, size, backend, "drill-sideways", r, x => x.Drill);
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Incremental update (1% of documents re-upserted)");
        sb.AppendLine();
        sb.AppendLine("Delete-by-id-term then add, the way `DefaultLuceneClient` does it (A commits the taxonomy writer too), then reopen the reader and replay all 300 faceted queries to expose any cold-cache cliff.");
        sb.AppendLine();
        sb.AppendLine("| Docs | Backend | Update + commit (ms) | Reader reopen (ms) | of which reader state (ms) | Post-reopen p50 | Post-reopen p95 |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (int size in sizes)
        {
            foreach (string backend in backends)
            {
                var r = results[(size, backend)];
                sb.AppendLine(Inv, $"| {size:N0} | {backend} | {Agg(r, x => x.UpdateMs)} | {Agg(r, x => x.ReopenMs)} | {Agg(r, x => x.StateBuildMs)} | {Agg(r, x => x.PostUpdate.P50)} | {Agg(r, x => x.PostUpdate.P95)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Caveats");
        sb.AppendLine();
        sb.AppendLine("- Synthetic corpus: a 2000-token pronounceable vocabulary with Zipf-weighted picks. Term frequencies are realistic in shape; real content has phrases, stopwords and a much longer tail.");
        sb.AppendLine("- `FSDirectory` on the local temp disk. The integration uses `CmsIODirectory`, which is the local filesystem in a dev/on-prem deployment. **Azure Blob-backed index storage is spec 13.4 and out of scope here** - it changes reader-open and directory-enumeration costs, not the relative facet-counting cost.");
        sb.AppendLine("- **The `category` top-10 is not the same work for both backends.** A returns the 5 rolled-up top-level children of the hierarchy; B returns 10 of the 125 flat `a/b/c` labels, because SortedSet faceting has no drill-down tree. This is the functional gap, and it slightly favours A in the query numbers.");
        sb.AppendLine("- B's `DefaultSortedSetDocValuesReaderState` is documented as expensive, but the measured cost scales with the number of *distinct facet labels* (183 in this corpus) and segment count, not with document count - hence the sub-millisecond figures. High-cardinality facets (thousands of taxonomy values) would move this number; more documents alone would not.");
        sb.AppendLine("- 1M documents is deferred to the spec 12 performance pass (owner decision). Both backends' counting work is O(matching docs), so the ranking is not expected to invert, but only a measurement settles it.");
        sb.AppendLine("- The fresh build is pure adds (`OpenMode.CREATE`); only the incremental pass does delete-then-add. Both backends are treated identically.");
        sb.AppendLine("- Single-threaded, no concurrent query load.");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, int size, string backend, string label, List<RunResult> r, Func<RunResult, Stats> select)
        => sb.AppendLine(Inv, $"| {size:N0} | {backend} | {label} | {Agg(r, x => select(x).P50)} | {Agg(r, x => select(x).P95)} | {Agg(r, x => select(x).P99)} | {Agg(r, x => select(x).TotalMs)} |");

    private static double Mb(long bytes) => bytes / (1024.0 * 1024);

    /// <summary>Median with the observed range, so run-to-run variance stays visible.</summary>
    private static string Agg(List<RunResult> results, Func<RunResult, double> select)
    {
        double[] values = [.. results.Select(select).Order()];
        double median = values.Length % 2 == 1
            ? values[values.Length / 2]
            : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
        return values.Length == 1
            ? median.ToString("F2", Inv)
            : string.Create(Inv, $"{median:F2} [{values[0]:F2}-{values[^1]:F2}]");
    }

    private static string LuceneVersionString() =>
        typeof(Lucene.Net.Index.IndexWriter).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Lucene.Net.Index.IndexWriter).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}

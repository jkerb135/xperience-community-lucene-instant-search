using System.Diagnostics;

namespace XpSearch.FacetSpike;

/// <summary>Latency distribution for one query class, in milliseconds.</summary>
internal readonly record struct Stats(double P50, double P95, double P99, double TotalMs)
{
    internal static Stats From(IEnumerable<double> samples)
    {
        double[] sorted = [.. samples.Order()];
        return sorted.Length == 0
            ? default
            : new Stats(Percentile(sorted, 50), Percentile(sorted, 95), Percentile(sorted, 99), sorted.Sum());
    }

    /// <summary>Nearest-rank percentile - no interpolation, so a reported value is always a measured value.</summary>
    private static double Percentile(double[] sorted, int percentile)
    {
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>Every metric from one (backend, size) pass.</summary>
internal sealed record RunResult(
    double BuildMs,
    long MainBytes,
    long TaxonomyBytes,
    double InitialOpenMs,
    Stats MatchAll,
    Stats SingleTerm,
    Stats TwoTermOr,
    Stats Drill,
    double UpdateMs,
    double ReopenMs,
    double StateBuildMs,
    Stats PostUpdate);

/// <summary>Runs the identical workload against one backend and records the metrics.</summary>
internal static class Measure
{
    internal static RunResult Run(Func<string, IFacetBackend> factory, string root, IReadOnlyList<Doc> docs)
    {
        SpikeIo.ResetDirectory(root);
        GC.Collect();
        GC.WaitForPendingFinalizers();

        using var backend = factory(root);

        var sw = Stopwatch.StartNew();
        backend.Build(docs);
        sw.Stop();
        double buildMs = sw.Elapsed.TotalMilliseconds;

        long mainBytes = backend.MainBytes;
        long taxonomyBytes = backend.TaxonomyBytes;

        GC.Collect();
        double initialOpenMs = backend.OpenReader().TotalMilliseconds;

        foreach (var query in Workload.Warmup())
        {
            backend.TopCounts(query.Query, Dims.All, 10);
        }

        GC.Collect();
        var faceted = RunFaceted(backend);

        GC.Collect();
        var drillSamples = new List<double>(100);
        foreach (var drill in Workload.Drills())
        {
            long start = Stopwatch.GetTimestamp();
            backend.DrillSideways(drill.Base, drill.Dim, drill.Value, Dims.All, 10);
            drillSamples.Add(Elapsed(start));
        }

        // "Incremental update" here means: re-upsert 1% of the corpus with unchanged content, which is
        // the integration's delete-by-id-then-add path. It is the cost of a small content publish.
        var updated = docs.Where((_, i) => i % 100 == 0).ToArray();
        GC.Collect();
        long updateStart = Stopwatch.GetTimestamp();
        backend.Upsert(updated);
        double updateMs = Elapsed(updateStart);

        var reopen = backend.OpenReader();
        double stateBuildMs = backend is SortedSetBackend sortedSet ? sortedSet.LastStateBuild.TotalMilliseconds : 0;

        GC.Collect();
        var postUpdate = RunFaceted(backend);

        return new RunResult(
            buildMs,
            mainBytes,
            taxonomyBytes,
            initialOpenMs,
            faceted[Workload.MatchAll],
            faceted[Workload.SingleTerm],
            faceted[Workload.TwoTermOr],
            Stats.From(drillSamples),
            updateMs,
            reopen.TotalMilliseconds,
            stateBuildMs,
            postUpdate["all"]);
    }

    /// <summary>Runs the 300 faceted queries; returns per-class stats plus an "all" aggregate.</summary>
    private static Dictionary<string, Stats> RunFaceted(IFacetBackend backend)
    {
        var samples = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var all = new List<double>(300);

        foreach (var query in Workload.Faceted())
        {
            long start = Stopwatch.GetTimestamp();
            backend.TopCounts(query.Query, Dims.All, 10);
            double ms = Elapsed(start);

            if (!samples.TryGetValue(query.Class, out var list))
            {
                list = [];
                samples[query.Class] = list;
            }

            list.Add(ms);
            all.Add(ms);
        }

        var stats = samples.ToDictionary(kvp => kvp.Key, kvp => Stats.From(kvp.Value), StringComparer.Ordinal);
        stats["all"] = Stats.From(all);
        return stats;
    }

    private static double Elapsed(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}

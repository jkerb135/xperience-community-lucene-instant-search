using System.Diagnostics;
using System.Globalization;

namespace XpSearch.Bench;

/// <summary>Latency distribution of one workload, in milliseconds.</summary>
internal readonly record struct Stats(double P50, double P95, double Max)
{
    internal static Stats From(IEnumerable<double> samples)
    {
        double[] sorted = [.. samples.Order()];

        return sorted.Length == 0 ? default : new Stats(Percentile(sorted, 50), Percentile(sorted, 95), sorted[^1]);
    }

    /// <summary>Nearest-rank percentile - no interpolation, so a reported value is always a measured value.</summary>
    private static double Percentile(double[] sorted, int percentile)
    {
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);

        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>One measurable thing the pipeline can be asked to do.</summary>
/// <param name="Name">Row label in the report.</param>
/// <param name="Run">Runs iteration <c>i</c>. Whatever it returns is discarded; it must not be optimized away.</param>
internal sealed record Workload(string Name, Func<int, Task<object?>> Run);

internal static class Measure
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Runs one workload <paramref name="iterations"/> times and returns its distribution.</summary>
    /// <param name="workload">The workload.</param>
    /// <param name="iterations">How many times to run it.</param>
    /// <returns>The distribution.</returns>
    internal static async Task<Stats> RunAsync(Workload workload, int iterations)
    {
        var samples = new List<double>(iterations);

        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var result = await workload.Run(i).ConfigureAwait(false);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (result is null)
            {
                throw new InvalidOperationException($"Workload '{workload.Name}' returned nothing at iteration {i}.");
            }
        }

        return Stats.From(samples);
    }

    /// <summary>Median with the observed range, so run-to-run variance stays visible.</summary>
    /// <param name="values">The per-run values.</param>
    /// <returns>The formatted cell.</returns>
    internal static string Agg(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];

        if (sorted.Length == 0)
        {
            return "-";
        }

        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;

        return sorted.Length == 1
            ? median.ToString("F2", Inv)
            : string.Create(Inv, $"{median:F2} [{sorted[0]:F2}-{sorted[^1]:F2}]");
    }
}

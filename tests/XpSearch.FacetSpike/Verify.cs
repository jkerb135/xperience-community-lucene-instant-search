namespace XpSearch.FacetSpike;

/// <summary>
/// The runnable check: A and B must produce identical facet counts before any timing is trusted.
/// Flat dimensions are compared value-for-value; <c>category</c> compares B's flat <c>a/b/c</c> labels
/// against A's leaf-path counts (A also rolls up to parents, which B structurally cannot do).
/// </summary>
internal static class Verify
{
    internal static void AssertBackendsAgree(IReadOnlyList<Doc> docs, string root)
    {
        string aRoot = Path.Combine(root, "verify-a");
        string bRoot = Path.Combine(root, "verify-b");
        SpikeIo.ResetDirectory(aRoot);
        SpikeIo.ResetDirectory(bRoot);

        using var a = new TaxonomyBackend(aRoot);
        using var b = new SortedSetBackend(bRoot);
        a.Build(docs);
        b.Build(docs);
        a.OpenReader();
        b.OpenReader();

        int queryIndex = 0;
        int comparisons = 0;
        foreach (var query in Workload.Verification())
        {
            var countsA = a.TopCounts(query, Dims.Flat, SpikeIo.AllValues);
            var countsB = b.TopCounts(query, Dims.Flat, SpikeIo.AllValues);

            foreach (string dim in Dims.Flat)
            {
                AssertEqual($"query#{queryIndex} dim={dim}", countsA[dim], countsB[dim]);
                comparisons += countsA[dim].Count;
            }

            var leafA = a.CategoryLeafCounts(query);
            var leafB = b.CategoryLeafCounts(query);
            AssertEqual($"query#{queryIndex} dim=category(leaf)", leafA, leafB);
            comparisons += leafA.Count;

            queryIndex++;
        }

        Console.WriteLine(
            $"CORRECTNESS: A and B agree on {comparisons} facet counts across {queryIndex} queries " +
            $"({docs.Count} docs, dims: {string.Join(", ", Dims.Flat)}, category leaf paths). PASS");
    }

    private static void AssertEqual(string context, Dictionary<string, int> expected, Dictionary<string, int> actual)
    {
        var mismatches = expected.Keys.Union(actual.Keys, StringComparer.Ordinal)
            .Where(key => Get(expected, key) != Get(actual, key))
            .Select(key => $"{key}: A={Get(expected, key)} B={Get(actual, key)}")
            .Take(10)
            .ToArray();

        if (mismatches.Length > 0)
        {
            throw new InvalidOperationException(
                $"FACET COUNT MISMATCH at {context}: {string.Join("; ", mismatches)}");
        }
    }

    private static int Get(Dictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out int value) ? value : 0;
}

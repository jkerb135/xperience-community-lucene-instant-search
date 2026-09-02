using System.Globalization;
using System.Text;

namespace XpSearch.Bench;

/// <summary>One synthetic document, before it becomes a Lucene document.</summary>
internal sealed record BenchDocument(
    string Id,
    string Title,
    string Body,
    string ContentType,
    string Language,
    string Url,
    string Section,
    IReadOnlyList<string> Topics,
    double Price);

/// <summary>
/// The deterministic synthetic corpus. One fixed seed produces the same documents every run and at
/// every size, so a 10k number and a 1M number describe the same content shape.
/// </summary>
/// <remarks>
/// Documents are yielded lazily: a million bodies materialized at once is roughly a gigabyte of
/// strings, and the index writer only ever needs one at a time.
/// </remarks>
internal static class Corpus
{
    internal const int Seed = 42;

    /// <summary>Vocabulary size. Term frequencies follow a Zipf curve over these, as natural language does.</summary>
    private const int VocabularySize = 5_000;

    /// <summary>Resolution of the O(1) Zipf lookup table.</summary>
    private const int ZipfBuckets = 1 << 16;

    /// <summary>Facet dimension with few values, like a content section.</summary>
    private const int SectionCardinality = 10;

    /// <summary>Facet dimension with many values, like a tag taxonomy.</summary>
    private const int TopicCardinality = 1_000;

    /// <summary>Share of documents carrying one of <see cref="HotTerms"/>.</summary>
    private const double HotShare = 0.02;

    private static readonly string[] Prefixes = ["ba", "ce", "di", "fo", "gu", "he", "ji", "ko", "lu", "ma", "ne", "pi", "ro", "sa", "te", "vu", "wa", "xe", "yo", "zi"];
    private static readonly string[] Middles = ["", "br", "cl", "dr", "fl", "gr", "kl", "mn", "pr", "st", "tr", "vl"];
    private static readonly string[] Suffixes = ["an", "el", "ic", "on", "us", "ar", "ing", "ent", "ion", "ate", "ly", "or"];

    /// <summary>The Zipf-ranked vocabulary: index 0 is the most frequent term.</summary>
    internal static string[] Vocabulary { get; } = BuildVocabulary();

    /// <summary>
    /// Terms that appear in about 2% of documents and nowhere else, so a query has a predictable,
    /// non-trivial match count that is neither "everything" nor "almost nothing".
    /// </summary>
    internal static string[] HotTerms { get; } = ["quarterlyreview", "onboardingkit", "fieldguide", "pressrelease", "casestudy"];

    internal static string[] ContentTypes { get; } = ["Article", "Product", "Landing", "Event"];

    internal static string[] Languages { get; } = ["en", "de", "fr"];

    internal static string Section(int i) => string.Create(CultureInfo.InvariantCulture, $"section-{i:00}");

    internal static string Topic(int i) => string.Create(CultureInfo.InvariantCulture, $"topic-{i:000}");

    /// <summary>Generates <paramref name="count"/> documents. The same count always yields the same documents.</summary>
    /// <param name="count">How many documents to generate.</param>
    /// <returns>The documents, lazily.</returns>
    internal static IEnumerable<BenchDocument> Generate(long count)
    {
        var random = new Random(Seed);
        int[] zipf = BuildZipfTable();
        var body = new StringBuilder(4_000);
        var title = new StringBuilder(80);

        for (long i = 0; i < count; i++)
        {
            title.Clear();
            for (int w = 0; w < 3 + random.Next(4); w++)
            {
                if (w > 0)
                {
                    title.Append(' ');
                }

                title.Append(Vocabulary[zipf[random.Next(ZipfBuckets)]]);
            }

            // Skewed short: the cube of a uniform draw puts the mass near the 50-word floor and
            // leaves a thin tail of long documents, which is what a real content tree looks like.
            double u = random.NextDouble();
            int words = 50 + (int)(450 * u * u * u);

            body.Clear();
            for (int w = 0; w < words; w++)
            {
                if (w > 0)
                {
                    body.Append(' ');
                }

                body.Append(Vocabulary[zipf[random.Next(ZipfBuckets)]]);
            }

            if (random.NextDouble() < HotShare)
            {
                body.Append(' ').Append(HotTerms[random.Next(HotTerms.Length)]);
            }

            // Facet dimensions are skewed too: Zipf over the value index, so the top values carry
            // most of the documents and the tail is thin - the shape that makes top-N facet
            // counting interesting.
            string section = Section(zipf[random.Next(ZipfBuckets)] % SectionCardinality);
            var topics = new string[1 + random.Next(3)];
            for (int t = 0; t < topics.Length; t++)
            {
                topics[t] = Topic(zipf[random.Next(ZipfBuckets)] % TopicCardinality);
            }

            yield return new BenchDocument(
                string.Create(CultureInfo.InvariantCulture, $"doc-{i}:en"),
                title.ToString(),
                body.ToString(),
                ContentTypes[zipf[random.Next(ZipfBuckets)] % ContentTypes.Length],
                Languages[zipf[random.Next(ZipfBuckets)] % Languages.Length],
                string.Create(CultureInfo.InvariantCulture, $"/bench/{i}"),
                section,
                topics,
                Math.Round(random.NextDouble() * 1000, 2));
        }
    }

    /// <summary>
    /// Maps a uniform bucket draw to a Zipf-distributed term rank in O(1). A binary search over the
    /// cumulative weights would be correct too, but this table is drawn hundreds of millions of times
    /// while building the 1M corpus.
    /// </summary>
    private static int[] BuildZipfTable()
    {
        double total = 0;
        for (int i = 0; i < VocabularySize; i++)
        {
            total += 1.0 / (i + 1);
        }

        var table = new int[ZipfBuckets];
        double cumulative = 0;
        int bucket = 0;

        for (int rank = 0; rank < VocabularySize && bucket < ZipfBuckets; rank++)
        {
            cumulative += 1.0 / ((rank + 1) * total);
            int upTo = Math.Min(ZipfBuckets, (int)(cumulative * ZipfBuckets));

            while (bucket < upTo)
            {
                table[bucket++] = rank;
            }
        }

        while (bucket < ZipfBuckets)
        {
            table[bucket++] = VocabularySize - 1;
        }

        return table;
    }

    private static string[] BuildVocabulary()
    {
        var random = new Random(Seed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var terms = new List<string>(VocabularySize);

        while (terms.Count < VocabularySize)
        {
            string term = Prefixes[random.Next(Prefixes.Length)]
                + Middles[random.Next(Middles.Length)]
                + Prefixes[random.Next(Prefixes.Length)]
                + Suffixes[random.Next(Suffixes.Length)];

            if (seen.Add(term))
            {
                terms.Add(term);
            }
        }

        return [.. terms];
    }
}

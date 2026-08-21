using System.Globalization;

namespace XpSearch.FacetSpike;

/// <summary>One synthetic document. <see cref="Category"/> is a 3-level hierarchical path.</summary>
internal sealed record Doc(
    string Id,
    string Title,
    string Content,
    string ContentType,
    string Language,
    string[] Tags,
    string[] Category);

/// <summary>
/// Deterministic synthetic corpus: <c>Random(42)</c>, a fixed 2000-word vocabulary and Zipf-weighted
/// term/tag selection so term frequencies (and therefore facet-count work) are not uniform.
/// </summary>
internal static class Corpus
{
    internal const int VocabularySize = 2000;

    /// <summary>Content types with the skew required by the spike brief (60/20/10/7/3%).</summary>
    internal static readonly string[] ContentTypes = ["article", "page", "product", "event", "faq"];
    // Cumulative form of the required 60/20/10/7/3% skew.
    private static readonly double[] ContentTypeWeights = [0.60, 0.80, 0.90, 0.97, 1.00];

    internal static readonly string[] Languages = ["en-US", "de-DE", "fr-FR"];

    internal static readonly string[] Tags = BuildTags();
    internal static readonly string[] Vocabulary = BuildVocabulary();

    private static readonly double[] ZipfVocabulary = BuildZipf(VocabularySize);
    private static readonly double[] ZipfTags = BuildZipf(50);

    /// <summary>Every category leaf path, in <c>a/b/c</c> order. Used by the A/B correctness proof.</summary>
    internal static IEnumerable<string[]> CategoryPaths()
    {
        for (int a = 0; a < 5; a++)
        {
            for (int b = 0; b < 5; b++)
            {
                for (int c = 0; c < 5; c++)
                {
                    yield return [Level(0, a), Level(1, b), Level(2, c)];
                }
            }
        }
    }

    private static string Level(int level, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"l{level}v{index}");

    internal static IReadOnlyList<Doc> Generate(int count)
    {
        var random = new Random(42);
        var docs = new Doc[count];

        for (int i = 0; i < count; i++)
        {
            int titleWords = random.Next(3, 9);
            int contentWords = random.Next(50, 201);

            docs[i] = new Doc(
                Id: string.Create(CultureInfo.InvariantCulture, $"doc-{i:D7}"),
                Title: Words(random, titleWords),
                Content: Words(random, contentWords),
                ContentType: ContentTypes[PickWeighted(random, ContentTypeWeights)],
                Language: Languages[random.Next(Languages.Length)],
                Tags: PickTags(random),
                Category: [Level(0, random.Next(5)), Level(1, random.Next(5)), Level(2, random.Next(5))]);
        }

        return docs;
    }

    private static string Words(Random random, int count)
    {
        var sb = new System.Text.StringBuilder(count * 7);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(Vocabulary[PickWeighted(random, ZipfVocabulary)]);
        }

        return sb.ToString();
    }

    private static string[] PickTags(Random random)
    {
        int n = random.Next(0, 5);
        if (n == 0)
        {
            return [];
        }

        // Distinct: a duplicated value in a multi-valued dimension would be counted once by both
        // backends, but keeping them distinct makes the corpus unambiguous.
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            chosen.Add(Tags[PickWeighted(random, ZipfTags)]);
        }

        return [.. chosen];
    }

    /// <summary>Picks an index from a monotonically increasing cumulative-weight array.</summary>
    private static int PickWeighted(Random random, double[] weights)
    {
        double target = random.NextDouble() * weights[^1];
        int lo = 0;
        int hi = weights.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (weights[mid] < target)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static double[] BuildZipf(int n)
    {
        var cumulative = new double[n];
        double running = 0;
        for (int i = 0; i < n; i++)
        {
            running += 1.0 / (i + 1);
            cumulative[i] = running;
        }

        return cumulative;
    }

    private static string[] BuildVocabulary()
    {
        // 20 * 5 * 20 == 2000 unique, pronounceable, analyzer-friendly tokens.
        string[] onsets = ["b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z", "br", "st"];
        string[] vowels = ["a", "e", "i", "o", "u"];
        string[] codas = ["b", "ck", "d", "ft", "g", "l", "lm", "m", "n", "nd", "ng", "nt", "p", "r", "rk", "s", "sk", "st", "t", "x"];

        var words = new string[VocabularySize];
        int w = 0;
        foreach (string onset in onsets)
        {
            foreach (string vowel in vowels)
            {
                foreach (string coda in codas)
                {
                    words[w++] = onset + vowel + coda;
                }
            }
        }

        return words;
    }

    private static string[] BuildTags()
    {
        var tags = new string[50];
        for (int i = 0; i < tags.Length; i++)
        {
            tags[i] = string.Create(CultureInfo.InvariantCulture, $"tag-{i:D2}");
        }

        return tags;
    }
}

using System.Globalization;
using System.Text;

using XpSearch.Core.Analytics;

namespace XpSearch.Admin.UIPages.Analytics;

/// <summary>What the dashboard asks for (spec §9.3).</summary>
public sealed class AnalyticsRequest
{
    /// <summary>Gets or sets the code name of the index, or an empty string for every index.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the first day of the range, as <c>yyyy-MM-dd</c> in UTC.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets the last day of the range, inclusive, as <c>yyyy-MM-dd</c> in UTC.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Gets or sets how many rows each top-N list holds. Clamped server-side to 1..100.</summary>
    public int Limit { get; set; } = 20;
}

/// <summary>Which zero-result query a "Create rule" action was invoked for.</summary>
public sealed class CreateRuleRequest
{
    /// <summary>Gets or sets the code name of the index the rule will apply to.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the query text to pre-fill as the rule's pattern.</summary>
    public string Query { get; set; } = string.Empty;
}

/// <summary>One row of the top queries and slowest queries reports.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="P95ProcessingTimeMs">The 95th percentile of its processing time; zero outside the slowest queries report.</param>
public sealed record QueryRow(string Query, int Volume, int P95ProcessingTimeMs);

/// <summary>One row of the zero-result queries report.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="LastSeen">When it was last searched for, as <c>yyyy-MM-dd</c> in UTC.</param>
public sealed record ZeroResultRow(string Query, int Volume, string LastSeen);

/// <summary>One row of the click-through report.</summary>
/// <param name="Query">The normalized query text.</param>
/// <param name="Volume">How many times it was searched for.</param>
/// <param name="Clicks">How many of those searches led to a click.</param>
/// <param name="ClickThroughRate">Clicks divided by volume, between zero and one.</param>
/// <param name="AverageClickedPosition">Mean clicked position, or <see langword="null"/> when nothing was clicked.</param>
public sealed record ClickThroughRow(string Query, int Volume, int Clicks, double ClickThroughRate, double? AverageClickedPosition);

/// <summary>One point of the search volume chart.</summary>
/// <param name="Day">The day, as <c>yyyy-MM-dd</c> in UTC.</param>
/// <param name="Volume">How many searches ran that day.</param>
public sealed record VolumePoint(string Day, int Volume);

/// <summary>Everything the dashboard renders (spec §9.3).</summary>
/// <param name="TopQueries">The most searched queries.</param>
/// <param name="ZeroResultQueries">The most searched queries that found nothing.</param>
/// <param name="ClickThrough">Click-through rate per query.</param>
/// <param name="AverageClickedPosition">Mean clicked position across the range, or <see langword="null"/>.</param>
/// <param name="VolumeOverTime">Searches per day, oldest first.</param>
/// <param name="SlowestQueries">The queries with the highest 95th percentile processing time.</param>
/// <param name="TotalSearches">How many searches the range holds.</param>
/// <param name="Error">A message to show instead of the reports, or an empty string when the load succeeded.</param>
public sealed record AnalyticsReportDto(
    IReadOnlyList<QueryRow> TopQueries,
    IReadOnlyList<ZeroResultRow> ZeroResultQueries,
    IReadOnlyList<ClickThroughRow> ClickThrough,
    double? AverageClickedPosition,
    IReadOnlyList<VolumePoint> VolumeOverTime,
    IReadOnlyList<QueryRow> SlowestQueries,
    int TotalSearches,
    string Error)
{
    /// <summary>The date format the dashboard exchanges days in.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Gets an empty report carrying a message.</summary>
    /// <param name="message">What to tell the user.</param>
    /// <returns>The report.</returns>
    public static AnalyticsReportDto Failed(string message) => new([], [], [], null, [], [], 0, message);

    /// <summary>Maps the service's report onto what the client renders.</summary>
    /// <param name="report">The report the analytics service produced.</param>
    /// <returns>The client-facing report.</returns>
    public static AnalyticsReportDto From(SearchAnalyticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new AnalyticsReportDto(
            [.. report.TopQueries.Select(row => new QueryRow(row.Query, row.Volume, 0))],
            [.. report.ZeroResultQueries.Select(row => new ZeroResultRow(
                row.Query,
                row.Volume,
                row.LastSeen.ToString(DateFormat, CultureInfo.InvariantCulture)))],
            [.. report.ClickThrough.Select(row => new ClickThroughRow(
                row.Query,
                row.Volume,
                row.Clicks,
                row.ClickThroughRate,
                row.AverageClickedPosition))],
            report.AverageClickedPosition,
            [.. report.VolumeOverTime.Select(point => new VolumePoint(
                point.Day.ToString(DateFormat, CultureInfo.InvariantCulture),
                point.Volume))],
            [.. report.SlowestQueries.Select(row => new QueryRow(row.Query, row.Volume, row.P95ProcessingTimeMs))],
            report.TotalSearches,
            string.Empty);
    }
}

/// <summary>
/// The value a zero-result row's "Create rule" action carries to the pre-filled rule create page.
/// </summary>
/// <remarks>
/// A UI page can only be handed a value through a parameterized URL slug
/// (https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages#parameterized-url-slugs),
/// and a slug is one URL segment. A visitor's query is arbitrary text - spaces, slashes, accents -
/// so the index and the query travel base64url-encoded, which is exactly the character set a slug
/// allows.
/// </remarks>
public static class RuleSeed
{
    private const char Separator = '\n';

    /// <summary>Encodes the index and query into one URL slug segment.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The encoded value.</returns>
    public static string Encode(string indexName, string query) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{indexName}{Separator}{query}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>Decodes a slug segment produced by <see cref="Encode"/>.</summary>
    /// <param name="value">The encoded value.</param>
    /// <returns>The index code name and the query. Both are empty when the value is not decodable.</returns>
    public static (string IndexName, string Query) Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return (string.Empty, string.Empty);
        }

        string decoded;

        string padded = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return (string.Empty, string.Empty);
        }

        int separator = decoded.IndexOf(Separator);

        return separator < 0
            ? (string.Empty, decoded)
            : (decoded[..separator], decoded[(separator + 1)..]);
    }
}

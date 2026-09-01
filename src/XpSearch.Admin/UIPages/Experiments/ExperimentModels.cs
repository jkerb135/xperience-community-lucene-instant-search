using Kentico.Xperience.Admin.Base;

using XpSearch.Core.Analytics;

namespace XpSearch.Admin.UIPages.Experiments;

/// <summary>Initial state of the experiment detail client template.</summary>
public class ExperimentDetailClientProperties : TemplateClientProperties
{
    /// <summary>Gets or sets the code name of the index the experiment tests.</summary>
    public string IndexName { get; set; } = string.Empty;

    /// <summary>Gets or sets the smallest share of traffic variant B can get.</summary>
    public int MinSplit { get; set; }

    /// <summary>Gets or sets the largest share of traffic variant B can get.</summary>
    public int MaxSplit { get; set; }
}

/// <summary>What the detail page's split editor submits.</summary>
public sealed class SplitRequest
{
    /// <summary>Gets or sets the percentage of traffic for variant B.</summary>
    public int SplitPercent { get; set; }
}

/// <summary>Which way a running experiment is being concluded.</summary>
public sealed class ConcludeRequest
{
    /// <summary>Gets or sets a value indicating whether variant B replaces the live tuning.</summary>
    public bool Promote { get; set; }
}

/// <summary>
/// One variant's side of the comparison. Observed rates over the searches that variant answered -
/// nothing here is a significance claim (amendment: honest samples, no fabricated significance).
/// </summary>
/// <param name="Variant">The variant, <c>A</c> or <c>B</c>.</param>
/// <param name="Searches">How many searches the variant answered.</param>
/// <param name="ZeroResultSearches">How many of those found nothing.</param>
/// <param name="Clicks">How many of those led to a click.</param>
/// <param name="AverageClickedPosition">Mean clicked position, or <see langword="null"/> when nothing was clicked.</param>
public sealed record VariantStatsDto(
    string Variant,
    int Searches,
    int ZeroResultSearches,
    int Clicks,
    double? AverageClickedPosition)
{
    /// <summary>An empty side, for a draft that has not run yet or a report that failed.</summary>
    /// <param name="variant">The variant.</param>
    /// <returns>The side.</returns>
    public static VariantStatsDto Empty(string variant) => new(variant, 0, 0, 0, null);

    /// <summary>Reads one variant's totals out of the report the analytics service produced.</summary>
    /// <param name="variant">The variant.</param>
    /// <param name="report">The report, already scoped to the experiment and the variant.</param>
    /// <returns>The side.</returns>
    public static VariantStatsDto From(string variant, SearchAnalyticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new VariantStatsDto(
            variant,
            report.TotalSearches,
            report.ZeroResultSearches,
            report.Clicks,
            report.AverageClickedPosition);
    }
}

/// <summary>Everything the experiment detail page renders.</summary>
/// <param name="Name">What the editor called the experiment.</param>
/// <param name="State">Draft, Running or Concluded.</param>
/// <param name="Outcome">Promoted, Discarded, or an empty string while it is not over.</param>
/// <param name="SplitPercent">Percentage of traffic for variant B.</param>
/// <param name="Started">When it started splitting traffic, or an empty string.</param>
/// <param name="Ended">When it was concluded, or an empty string.</param>
/// <param name="A">The live tuning's side of the comparison.</param>
/// <param name="B">The draft tuning's side of the comparison.</param>
/// <param name="Error">A message to show instead of the report, or an empty string.</param>
public sealed record ExperimentReportDto(
    string Name,
    string State,
    string Outcome,
    int SplitPercent,
    string Started,
    string Ended,
    VariantStatsDto A,
    VariantStatsDto B,
    string Error)
{
    /// <summary>Gets a report carrying nothing but a message.</summary>
    /// <param name="message">What to tell the user.</param>
    /// <returns>The report.</returns>
    public static ExperimentReportDto Failed(string message) =>
        new(string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, VariantStatsDto.Empty("A"), VariantStatsDto.Empty("B"), message);
}

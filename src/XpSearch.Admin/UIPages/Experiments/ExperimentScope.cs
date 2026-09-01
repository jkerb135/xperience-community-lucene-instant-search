using System.Globalization;

using Kentico.Xperience.Admin.Base;

using XpSearch.Admin.Persistence;
using XpSearch.Admin.Tuning;
using XpSearch.Core.Tuning;

namespace XpSearch.Admin.UIPages.Experiments;

/// <summary>
/// Resolution of the experiment a variant-B page is scoped to, and the banner every one of those pages
/// shows (XP-1). The live tuning pages never go through here.
/// </summary>
public static class ExperimentScope
{
    /// <summary>The message a save of a row belonging to another variant is refused with.</summary>
    /// <remarks>
    /// Without it, saving a draft row through a live editor's URL would clear its experiment reference
    /// and promote it into the live tuning behind the editor's back.
    /// </remarks>
    public const string CrossVariantRefusal = "This record belongs to a different tuning variant and was not saved.";

    /// <summary>The message a delete of a row belonging to another variant is refused with.</summary>
    public const string CrossVariantDeleteRefusal = "This record belongs to a different tuning variant and was not deleted.";

    /// <summary>
    /// The message a write to a started experiment's variant B is refused with. Changing the draft while
    /// half the visitors are being served from it would invalidate everything measured so far.
    /// </summary>
    public const string FrozenRefusal = "This experiment has started, so its variant B can no longer be edited. Conclude the experiment first.";

    /// <summary>The format the detail page exchanges moments in, in UTC.</summary>
    public const string MomentFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Builds the URL parameter values every link to a page inside an experiment needs.</summary>
    /// <param name="indexIdentifier">Identifier of the index, from the URL.</param>
    /// <param name="experimentId">Identifier of the experiment, from the URL.</param>
    /// <returns>The parameter values, in the order the two parameterized slugs appear in the URL.</returns>
    public static PageParameterValues Route(int indexIdentifier, int experimentId) =>
        new()
        {
            { typeof(IndexTuningSection), indexIdentifier },
            { typeof(ExperimentSection), experimentId }
        };

    /// <summary>Reads the experiment in the URL, without trusting that it belongs to the index in the URL.</summary>
    /// <param name="catalog">Reads stored experiments.</param>
    /// <param name="experimentId">Identifier from the URL.</param>
    /// <param name="indexName">Code name the URL's index resolves to.</param>
    /// <returns>The experiment, or <see langword="null"/> when there is none or it belongs elsewhere.</returns>
    public static ExperimentSummary? Resolve(IExperimentCatalog catalog, int experimentId, string indexName)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var row = catalog.Get(experimentId);

        return row is not null && IndexScope.Matches(row.IndexName, indexName) ? row : null;
    }

    /// <summary>Tells whether an experiment's variant-B tuning may still be edited.</summary>
    /// <param name="row">The experiment, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> only while it is a draft: editing a running test would corrupt it.</returns>
    public static bool IsDraft(ExperimentSummary? row) => row is { State: ExperimentState.Draft };

    /// <summary>The variant a page scoped to an experiment reads and writes.</summary>
    /// <param name="experimentId">Identifier of the experiment, or zero.</param>
    /// <returns>The variant.</returns>
    public static TuningVariant Variant(int experimentId) =>
        experimentId > 0 ? new TuningVariant(experimentId) : TuningVariant.Live;

    /// <summary>
    /// The banner every variant-B editor carries, so nobody edits the draft thinking it is live, or the
    /// other way round.
    /// </summary>
    /// <param name="row">The experiment the page is scoped to, or <see langword="null"/> when it is gone.</param>
    /// <returns>The callout to put on the page.</returns>
    public static CalloutConfiguration Banner(ExperimentSummary? row) =>
        new()
        {
            Headline = row is null ? "Variant B draft — experiment not found" : $"Variant B draft — {row.DisplayName}",
            Content = BannerContent(row),
            Type = IsDraft(row) ? CalloutType.QuickTip : CalloutType.FriendlyWarning,
            Placement = CalloutPlacement.OnDesk
        };

    /// <summary>The words on the banner, which differ once the experiment has started.</summary>
    /// <param name="row">The experiment, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    public static string BannerContent(ExperimentSummary? row) =>
        row is null
            ? "This experiment no longer exists. Nothing edited here applies to anyone."
            : IsDraft(row)
                ? "You are editing the experiment's variant B, not the live tuning of the index. Visitors see these rows only once the experiment is started, and only if they are bucketed into B."
                : $"This experiment is {Label(row.State).ToLowerInvariant()}, so its variant B is read-only. Editing it now would change the test half-way through and make its numbers meaningless.";

    /// <summary>The word the admin uses for a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The label.</returns>
    public static string Label(ExperimentState state) => state.ToString();

    /// <summary>The word the admin uses for an outcome.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The label, or an empty string while the experiment is not over.</returns>
    public static string Label(ExperimentOutcome outcome) =>
        outcome == ExperimentOutcome.None ? string.Empty : outcome.ToString();

    /// <summary>Formats a stored moment for the client.</summary>
    /// <param name="moment">The moment, in UTC.</param>
    /// <returns>The formatted moment, or an empty string when there is none.</returns>
    public static string Moment(DateTime? moment) =>
        moment?.ToString(MomentFormat, CultureInfo.InvariantCulture) ?? string.Empty;
}

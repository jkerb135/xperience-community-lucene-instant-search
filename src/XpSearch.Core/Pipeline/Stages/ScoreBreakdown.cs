using Lucene.Net.Search;

namespace XpSearch.Core.Pipeline.Stages;

/// <summary>
/// Explains the score checkpoints of one document into its score steps (QT-2). Shared by
/// <see cref="ScoreBreakdownStage"/>, which runs it for every document on the page, and by
/// <see cref="PinnedAndBuriedStage"/>, which runs it for a document a pin injects (QT-3) so an
/// injected result reads the same way as one the query returned.
/// </summary>
internal static class ScoreBreakdown
{
    private const double Same = 1e-6;

    /// <summary>Builds the score steps of one document.</summary>
    /// <param name="searcher">A leased searcher over the index the document lives in.</param>
    /// <param name="context">The search context, whose checkpoints are explained and whose applied rules a boost is recorded on.</param>
    /// <param name="id">The result id, which applied rules are keyed by.</param>
    /// <param name="docId">The Lucene document id to explain against.</param>
    /// <returns>One step per checkpoint that changed this document's score, in order.</returns>
    internal static List<ScoreStep> StepsFor(IndexSearcher searcher, SearchContext context, string id, int docId)
    {
        var steps = new List<ScoreStep>();

        foreach (var checkpoint in context.ScoreCheckpoints)
        {
            double score = searcher.Explain(checkpoint.Query, docId).Value;
            double previous = steps.Count == 0 ? double.NaN : steps[^1].Score;

            // A stage that did not touch this document is not a step of its story; the first
            // one is, because it is the score everything else is measured against.
            if (steps.Count > 0 && Math.Abs(score - previous) < Same)
            {
                continue;
            }

            steps.Add(new ScoreStep(checkpoint.Stage, score, checkpoint.RuleId));

            // A boost adds a SHOULD clause, which lowers the score of every document that
            // does not match it (Lucene's coordination factor). That is a real step of the
            // score - it is in the executed query - but the rule did not boost this
            // document, so it is not one of the rules that touched it.
            if (checkpoint.RuleId is { } ruleId
                && score > previous
                && context.Tuning.Rules.FirstOrDefault(rule => rule.Id == ruleId) is { } applied)
            {
                context.RecordAppliedRule(id, applied, "boost");
            }
        }

        return steps;
    }
}

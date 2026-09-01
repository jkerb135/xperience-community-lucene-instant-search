using System.Security.Cryptography;
using System.Text;

using XpSearch.Core.Tuning;

namespace XpSearch.Core.Experiments;

/// <summary>Which half of a running experiment a visitor was bucketed into (XP-1).</summary>
public enum SearchVariant
{
    /// <summary>The live tuning of the index.</summary>
    A,

    /// <summary>The experiment's draft tuning.</summary>
    B
}

/// <summary>The one experiment that is running on an index, as the pipeline needs to know it.</summary>
/// <param name="Id">Database identifier of the experiment; the variant-B tuning rows carry it.</param>
/// <param name="Guid">Object GUID, hashed with the visitor's bucket id so two experiments split differently.</param>
/// <param name="SplitPercent">Percentage of traffic sent to variant B, 1 to 99.</param>
public sealed record RunningExperiment(int Id, Guid Guid, int SplitPercent);

/// <summary>
/// What a running experiment did to one search request: which experiment applied and which variant
/// the visitor was bucketed into.
/// </summary>
/// <param name="ExperimentId">Database identifier of the experiment, or zero when none is running.</param>
/// <param name="Variant">The variant the visitor was bucketed into.</param>
/// <remarks>
/// The variant is expressed as a <see cref="TuningVariant"/> rather than as an index or an analyzer,
/// so a later "whole-index variant" (amendment, out of scope for XP-1) can widen this record without
/// touching the stages that read it.
/// </remarks>
public sealed record ExperimentAssignment(int ExperimentId, SearchVariant Variant)
{
    /// <summary>Gets the answer for an index with no running experiment: live tuning, nothing stamped.</summary>
    public static ExperimentAssignment None { get; } = new(0, SearchVariant.A);

    /// <summary>Gets a value indicating whether a running experiment applied to the request.</summary>
    public bool IsActive => ExperimentId > 0;

    /// <summary>Gets the tuning rows the request must be answered from.</summary>
    public TuningVariant Tuning => IsActive && Variant == SearchVariant.B ? new TuningVariant(ExperimentId) : TuningVariant.Live;
}

/// <summary>
/// Buckets a visitor into a variant: stable for the visitor, reproducible on any server, and stored
/// nowhere but in the visitor's own cookie (XP-1).
/// </summary>
public static class ExperimentBucketing
{
    /// <summary>Name of the first-party cookie holding the visitor's bucket id.</summary>
    /// <remarks>
    /// Registered at <c>CookieLevel.Essential</c> by <c>AddXpSearch</c> - Xperience has no "functional"
    /// level, and Essential is the level that means "cookies I may need, but do not track me"
    /// (https://docs.kentico.com/documentation/developers-and-admins/data-protection/cookies). An
    /// experiment therefore runs without consent to tracking, unlike a search activity.
    /// </remarks>
    public const string CookieName = "xpsearch_bucket";

    /// <summary>How long the bucket cookie lives. Long enough that an experiment outlives a visitor's session.</summary>
    public static TimeSpan CookieLifetime { get; } = TimeSpan.FromDays(365);

    /// <summary>Creates a bucket id for a visitor who does not have one yet.</summary>
    /// <returns>An opaque random identifier. It says nothing about the visitor, which is why no consent to tracking is needed.</returns>
    public static string NewBucketId() => Guid.NewGuid().ToString("N");

    /// <summary>Buckets one visitor into one experiment.</summary>
    /// <param name="bucketId">The visitor's bucket id, from their cookie.</param>
    /// <param name="experimentGuid">GUID of the experiment, so a visitor in B of one experiment is not in B of every experiment.</param>
    /// <param name="splitPercent">Percentage of traffic that belongs in B.</param>
    /// <returns>The variant, the same one for the same arguments on any server at any time.</returns>
    public static SearchVariant Variant(string bucketId, Guid experimentGuid, int splitPercent) =>
        Bucket(bucketId, experimentGuid) < splitPercent ? SearchVariant.B : SearchVariant.A;

    /// <summary>The visitor's position on the 0-99 line for one experiment.</summary>
    /// <param name="bucketId">The visitor's bucket id.</param>
    /// <param name="experimentGuid">GUID of the experiment.</param>
    /// <returns>A number between 0 and 99.</returns>
    /// <remarks>
    /// SHA-256 rather than <see cref="string.GetHashCode()"/>: string hashing is randomized per
    /// process, which would rebucket every visitor on every restart and on every other server.
    /// </remarks>
    public static int Bucket(string bucketId, Guid experimentGuid)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{bucketId}:{experimentGuid:N}"));

        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
    }
}

/// <summary>
/// Where the pipeline learns which experiment is running on an index. Core ships an implementation
/// that answers "none", so search works without <c>XpSearch.Admin</c> installed; the Admin package
/// replaces it with the cached, database-backed one.
/// </summary>
public interface IRunningExperimentSource
{
    /// <summary>Gets the running experiment of an index.</summary>
    /// <param name="indexName">Code name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The experiment, or <see langword="null"/> when none is running.</returns>
    Task<RunningExperiment?> GetRunningExperimentAsync(string indexName, CancellationToken cancellationToken);
}

/// <summary>The default source: no index is ever experimenting.</summary>
public sealed class NoRunningExperimentSource : IRunningExperimentSource
{
    /// <inheritdoc />
    public Task<RunningExperiment?> GetRunningExperimentAsync(string indexName, CancellationToken cancellationToken) =>
        Task.FromResult<RunningExperiment?>(null);
}

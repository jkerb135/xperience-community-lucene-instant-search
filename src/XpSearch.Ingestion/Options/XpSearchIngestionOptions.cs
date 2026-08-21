using XpSearch.Core.Abstractions;

namespace XpSearch.Ingestion.Options;

/// <summary>
/// Per-index ingestion settings, reachable through <see cref="XpSearchIngestionOptions.Indexes"/>.
/// They are the configuration fallback for what spec §10.3 asks to be declared on the strategy class
/// with <c>XpSearchFieldAttribute</c>; anything set here wins over the attributes.
/// </summary>
public sealed class XpSearchIngestionIndexOptions
{
    /// <summary>Gets the fields the index accepts, in addition to the detected and declared ones.</summary>
    public IList<SchemaField> Fields { get; } = [];

    /// <summary>
    /// Gets or sets whether a pushed document may carry attributes the schema does not declare.
    /// Leave it unset to use the strategy class's declaration, which itself defaults to <c>false</c>.
    /// </summary>
    public bool? AllowDynamicFields { get; set; }
}

/// <summary>
/// Configuration of the ingestion API, bound through <c>services.AddXpSearchIngestion(o =&gt; ...)</c>.
/// </summary>
public sealed class XpSearchIngestionOptions
{
    /// <summary>Gets or sets the cap on documents per upsert request (spec §10.2). Defaults to 1000.</summary>
    public int MaxDocumentsPerRequest { get; set; } = 1_000;

    /// <summary>Gets or sets the cap on the body size of an upsert request in bytes (spec §10.2). Defaults to 10 MB.</summary>
    public long MaxRequestBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>Gets or sets the source a pushed document with no <c>_source</c> is stored under. Defaults to <c>external</c>.</summary>
    public string DefaultSource { get; set; } = "external";

    /// <summary>Gets or sets how many ingestion requests one API key may make per window. Defaults to 60.</summary>
    public int RateLimitPermitsPerWindow { get; set; } = 60;

    /// <summary>Gets or sets the rate limiting window. Defaults to one minute.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets how often a key's last-used timestamp is written back. Defaults to five minutes:
    /// the timestamp exists for the admin listing, and a write per request would be pure overhead.
    /// </summary>
    public TimeSpan KeyLastUsedThrottle { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long the rebuild replay waits for the integration's own queue to publish the
    /// rebuilt index before writing the external documents back (spec §10.2). Defaults to two minutes.
    /// </summary>
    public TimeSpan ReplayTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets the per-index settings, keyed by index code name (case-insensitive).</summary>
    public IDictionary<string, XpSearchIngestionIndexOptions> Indexes { get; } =
        new Dictionary<string, XpSearchIngestionIndexOptions>(StringComparer.OrdinalIgnoreCase);
}

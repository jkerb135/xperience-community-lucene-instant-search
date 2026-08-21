using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using XpSearch.Ingestion.Options;

namespace XpSearch.Ingestion.Security;

/// <summary>
/// What an API key is allowed to do (spec §10.4), serialized to the key's <c>KeyScopes</c> column as
/// <c>{"indexes":["products"],"ops":["write","delete"]}</c>.
/// </summary>
public sealed class ApiKeyScopes
{
    /// <summary>Gets or sets the indexes the key may act on. <c>["*"]</c> means every index.</summary>
    [JsonPropertyName("indexes")]
    public IList<string> Indexes { get; set; } = [];

    /// <summary>Gets or sets the operations the key may perform: <c>write</c>, <c>delete</c>, <c>rebuild</c>, <c>read</c>.</summary>
    [JsonPropertyName("ops")]
    public IList<string> Ops { get; set; } = [];

    /// <summary>Determines whether the key may perform an operation on an index.</summary>
    /// <param name="index">Code name of the index, or empty for an operation that names no index (the index listing).</param>
    /// <param name="operation">The operation being attempted.</param>
    /// <returns><see langword="true"/> when both the index and the operation are in scope.</returns>
    public bool Allows(string index, string operation) =>
        (string.IsNullOrEmpty(index) || Indexes.Any(scope => scope == "*" || string.Equals(scope, index, StringComparison.OrdinalIgnoreCase)))
        && Ops.Any(scope => scope == "*" || string.Equals(scope, operation, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A stored API key. The plaintext is never part of it: only the hash, and the prefix that identifies
/// the key in the admin UI and in the ingestion log.
/// </summary>
/// <param name="Id">Database identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Hash">PBKDF2 hash of the key, in the encoded form <see cref="ApiKeyService"/> produces.</param>
/// <param name="Prefix">First characters of the key, for identification.</param>
/// <param name="Scopes">What the key may do.</param>
/// <param name="Enabled">Whether the key is usable at all.</param>
/// <param name="ExpiresAt">When the key stops working, in UTC. Null never expires.</param>
/// <param name="LastUsedAt">When the key was last used, in UTC.</param>
public sealed record ApiKeyRecord(
    int Id,
    string Name,
    string Hash,
    string Prefix,
    ApiKeyScopes Scopes,
    bool Enabled,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt);

/// <summary>Persistence of API keys, backed by the <c>XpSearchApiKey</c> custom module class.</summary>
public interface IApiKeyStore
{
    /// <summary>Finds a key by its prefix, which is the only part of it that is stored in the clear.</summary>
    /// <param name="prefix">The key prefix.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching keys - a prefix is short, so more than one can share it.</returns>
    Task<IReadOnlyList<ApiKeyRecord>> FindByPrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Stores a new key.</summary>
    /// <param name="record">The key, with its hash already computed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored key, with its database identifier.</returns>
    Task<ApiKeyRecord> CreateAsync(ApiKeyRecord record, CancellationToken cancellationToken);

    /// <summary>Records that a key was used.</summary>
    /// <param name="id">Database identifier of the key.</param>
    /// <param name="usedAt">When it was used, in UTC.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the timestamp is stored.</returns>
    Task TouchAsync(int id, DateTime usedAt, CancellationToken cancellationToken);
}

/// <summary>A newly created key: the one and only time the plaintext exists outside the caller.</summary>
/// <param name="Key">The plaintext key. Show it once, store it nowhere.</param>
/// <param name="Record">The stored key.</param>
public sealed record CreatedApiKey(string Key, ApiKeyRecord Record);

/// <summary>Why an API key was refused.</summary>
public enum ApiKeyFailure
{
    /// <summary>The key is valid.</summary>
    None,

    /// <summary>No key was sent, or it does not match any stored key.</summary>
    Unknown,

    /// <summary>The key exists but has been disabled.</summary>
    Disabled,

    /// <summary>The key exists but its expiry has passed.</summary>
    Expired,

    /// <summary>The key is valid but not scoped to this index or this operation.</summary>
    OutOfScope
}

/// <summary>Creates and verifies ingestion API keys (spec §10.4).</summary>
public interface IApiKeyService
{
    /// <summary>Creates a key and returns its plaintext exactly once.</summary>
    /// <param name="name">Display name of the key.</param>
    /// <param name="scopes">What the key may do.</param>
    /// <param name="expiresAt">When the key stops working, in UTC, or null for no expiry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The plaintext key and the stored record.</returns>
    Task<CreatedApiKey> CreateAsync(string name, ApiKeyScopes scopes, DateTime? expiresAt, CancellationToken cancellationToken);

    /// <summary>Verifies a bearer token against the stored keys and the requested operation.</summary>
    /// <param name="key">The plaintext key from the <c>Authorization</c> header.</param>
    /// <param name="index">Code name of the index being acted on.</param>
    /// <param name="operation">The operation being attempted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching key and why it was refused, if it was.</returns>
    Task<(ApiKeyRecord? Key, ApiKeyFailure Failure)> AuthenticateAsync(string? key, string index, string operation, CancellationToken cancellationToken);
}

/// <summary>
/// Hashes keys with PBKDF2-HMAC-SHA256 and a per-key random salt
/// (https://learn.microsoft.com/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2).
/// A plain SHA-256 of a 32-byte random key would be adequate against offline attack, but PBKDF2 costs
/// one hash per request and removes the argument entirely; the iteration count is the OWASP-2023
/// figure for PBKDF2-HMAC-SHA256.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    /// <summary>Number of characters of a key stored in the clear to identify it.</summary>
    public const int PrefixLength = 8;

    private const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Scheme = "pbkdf2-sha256";

    private readonly IApiKeyStore store;
    private readonly XpSearchIngestionOptions options;
    private readonly TimeProvider time;

    /// <summary>Initializes a new instance of the <see cref="ApiKeyService"/> class.</summary>
    /// <param name="store">Where keys are persisted.</param>
    /// <param name="options">Ingestion configuration.</param>
    /// <param name="time">Clock, substitutable in tests.</param>
    public ApiKeyService(IApiKeyStore store, IOptions<XpSearchIngestionOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        this.store = store;
        this.options = options.Value;
        this.time = time;
    }

    /// <summary>Hashes a plaintext key.</summary>
    /// <param name="key">The plaintext key.</param>
    /// <returns>The encoded hash: scheme, iteration count, salt and hash, separated by <c>$</c>.</returns>
    public static string Hash(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Join('$', Scheme, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Verifies a plaintext key against an encoded hash.</summary>
    /// <param name="key">The plaintext key.</param>
    /// <param name="encoded">The stored hash.</param>
    /// <returns><see langword="true"/> when the key produces the stored hash.</returns>
    public static bool Verify(string key, string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        string[] parts = encoded.Split('$');

        if (string.IsNullOrEmpty(key) || parts.Length != 4 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
        {
            return false;
        }

        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(key),
            Convert.FromBase64String(parts[2]),
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <inheritdoc />
    public async Task<CreatedApiKey> CreateAsync(string name, ApiKeyScopes scopes, DateTime? expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(scopes);

        // URL-safe base64 of 32 random bytes: no ambiguous characters, nothing to escape in a header.
        string key = "xps_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var record = new ApiKeyRecord(
            Id: 0,
            name,
            Hash(key),
            key[..PrefixLength],
            scopes,
            Enabled: true,
            expiresAt,
            LastUsedAt: null);

        return new CreatedApiKey(key, await store.CreateAsync(record, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<(ApiKeyRecord? Key, ApiKeyFailure Failure)> AuthenticateAsync(
        string? key,
        string index,
        string operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length <= PrefixLength)
        {
            return (null, ApiKeyFailure.Unknown);
        }

        var candidates = await store.FindByPrefixAsync(key[..PrefixLength], cancellationToken).ConfigureAwait(false);
        var match = candidates.FirstOrDefault(candidate => Verify(key, candidate.Hash));

        if (match is null)
        {
            return (null, ApiKeyFailure.Unknown);
        }

        var now = time.GetUtcNow().UtcDateTime;

        if (!match.Enabled)
        {
            return (match, ApiKeyFailure.Disabled);
        }

        if (match.ExpiresAt is { } expiry && expiry <= now)
        {
            return (match, ApiKeyFailure.Expired);
        }

        if (!match.Scopes.Allows(index, operation))
        {
            return (match, ApiKeyFailure.OutOfScope);
        }

        if (match.LastUsedAt is null || now - match.LastUsedAt >= options.KeyLastUsedThrottle)
        {
            await store.TouchAsync(match.Id, now, cancellationToken).ConfigureAwait(false);
        }

        return (match, ApiKeyFailure.None);
    }

    /// <summary>Serializes scopes for the <c>KeyScopes</c> column.</summary>
    /// <param name="scopes">The scopes.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ApiKeyScopes scopes) => JsonSerializer.Serialize(scopes);

    /// <summary>Reads scopes back from the <c>KeyScopes</c> column.</summary>
    /// <param name="json">The stored JSON, which may be empty on a malformed row.</param>
    /// <returns>The scopes; an empty scope set when the column cannot be read, which allows nothing.</returns>
    public static ApiKeyScopes Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ApiKeyScopes();
        }

        try
        {
            return JsonSerializer.Deserialize<ApiKeyScopes>(json) ?? new ApiKeyScopes();
        }
        catch (JsonException)
        {
            return new ApiKeyScopes();
        }
    }
}

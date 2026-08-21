namespace XpSearch.Core.Contract;

/// <summary>
/// The frozen transport details of the Xperience Search JSON API (spec §4.2, §4.3).
/// The TypeScript client mirrors these values in <c>src/contract/constants.ts</c>.
/// </summary>
public static class ContractConstants
{
    /// <summary>
    /// The contract version, sent on every response in the <see cref="ApiVersionHeader"/> header.
    /// It is the semver major of both the NuGet and the npm package; routes carry no version segment.
    /// </summary>
    public const string ApiVersion = "1";

    /// <summary>
    /// Name of the response header that carries <see cref="ApiVersion"/>.
    /// </summary>
    public const string ApiVersionHeader = "X-XpSearch-Api-Version";

    /// <summary>
    /// Route of the search endpoint: POST, <c>SearchRequest</c> in, <c>SearchResponse</c> out.
    /// </summary>
    public const string QueryRoute = "/api/xpsearch/query";

    /// <summary>
    /// Route of the autocomplete endpoint: POST, <c>SuggestRequest</c> in, <c>SuggestResponse</c> out.
    /// </summary>
    public const string SuggestRoute = "/api/xpsearch/suggest";

    /// <summary>
    /// Route of the analytics endpoint: POST, <c>EventRequest</c> in, <c>202 Accepted</c> with an empty body out.
    /// </summary>
    public const string EventsRoute = "/api/xpsearch/events";
}

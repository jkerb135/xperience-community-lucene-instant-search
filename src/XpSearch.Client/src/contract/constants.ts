/**
 * The frozen transport details of the Xperience Search JSON API (spec 4.2, 4.3).
 * The C# client mirrors these values in `XpSearch.Core/Contract/ContractConstants.cs`.
 */

/**
 * The contract version, sent on every response in the {@link API_VERSION_HEADER} header.
 * It is the semver major of both the npm and the NuGet package; routes carry no version segment.
 */
export const API_VERSION = '1';

/** Name of the response header that carries {@link API_VERSION}. */
export const API_VERSION_HEADER = 'X-XpSearch-Api-Version';

/** Route of the search endpoint: POST, `SearchRequest` in, `SearchResponse` out. */
export const QUERY_ROUTE = '/api/xpsearch/query';

/** Route of the autocomplete endpoint: POST, `SuggestRequest` in, `SuggestResponse` out. */
export const SUGGEST_ROUTE = '/api/xpsearch/suggest';

/** Route of the analytics endpoint: POST, `EventRequest` in, `202 Accepted` with an empty body out. */
export const EVENTS_ROUTE = '/api/xpsearch/events';

/**
 * The request and response samples from the specification (§4.2), typed against the generated
 * contract. `npm run typecheck` is the test: it fails if the generated types and the samples
 * disagree. The same two payloads back the C# round-trip tests in
 * `XpSearch.Core.Tests/Contract/Fixtures/`.
 */
import type { SearchRequest, SearchResponse } from '../generated.js';

export const specSearchRequest: SearchRequest = {
  index: 'site-content',
  query: 'espresso',
  page: 0,
  hitsPerPage: 20,
  facets: ['contentType', 'tags', 'language'],
  facetFilters: [
    ['contentType:Article', 'contentType:Product'],
    ['tags:coffee'],
  ],
  numericFilters: ['price<=50', 'publishedAt>=1700000000'],
  sort: 'relevance',
  highlight: {
    fields: ['title', 'content'],
    preTag: '<mark>',
    postTag: '</mark>',
    snippetLength: 200,
  },
  attributesToRetrieve: ['title', 'url', 'summary', 'image'],
  language: 'en',
  queryId: 'generated-guid',
};

export const specSearchResponse: SearchResponse = {
  hits: [
    {
      objectID: 'web-page-42-en',
      // title and summary are not reserved members: they type-check only because Hit is open.
      title: 'Espresso Basics',
      url: '/articles/espresso-basics',
      summary: '...',
      _score: 8.42,
      _highlights: {
        title: '<mark>Espresso</mark> Basics',
        content: '...brewing <mark>espresso</mark> requires...',
      },
      _rankingInfo: {
        baseScore: 6.10,
        appliedBoosts: ['freshness:+1.2', 'rule:pin-espresso-guide'],
        position: 1,
      },
    },
  ],
  facets: {
    contentType: { Article: 34, Product: 12 },
    tags: { coffee: 40, brewing: 18 },
  },
  page: 0,
  hitsPerPage: 20,
  nbHits: 46,
  nbPages: 3,
  processingTimeMs: 14,
  queryId: 'generated-guid',
};

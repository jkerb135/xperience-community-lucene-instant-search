/**
 * The request and response samples from the contract amendment (ADR-0010, §4.2), typed against
 * the generated contract. `npm run typecheck` is the test: it fails if the generated types and
 * the samples disagree. The same two payloads back the C# round-trip tests in
 * `XpSearch.Core.Tests/Contract/Fixtures/`.
 */
import type { SearchRequest, SearchResponse } from '../generated.js';

export const specSearchRequest: SearchRequest = {
  index: 'site-content',
  query: 'espresso',
  page: 1,
  pageSize: 20,
  facets: ['contentType', 'tags'],
  filters: {
    facets: [
      { attribute: 'contentType', values: ['Article', 'Product'], operator: 'or' },
      { attribute: 'tags', values: ['coffee'] },
    ],
    numeric: [
      { attribute: 'price', operator: 'lte', value: 50 },
      { attribute: 'publishedAt', operator: 'gte', value: 1700000000 },
    ],
  },
  sort: 'relevance',
  highlight: {
    fields: ['title', 'content'],
    preTag: '<mark>',
    postTag: '</mark>',
    snippetLength: 200,
  },
  fields: ['title', 'url', 'summary', 'image'],
  language: 'en',
  queryId: 'generated-guid',
  explain: false,
};

export const specSearchResponse: SearchResponse = {
  results: [
    {
      id: 'web-page-42-en',
      score: 8.42,
      // title, url and summary are retrieved document fields: they live in `attributes`, the
      // only open object in the contract, and can never collide with a member beside it.
      attributes: {
        title: 'Espresso Basics',
        url: '/articles/espresso-basics',
        summary: '...',
      },
      highlights: {
        title: '<mark>Espresso</mark> Basics',
        content: '...brewing <mark>espresso</mark> requires...',
      },
      ranking: {
        baseScore: 6.1,
        boosts: ['freshness:+1.2', 'rule:pin-espresso-guide'],
        position: 1,
      },
    },
  ],
  facets: {
    contentType: [
      { value: 'Article', label: 'Article', count: 34 },
      { value: 'Product', label: 'Product', count: 12 },
    ],
    tags: [
      { value: 'coffee', label: 'Coffee', count: 40 },
      { value: 'brewing', label: 'Brewing', count: 18 },
    ],
  },
  page: 1,
  pageSize: 20,
  total: 46,
  totalPages: 3,
  tookMs: 14,
  redirect: null,
  queryId: 'generated-guid',
};

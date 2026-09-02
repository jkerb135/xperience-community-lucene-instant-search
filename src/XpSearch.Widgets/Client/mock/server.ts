/**
 * A contract-faithful mock of the search API, for tests, docs examples and demos.
 * No dependencies, no build step: Node strips the types (`node mock/server.ts`).
 *
 * It implements the wire behaviour of spec 4.2/4.3 over an in-memory corpus — it is not a
 * model of the Lucene pipeline. The response objects are typed as the generated contract
 * types, so `npm run typecheck` fails if the mock drifts from the schema.
 */
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import { randomUUID } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { API_VERSION, API_VERSION_HEADER, EVENTS_ROUTE, QUERY_ROUTE, SUGGEST_ROUTE } from '../src/contract/constants.ts';
import type {
  EventRequest,
  FacetFilter,
  FacetValue,
  NumericFilter,
  Result,
  SearchRequest,
  SearchResponse,
  SuggestRequest,
  SuggestResponse,
  Suggestion,
} from '../src/contract/generated.ts';
import type {
  IngestionError,
  PushDocument,
  UpsertRequest,
  UpsertResponse,
} from '../src/contract/ingestion-generated.ts';

interface Doc {
  id: string;
  title: string;
  content: string;
  url: string;
  contentType: string;
  tags: string[];
  language: string;
  price: number;
  publishedAt: number;
}

const CONTENT_TYPES = ['Article', 'Product', 'FAQ'];
const TAGS = ['coffee', 'brewing', 'espresso', 'beans', 'grinder', 'milk'];
const LANGUAGES = ['en', 'de'];
const TOPICS = [
  'Espresso Basics',
  'Cold Brew Guide',
  'Milk Frothing',
  'Bean Origins',
  'Grinder Setup',
  'Water Chemistry',
  'Latte Art',
  'Roast Profiles',
  'Decaf Myths',
];
/** 2023-11-14T22:13:20Z, so `publishedAt` values are realistic epoch seconds. */
const EPOCH = 1_700_000_000;

/**
 * The display title of each tag, keyed by its code name — what an Xperience taxonomy carries and
 * what the server puts in `facets[].label`, so a facet list never shows a code name.
 */
const TAG_LABELS: Record<string, string> = {
  coffee: 'Coffee',
  brewing: 'Brewing',
  espresso: 'Espresso',
  beans: 'Beans',
  grinder: 'Grinders',
  milk: 'Milk drinks',
  equipment: 'Equipment',
};

/**
 * The parent of each tag, keyed by code name — `tags` is a hierarchical taxonomy, as an Xperience
 * one is. Two levels: `coffee > (beans, espresso)`, `equipment > grinder`, `brewing > milk`.
 */
const TAG_PARENTS: Record<string, string> = {
  beans: 'coffee',
  espresso: 'coffee',
  grinder: 'equipment',
  milk: 'brewing',
};

/** A tag's ancestors, root first, excluding the tag itself — the contract's `FacetValue.path`. */
const ancestorsOf = (tag: string): string[] => {
  const parent = TAG_PARENTS[tag];
  return parent === undefined ? [] : [...ancestorsOf(parent), parent];
};

/**
 * A document carries its tags' ancestors as well, which is exactly what the Lucene indexer writes:
 * counts then roll up and a filter on a parent matches the documents tagged with its descendants,
 * with no hierarchy logic anywhere else.
 */
const withAncestors = (tags: string[]): string[] => [
  ...new Set(tags.flatMap((tag) => [...ancestorsOf(tag), tag])),
];

/** 54 documents: 3 content types, 6 tags, 2 languages, a price and a publish date. */
export const CORPUS: Doc[] = Array.from({ length: 54 }, (_, i) => {
  const topic = TOPICS[i % TOPICS.length]!;
  return {
    id: `doc-${i + 1}`,
    title: `${topic} ${i + 1}`,
    content: `A guide to ${topic.toLowerCase()}. ${TAGS[i % TAGS.length]} and ${TAGS[(i + 2) % TAGS.length]} for every barista.`,
    url: `/docs/${topic.toLowerCase().replace(/\s+/g, '-')}-${i + 1}`,
    // `i % 5` first: a plain `i % 3` would line up with the topic and tag cycles and leave
    // whole content types unmatched by any query.
    contentType: CONTENT_TYPES[(i % 5) % CONTENT_TYPES.length]!,
    tags: withAncestors([TAGS[i % TAGS.length]!, TAGS[(i + 2) % TAGS.length]!]),
    language: LANGUAGES[i % LANGUAGES.length]!,
    price: 5 + (i % 12) * 5,
    publishedAt: EPOCH - i * 86_400,
  };
});

const attributeOf = (doc: Doc, attribute: string): string[] => {
  const value = (doc as unknown as Record<string, unknown>)[attribute];
  if (Array.isArray(value)) return value.map(String);
  return value === undefined ? [] : [String(value)];
};

const numberOf = (doc: Doc, attribute: string): number | undefined => {
  const value = (doc as unknown as Record<string, unknown>)[attribute];
  return typeof value === 'number' ? value : undefined;
};

const labelOf = (attribute: string, value: string): string =>
  attribute === 'tags' ? (TAG_LABELS[value] ?? value) : value;

const tokens = (query: string): string[] =>
  query.toLowerCase().split(/\s+/).filter((t) => t !== '');

function score(doc: Doc, query: string): number {
  const words = tokens(query);
  if (words.length === 0) return 1;
  const title = doc.title.toLowerCase();
  const haystack = `${title} ${doc.content.toLowerCase()} ${doc.tags.join(' ')}`;
  let total = 0;
  for (const word of words) {
    if (!haystack.includes(word)) return 0;
    total += title.includes(word) ? 2 : 1;
  }
  return total;
}

/** One `filters.facets` entry: `or` matches any selected value, `and` requires all of them. */
function matchesFacet(doc: Doc, filter: FacetFilter): boolean {
  const values = attributeOf(doc, filter.attribute);
  return (filter.operator ?? 'or') === 'and'
    ? filter.values.every((value) => values.includes(value))
    : filter.values.some((value) => values.includes(value));
}

function matchesNumeric(doc: Doc, filter: NumericFilter): boolean {
  const value = numberOf(doc, filter.attribute);
  if (value === undefined) return false;
  switch (filter.operator) {
    case 'lt':
      return value < filter.value;
    case 'lte':
      return value <= filter.value;
    case 'gt':
      return value > filter.value;
    case 'gte':
      return value >= filter.value;
    case 'eq':
      return value === filter.value;
    default:
      return value !== filter.value;
  }
}

const escapeHtml = (text: string): string =>
  text.replace(/[&<>"']/g, (c) => `&${{ '&': 'amp', '<': 'lt', '>': 'gt', '"': 'quot', "'": '#39' }[c]!};`);

/** Encode first, then insert the tags — never the other way round (spec 4.6). */
function highlight(value: string, query: string, preTag: string, postTag: string, snippetLength: number): string {
  const words = tokens(query);
  const lower = value.toLowerCase();
  const first = words.map((w) => lower.indexOf(w)).filter((i) => i >= 0).sort((a, b) => a - b)[0] ?? 0;
  const start = Math.max(0, first - Math.floor(snippetLength / 4));
  const snippet = value.slice(start, start + snippetLength);
  let out = escapeHtml(snippet);
  for (const word of words) {
    const pattern = new RegExp(escapeHtml(word).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');
    out = out.replace(pattern, (m) => `${preTag}${m}${postTag}`);
  }
  return (start > 0 ? '…' : '') + out;
}

function sortDocs(docs: Array<{ doc: Doc; score: number }>, sort: string | undefined): void {
  switch (sort) {
    case 'price_asc':
      docs.sort((a, b) => a.doc.price - b.doc.price);
      break;
    case 'price_desc':
      docs.sort((a, b) => b.doc.price - a.doc.price);
      break;
    case 'newest':
      docs.sort((a, b) => b.doc.publishedAt - a.doc.publishedAt);
      break;
    default:
      docs.sort((a, b) => b.score - a.score || a.doc.id.localeCompare(b.doc.id));
  }
}

/** Runs one query against the corpus. Exported so tests can assert without HTTP. */
export function query(request: SearchRequest): SearchResponse {
  const started = Date.now();
  const facetEntries = request.filters?.facets ?? [];
  const numeric = request.filters?.numeric ?? [];
  const base = CORPUS.filter(
    (doc) =>
      score(doc, request.query ?? '') > 0 &&
      (request.language === undefined || doc.language === request.language) &&
      numeric.every((filter) => matchesNumeric(doc, filter))
  );
  const matched = base.filter((doc) => facetEntries.every((filter) => matchesFacet(doc, filter)));

  // Disjunctive faceting: a value's count ignores the filter on its own attribute, so an
  // `or` facet list keeps showing the alternatives the user can still pick.
  const facets: Record<string, FacetValue[]> = {};
  for (const attribute of request.facets ?? []) {
    const others = facetEntries.filter((filter) => filter.attribute !== attribute);
    const counts = new Map<string, number>();
    for (const doc of base.filter((d) => others.every((filter) => matchesFacet(d, filter)))) {
      for (const value of attributeOf(doc, attribute)) {
        counts.set(value, (counts.get(value) ?? 0) + 1);
      }
    }
    // Count descending, then value ascending — the order the contract promises.
    facets[attribute] = [...counts]
      .map(([value, count]) => {
        const path = attribute === 'tags' ? ancestorsOf(value) : [];
        // `path` is absent, not empty, for a root value and for a non-taxonomy attribute.
        return { value, label: labelOf(attribute, value), count, ...(path.length > 0 ? { path } : {}) };
      })
      .sort((a, b) => b.count - a.count || (a.value < b.value ? -1 : a.value > b.value ? 1 : 0));
  }

  const scored = matched.map((doc) => ({ doc, score: score(doc, request.query ?? '') }));
  sortDocs(scored, request.sort);

  const pageSize = Math.min(Math.max(request.pageSize ?? 20, 1), 1000);
  const page = Math.max(request.page ?? 1, 1);
  const window = scored.slice((page - 1) * pageSize, page * pageSize);

  const preTag = request.highlight?.preTag ?? '<mark>';
  const postTag = request.highlight?.postTag ?? '</mark>';
  const snippetLength = request.highlight?.snippetLength ?? 200;

  const results: Result[] = window.map(({ doc, score: resultScore }, index) => {
    const source = doc as unknown as Record<string, unknown>;
    const attributes: Record<string, unknown> = {};
    for (const field of request.fields ?? Object.keys(source)) {
      if (field !== 'id' && field in source) attributes[field] = source[field];
    }
    const result: Result = { id: doc.id, attributes, score: resultScore };
    const fields = request.highlight?.fields ?? [];
    if (fields.length > 0 && (request.query ?? '') !== '') {
      const highlights: Record<string, string> = {};
      for (const field of fields) {
        const value = source[field];
        if (typeof value === 'string') {
          highlights[field] = highlight(value, request.query ?? '', preTag, postTag, snippetLength);
        }
      }
      result.highlights = highlights;
    }
    if (request.explain) {
      result.ranking = {
        baseScore: resultScore,
        boosts: [],
        position: (page - 1) * pageSize + index + 1,
      };
    }
    return result;
  });

  return {
    results,
    facets,
    page,
    pageSize,
    total: scored.length,
    totalPages: Math.ceil(scored.length / pageSize),
    tookMs: Date.now() - started,
    redirect: null,
    // Stands in for a Search-tuning rule with the Return-custom-data action, so a client can
    // be developed against `results.ruleData` without an Xperience instance.
    ...(request.query?.toLowerCase().includes('espresso')
      ? { ruleData: { banner: 'espresso-week', layout: 'grid' } }
      : {}),
    // No-results recovery (SG-1): only for a dead end, and never for a probe.
    ...(scored.length === 0 && request.probe !== true ? recovery(request.query ?? '') : {}),
    queryId: request.queryId ?? randomUUID(),
  };
}

/** The most-searched queries the mock pretends its query log holds. */
const POPULAR = ['espresso', 'latte art', 'grinder'];

/**
 * Stands in for the server's did-you-mean and popular-search enrichment: a query one edit away from
 * a corpus title corrects to it, and the popular searches are a fixed list.
 */
function recovery(query: string): { didYouMean?: string; popularSearches?: string[] } {
  const words = new Set(CORPUS.flatMap((doc) => doc.title.toLowerCase().split(' ')));
  const corrected = query
    .split(' ')
    .map((word) => [...words].find((known) => editDistance(word.toLowerCase(), known) === 1) ?? word)
    .join(' ');
  return {
    ...(corrected.toLowerCase() === query.toLowerCase() ? {} : { didYouMean: corrected }),
    popularSearches: POPULAR,
  };
}

/** Levenshtein, capped at what a one-edit correction needs. */
function editDistance(left: string, right: string): number {
  if (Math.abs(left.length - right.length) > 1) return 2;
  let row = [...Array.from({ length: right.length + 1 }, (_, at) => at)];
  for (let i = 1; i <= left.length; i++) {
    const next = [i];
    for (let j = 1; j <= right.length; j++) {
      next[j] = Math.min(
        (row[j] ?? 0) + 1,
        (next[j - 1] ?? 0) + 1,
        (row[j - 1] ?? 0) + (left[i - 1] === right[j - 1] ? 0 : 1)
      );
    }
    row = next;
  }
  return row[right.length] ?? 2;
}

export function suggest(request: SuggestRequest): SuggestResponse {
  const prefix = (request.query ?? '').toLowerCase();
  const limit = request.limit ?? 5;
  if (prefix === '') return { suggestions: [] };
  const seen = new Set<string>();
  // Mixed mode (SG-1): the popular queries lead with half the limit, the documents fill the rest,
  // and every entry says which source it came from.
  const queries: Suggestion[] = POPULAR.filter((text) => text.startsWith(prefix))
    .slice(0, Math.max(1, Math.floor(limit / 2)))
    .map((text) => ({ text, group: 'query' }));
  const documents: Suggestion[] = CORPUS.filter((doc) => doc.title.toLowerCase().startsWith(prefix))
    .filter((doc) => {
      if (seen.has(doc.title)) return false;
      seen.add(doc.title);
      return true;
    })
    .slice(0, limit - queries.length)
    .map((doc) => ({
      text: doc.title,
      group: 'document',
      url: doc.url,
      result: { id: doc.id, attributes: { title: doc.title, url: doc.url } },
    }));
  return { suggestions: [...queries, ...documents] };
}

/**
 * The ingestion half of the mock (CL-1): just enough of `POST indexes/{index}/documents` for the
 * typed clients' end-to-end test — bearer auth, the document cap, per-document validation and the
 * partial-success answer. A test double, not a second implementation of the server.
 */
export const PUSHED = new Map<string, Map<string, PushDocument>>();

/** Prefix of the one ingestion route the mock answers. */
const UPSERT_PATTERN = /^\/api\/xpsearch\/admin\/indexes\/([^/]+)\/documents$/;

/** The API key the mock accepts, so a test can assert the 401 path with any other value. */
export const MOCK_API_KEY = 'xps_mock_key';

function upsert(index: string, authorization: string | undefined, body: unknown): { status: number; body: unknown } {
  if (authorization !== `Bearer ${MOCK_API_KEY}`) {
    return { status: 401, body: { title: 'The API key is not valid.', status: 401 } };
  }
  const request = body as UpsertRequest;
  const documents = request?.documents;
  if (!Array.isArray(documents) || documents.length === 0) {
    return { status: 400, body: { title: 'The request is not valid.', status: 400, errors: { documents: ['At least one document is required.'] } } };
  }
  if (documents.length > 1000) {
    return { status: 413, body: { title: 'The request is too large.', status: 413, detail: 'A request may carry at most 1000 documents. Split the batch.' } };
  }
  const stored = PUSHED.get(index) ?? new Map<string, PushDocument>();
  PUSHED.set(index, stored);
  const errors: IngestionError[] = [];
  for (const document of documents) {
    // The one validation rule the mock keeps: a document must have an id, and `price` must be a
    // number if it is there — enough to exercise the partial-failure path.
    if (typeof document.id !== 'string' || document.id === '') {
      errors.push({ message: 'id is required.' });
    } else if ('price' in document && typeof document.price !== 'number') {
      errors.push({ id: document.id, field: 'price', message: 'price must be a number.' });
    } else {
      stored.set(document.id, document);
    }
  }
  const response: UpsertResponse = {
    indexed: documents.length - errors.length,
    failed: errors.length,
    errors,
    taskId: randomUUID(),
    tookMs: 1,
  };
  return { status: 200, body: response };
}

function readBody(req: IncomingMessage): Promise<unknown> {
  return new Promise((resolve, reject) => {
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('error', reject);
    req.on('end', () => {
      try {
        resolve(raw === '' ? {} : JSON.parse(raw));
      } catch (error) {
        reject(error as Error);
      }
    });
  });
}

function send(res: ServerResponse, status: number, body?: unknown): void {
  res.writeHead(status, {
    [API_VERSION_HEADER]: API_VERSION,
    'content-type': body === undefined ? 'text/plain' : 'application/json',
    'access-control-allow-origin': '*',
  });
  res.end(body === undefined ? '' : JSON.stringify(body));
}

/** The API half of the mock, so the demo server can serve files alongside it. */
export function handleApiRequest(req: IncomingMessage, res: ServerResponse): void {
  const path = (req.url ?? '').split('?')[0];
  if (req.method !== 'POST') {
    send(res, 405);
    return;
  }
  void readBody(req).then(
    (body) => {
      if (path === QUERY_ROUTE) {
        const request = body as SearchRequest;
        if (!request?.index) {
          send(res, 400, { title: 'index is required', status: 400 });
          return;
        }
        send(res, 200, query(request));
      } else if (path === SUGGEST_ROUTE) {
        send(res, 200, suggest(body as SuggestRequest));
      } else if (path === EVENTS_ROUTE) {
        const event = body as EventRequest;
        if (!event?.resultId || !event?.queryId) send(res, 400, { title: 'resultId and queryId are required', status: 400 });
        else send(res, 202);
      } else if (UPSERT_PATTERN.test(path ?? '')) {
        const index = decodeURIComponent(UPSERT_PATTERN.exec(path!)![1]!);
        const answer = upsert(index, req.headers.authorization, body);
        send(res, answer.status, answer.body);
      } else {
        send(res, 404, { title: 'Not found', status: 404 });
      }
    },
    () => send(res, 400, { title: 'Malformed JSON body', status: 400 })
  );
}

export function createMockServer(): Server {
  return createServer(handleApiRequest);
}

/** Starts the mock on `port` (0 = an ephemeral one) and resolves its base URL. */
export function startMockServer(port = 0): Promise<{ url: string; close: () => Promise<void> }> {
  const server = createMockServer();
  return new Promise((resolve) => {
    server.listen(port, '127.0.0.1', () => {
      const address = server.address();
      const actual = typeof address === 'object' && address !== null ? address.port : port;
      resolve({
        url: `http://127.0.0.1:${actual}`,
        close: () => new Promise<void>((done) => server.close(() => done())),
      });
    });
  });
}

// Only when started directly (`npx xpsearch-mock`, `npm run repo:mock`), not when a test imports
// startMockServer.
if (process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const port = Number(process.env['PORT'] ?? 3131);
  void startMockServer(port).then(({ url }) => {
    console.log(`xpsearch mock server on ${url}${QUERY_ROUTE} (${CORPUS.length} documents)`);
  });
}

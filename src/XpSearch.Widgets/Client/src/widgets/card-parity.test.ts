/**
 * The default result card exists three times — the client's `defaultResultItem`, the widgets'
 * `_Result.cshtml`, and `ServerRenderedResults.DefaultCard` in XpSearch.Core (KNOWN-LIMITATIONS).
 * Drift between them is silent on a real host: the first paint and the hydrated list simply stop
 * matching. This reads the two server sources as text and compares them with what the client
 * actually renders.
 */
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { toHtml } from '../templates/html';
import { defaultResultItem, emptyState } from './results';

const here = dirname(fileURLToPath(import.meta.url));
const widgets = join(here, '../../..'); // src/XpSearch.Widgets
const razor = readFileSync(join(widgets, 'Components/Widgets/XpSearch/_Result.cshtml'), 'utf8');
const csharp = readFileSync(join(widgets, '../XpSearch.Core/Rendering/ServerRenderedResults.cs'), 'utf8');

/** The markup of a C# expression made of string literals, concatenated in source order. */
const literals = (source: string): string =>
  [...source.matchAll(/"((?:[^"\\]|\\.)*)"/g)]
    .map((match) => match[1]!.replace(/\\"/g, '"').replace(/\\\\/g, '\\'))
    .join('');

const between = (source: string, from: string, to: string): string =>
  source.slice(source.indexOf(from), source.indexOf(to));

const icon = literals(between(csharp, 'internal const string FileIcon', 'private readonly ISearchPipeline'));
/** `DefaultCard`'s own markup, with the shared icon constant spliced in where it is appended. */
const defaultCard = literals(
  between(csharp, 'private static IHtmlContent DefaultCard', 'private static bool Applies').replace(
    'AppendHtml(FileIcon)',
    `AppendHtml("${icon.replace(/"/g, '\\"')}")`
  )
);

/** `<tag class="…">` for every element that carries an `xps-` class, in document order. */
const cardClasses = (markup: string): string[] =>
  [...markup.matchAll(/<([a-zA-Z][\w-]*)\s[^>]*class="(xps-[^"]*)"/g)].map(
    (match) => `${match[1]!.toLowerCase()}.${match[2]!}`
  );

const RESULT = {
  id: 'doc-1',
  attributes: {
    title: 'Choosing an espresso machine',
    url: '/blog/choosing-an-espresso-machine',
    path: 'Home / Blog / Coffee',
    summary: 'A dual-boiler machine holds temperature.',
    contentType: 'Article',
    image: '/img/1.png',
  },
};

const FILE_RESULT = {
  id: 'doc-2',
  attributes: { title: 'Warranty terms', url: '/legal/warranty.pdf', fileType: 'pdf' },
};

const client = toHtml(defaultResultItem(RESULT, {}));
const clientFile = toHtml(defaultResultItem(FILE_RESULT, {}));

describe('the default card renders the same markup in all three renderers', () => {
  it('carries the same set of element/class pairs', () => {
    // The client picks one media branch per result, the two server sources spell out both.
    const fromClient = new Set([...cardClasses(client), ...cardClasses(clientFile)]);

    expect([...new Set(cardClasses(defaultCard))].sort()).toEqual([...fromClient].sort());
    expect([...new Set(cardClasses(razor))].sort()).toEqual([...fromClient].sort());
  });

  it('orders the card body the same way', () => {
    const body = ['xps-result__title', 'xps-result__path', 'xps-result__snippet', 'xps-result__meta'];
    const order = (markup: string): string[] =>
      cardClasses(markup)
        .map((token) => token.slice(token.indexOf('.') + 1))
        .filter((className) => body.includes(className));

    for (const [name, markup] of [['client', client], ['DefaultCard', defaultCard], ['_Result.cshtml', razor]] as const) {
      expect(order(markup), name).toEqual(body);
    }
  });

  it('uses one byte-identical file-type glyph', () => {
    expect(icon).toContain('<svg class="xps-result__icon"');
    expect(clientFile).toContain(icon);
    expect(razor).toContain(icon);
  });
});

/**
 * TH-6: the empty state exists twice — the client's `defaultEmpty` and `ServerRenderedResults`'
 * first paint. The server one has no recovery offers (they need the response the client fetches),
 * so what is pinned is the glyph, the headline element and the headline's wording.
 */
describe('the empty state matches between the client and the server-rendered first paint', () => {
  const emptyIcon = literals(between(csharp, 'internal const string EmptyIcon', 'private const string EmptyOpen'));
  /** `EmptyOpen` + `EmptyClose`, with the icon constant spliced in where it is concatenated. */
  const serverEmpty = literals(
    between(csharp, 'private const string EmptyOpen', 'internal const string FileIcon')
  ).replace('<div class="xps-results__empty">', `<div class="xps-results__empty">${emptyIcon}`);
  const clientEmpty = toHtml(
    emptyState({ query: 'espresso', hasRefinements: false, clearRefinements: () => {} })
  );

  it('uses one byte-identical empty-state glyph', () => {
    expect(emptyIcon).toContain('<svg class="xps-results__empty-icon"');
    expect(clientEmpty).toContain(emptyIcon);
  });

  it('carries the same elements in the same order', () => {
    expect(serverEmpty).toContain('class="xps xps-results xps-results--empty"'); // the mount root
    expect(cardClasses(serverEmpty)).toEqual(cardClasses(clientEmpty));
  });

  it('words the headline the same way, entity for entity', () => {
    expect(clientEmpty).toContain(
      '<p class="xps-results__empty-title">No results for &ldquo;espresso&rdquo;</p>'
    );
    expect(csharp).toContain(
      'state.AppendHtml("No results for &ldquo;").Append(query).AppendHtml("&rdquo;");'
    );
    expect(csharp).toContain('state.Append("No results.");');
  });
});

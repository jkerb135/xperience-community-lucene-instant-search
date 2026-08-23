/**
 * `npm run demo`: the mock API plus a static file server for `demo/`, `dist/` and the
 * stylesheets in `themes/`. No dependencies — Node strips the types.
 *
 * POST goes to the mock API (spec 4.2/4.3), everything else is a file read out of one of two
 * roots. Development only: it has no caching, no compression and no directory listing.
 */
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, resolve, sep } from 'node:path';
import { handleApiRequest, CORPUS } from './server.ts';
import { QUERY_ROUTE } from '../src/contract/constants.ts';

const CLIENT_ROOT = resolve(import.meta.dirname, '..');
const THEMES_ROOT = resolve(CLIENT_ROOT, '../../../themes');

const CONTENT_TYPES: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json',
  '.map': 'application/json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
};

/** Maps a URL path to a file, or `undefined` when it escapes both roots. */
function fileFor(path: string): string | undefined {
  const [root, relative] = path.startsWith('/themes/')
    ? [THEMES_ROOT, path.slice('/themes/'.length)]
    : [CLIENT_ROOT, path.replace(/^\//, '')];
  const file = resolve(root, relative);
  return file === root || file.startsWith(root + sep) ? file : undefined;
}

const server = createServer((request, response) => {
  if (request.method === 'POST') {
    handleApiRequest(request, response);
    return;
  }
  const path = (request.url ?? '/').split('?')[0] ?? '/';
  const file = fileFor(path === '/' ? '/demo/index.html' : path);
  if (file === undefined) {
    response.writeHead(403).end('Forbidden');
    return;
  }
  void readFile(file).then(
    (body) => {
      response.writeHead(200, {
        'content-type': CONTENT_TYPES[extname(file)] ?? 'application/octet-stream',
      });
      response.end(body);
    },
    () => {
      response.writeHead(404, { 'content-type': 'text/plain' }).end(`Not found: ${path}`);
    }
  );
});

const port = Number(process.env['PORT'] ?? 3131);
server.listen(port, '127.0.0.1', () => {
  console.log(`xpsearch demo on http://127.0.0.1:${port}/`);
  console.log(`  mock API   http://127.0.0.1:${port}${QUERY_ROUTE} (${CORPUS.length} documents)`);
  console.log(`  themes     ${join(THEMES_ROOT, 'src')}`);
});

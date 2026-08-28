#!/usr/bin/env node
/**
 * Builds `samples/CustomWidget.Dropdown` the way a customer would: from packages, never from
 * project references. Packs the three NuGet packages and the npm tarball into `samples/.feed/`,
 * then restores, builds and tests the sample against that feed only.
 *
 * Run from anywhere: `node samples/pack-and-build.mjs` (or `samples/pack-and-build.ps1`).
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, rmSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const samplesDir = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(samplesDir, '..');
const feed = join(samplesDir, '.feed');
const sample = join(samplesDir, 'CustomWidget.Dropdown');

const run = (command, args, cwd) => {
  console.log(`\n> ${command} ${args.join(' ')}   (${cwd})`);
  // `npm` is a .cmd shim on Windows and Node refuses to spawn one without a shell, so quote the
  // arguments ourselves — a repository path with a space would otherwise split.
  const shell = process.platform === 'win32';
  const quoted = shell ? args.map((arg) => (arg.includes(' ') ? `"${arg}"` : arg)) : args;
  const result = spawnSync(command, quoted, { cwd, stdio: 'inherit', shell });
  if (result.status !== 0) {
    console.error(`\npack-and-build failed: ${command} ${args.join(' ')} exited ${result.status}`);
    process.exit(result.status ?? 1);
  }
};

// custom-widgets.md claims the worked example *is* dropdownFacet.ts. Hold it to that: a guide that
// drifts from the file it says it shows is how the previous example came to ship four bugs.
const read = (file) => readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
const guide = read(join(repoRoot, 'docs/guides/custom-widgets.md'));
const widget = read(join(sample, 'src/dropdownFacet.ts'));
if (!guide.includes(`\`\`\`ts\n${widget.trimEnd()}\n\`\`\``)) {
  console.error(
    'docs/guides/custom-widgets.md no longer reproduces samples/CustomWidget.Dropdown/src/' +
      'dropdownFacet.ts verbatim. Paste the file into the guide, or stop claiming it is the file.'
  );
  process.exit(1);
}
console.log('ok: custom-widgets.md reproduces dropdownFacet.ts verbatim');

// A stale feed is worse than no feed: NuGet would happily restore yesterday's package.
rmSync(feed, { recursive: true, force: true });
mkdirSync(feed, { recursive: true });

for (const project of ['XpSearch.Core', 'XpSearch.Widgets', 'XpSearch.Admin']) {
  run('dotnet', ['pack', join('src', project), '-c', 'Release', '-o', feed], repoRoot);
}

run('npm', ['pack', '--pack-destination', feed], join(repoRoot, 'src', 'XpSearch.Widgets', 'Client'));

// npm resolves a `file:` tarball through the lockfile's integrity hash, so a freshly packed
// tarball at the same path is only picked up once the previous resolution is gone.
for (const stale of ['node_modules', 'package-lock.json']) {
  rmSync(join(sample, stale), { recursive: true, force: true });
}

run('npm', ['install'], sample);
run('npm', ['run', 'typecheck'], sample);
run('npm', ['test'], sample);

const dotnetDir = join(sample, 'dotnet');
// Into a throwaway global-packages folder: the version never changes during development, so the
// machine-wide cache would serve yesterday's xperience-community.Xperience.Search.* instead of what we packed.
run('dotnet', ['restore', '--packages', join(feed, '.packages')], dotnetDir);
run('dotnet', ['build', '--no-restore', '-c', 'Release'], dotnetDir);
run('dotnet', ['test', '--no-build', '-c', 'Release'], dotnetDir);

console.log(`\npack-and-build: the sample built and tested against ${feed} only.`);
if (!existsSync(join(feed, 'xperience-community.Xperience.Search.Widgets.0.1.0.nupkg'))) {
  console.warn('note: the widgets package version is no longer 0.1.0 — update the sample.');
}

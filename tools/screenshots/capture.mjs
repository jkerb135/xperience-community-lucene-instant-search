// Screenshot capture pipeline for docs/guides/images/ — see docs/internal/screenshot-manifest.md.
// Usage: npm run capture [-- shotName ...]   (no args = all shots in routes.json)
// First run opens a headed browser; sign in once (tick "Keep me signed in") — the profile
// in .profile/ keeps the session for unattended recaptures.
import { chromium } from 'playwright';
import { readFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const cfg = JSON.parse(readFileSync(resolve(here, 'routes.json'), 'utf8'));
const outDir = resolve(here, cfg.outDir);
mkdirSync(outDir, { recursive: true });

const only = process.argv.slice(2);
const shots = only.length ? cfg.shots.filter(s => only.includes(s.name)) : cfg.shots;
if (!shots.length) throw new Error(`No shots matched: ${only.join(', ')}`);

const ctx = await chromium.launchPersistentContext(resolve(here, '.profile'), {
  headless: false,
  viewport: cfg.viewport,
});
const page = ctx.pages()[0] ?? await ctx.newPage();

// Sign-in gate: wait (up to 5 min) until no password field is on screen.
await page.goto(cfg.baseUrl + '/admin', { waitUntil: 'networkidle' });
if (await page.locator('input[type="password"]').count()) {
  console.log('Please sign in in the browser window (tick "Keep me signed in")...');
  await page.waitForFunction(() => !document.querySelector('input[type="password"]'), null, { timeout: 300_000 });
  console.log('Signed in.');
}

// The React admin keeps polling; networkidle alone is unreliable — settle with a fixed pause.
const settle = async () => { await page.waitForLoadState('networkidle').catch(() => {}); await page.waitForTimeout(1200); };

let failed = 0;
for (const shot of shots) {
  try {
    await page.goto(cfg.baseUrl + shot.url);
    await settle();
    for (const step of shot.steps ?? []) {
      if (step.click) await page.getByText(step.click, { exact: false }).first().click();
      if (step.clickButton) await page.getByRole('button', { name: step.clickButton }).first().click();
      if (step.fill) await page.getByLabel(step.fill[0]).fill(step.fill[1]);
      if (step.fillPlaceholder) await page.getByPlaceholder(step.fillPlaceholder[0]).fill(step.fillPlaceholder[1]);
      if (step.wait) await page.waitForTimeout(step.wait);
      await settle();
    }
    if (await page.getByText('Something went wrong!').count()) throw new Error('page rendered the admin 500 screen');
    // Crop the dev-license banner off the top so published docs stay clean.
    const crop = cfg.topCrop ?? 0;
    await page.screenshot({
      path: resolve(outDir, `${shot.name}.png`),
      clip: { x: 0, y: crop, width: cfg.viewport.width, height: cfg.viewport.height - crop },
    });
    console.log(`ok  ${shot.name}`);
  } catch (e) {
    failed++;
    console.error(`FAIL ${shot.name}: ${e.message}`);
  }
}
await ctx.close();
console.log(`${shots.length - failed}/${shots.length} captured -> ${outDir}`);
process.exit(failed ? 1 : 0);

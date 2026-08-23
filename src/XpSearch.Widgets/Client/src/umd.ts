/**
 * UMD entry (spec 5.9): one `<script>` tag, one global `xpsearch`, everything hanging off it.
 * Unlike the ESM entry, this one boots itself — `.xps-mount` markup works with no author code.
 */
import * as behaviors from './behaviors';
import { mountAll } from './bootstrap';
import createSearchCore from './index';
import * as core from './index';
import type { XpSearchOptions } from './types';

const xpsearch = Object.assign(
  (options: XpSearchOptions) => createSearchCore(options),
  core,
  behaviors,
  { behaviors }
);

if (typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => mountAll());
  } else {
    mountAll();
  }
}

export default xpsearch;

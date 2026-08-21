/**
 * UMD entry (spec 5.9): one `<script>` tag, one global `xpsearch`, everything hanging off it.
 * Unlike the ESM entry, this one boots itself — `.xps-mount` markup works with no author code.
 */
import { mountAll } from './bootstrap';
import * as connectors from './connectors';
import xpsearchCore from './index';
import * as core from './index';
import type { XpSearchOptions } from './types';

const xpsearch = Object.assign(
  (options: XpSearchOptions) => xpsearchCore(options),
  core,
  connectors,
  { connectors }
);

if (typeof document !== 'undefined') {
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => mountAll());
  } else {
    mountAll();
  }
}

export default xpsearch;

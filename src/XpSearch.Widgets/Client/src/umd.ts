/**
 * UMD entry (spec 5.9): one `<script>` tag, one global `xpsearch`, everything hanging off it.
 * Unlike the ESM entry, this one boots itself — `.xps-mount` markup works with no author code.
 */
import * as behaviors from './behaviors';
import { mountAll, registerWidgetType } from './bootstrap';
import createSearchCore from './index';
import * as core from './index';
import type { XpSearchOptions } from './types';
import { DEFAULT_WIDGETS } from './widgets';

// The no-build path resolves `data-xps-widget` with no author code, so this bundle - and only this
// bundle - registers every first-party widget. Page code calling registerWidgetType() later still
// overrides them, and the ESM entry stays free of this import so it can tree-shake.
for (const [id, factory] of Object.entries(DEFAULT_WIDGETS)) registerWidgetType(id, factory);

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

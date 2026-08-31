/**
 * `myCompany.dropdownFacet` — a single-select `<select>` facet built on the published
 * `withFacetList` behaviour. This file is the worked example in
 * `docs/guides/custom-widgets.md`; the two are the same text and CI builds this one.
 */
import { escapeHtml, readMountConfig, registerWidgetType, widgetId } from '@xperience-community/xperience-search';
import type { MountConfig, Widget } from '@xperience-community/xperience-search';
import { withFacetList } from '@xperience-community/xperience-search/behaviors';

/** The one identifier the JavaScript side uses, so the two registrations cannot drift. */
export const WIDGET_TYPE = 'myCompany.dropdownFacet';

export interface DropdownFacetParams extends Record<string, unknown> {
  /** The element to render into. In Page Builder this is the `.xps-mount` element itself. */
  container: HTMLElement;
  /** Visible label of the select. Defaults to "Filter". */
  label?: string;
  /** Text of the option that applies no filter. Defaults to "All". */
  allLabel?: string;
}

const option = (value: string, text: string, selected: boolean): string =>
  `<option value="${escapeHtml(value)}"${selected ? ' selected' : ''}>${escapeHtml(text)}</option>`;

export const dropdownFacet = withFacetList<DropdownFacetParams>((renderOptions, isFirstRender) => {
  const { items, apply, canApply, params } = renderOptions;
  const { container, label = 'Filter', allLabel = 'All' } = params;

  if (isFirstRender) {
    const id = widgetId(container, 'dropdown-facet', 'control');
    container.innerHTML = `<div class="xps xps-stack xps-select">
  <label class="xps-select__label" for="${id}">${escapeHtml(label)}</label>
  <select class="xps-select__control" id="${id}"></select>
</div>`;

    const control = container.querySelector('select');
    control?.addEventListener('change', () => {
      // The applied value is read back from the DOM, not from `renderOptions`: this listener is
      // registered once and would otherwise close over the first render's items. It is written
      // back here too, because a re-render is queued on a microtask — two changes in a row can
      // both happen before one arrives.
      const previous = control.dataset['xpsActive'] ?? '';
      control.dataset['xpsActive'] = control.value;
      if (previous !== '') apply(previous); // single select: clear what was chosen before
      if (control.value !== '') apply(control.value);
    });
  }

  const select = container.querySelector('select');
  const root = container.querySelector('.xps-select');
  if (!select || !root) return;

  const active = items.find((item) => item.isActive);
  select.innerHTML =
    option('', allLabel, active === undefined) +
    items.map((item) => option(item.value, `${item.label} (${item.count})`, item.isActive)).join('');
  // State is authoritative: routing or a clear-filters widget can change it behind our back.
  select.value = active?.value ?? '';
  select.dataset['xpsActive'] = active?.value ?? '';
  select.disabled = !canApply;
  root.classList.toggle('xps-select--disabled', !canApply);
});

/**
 * Makes the control resolvable from `data-xps-widget="myCompany.dropdownFacet"`.
 * The mount config is editor-supplied JSON, so `readMountConfig` narrows it; a missing
 * `attribute` throws, which the bootstrap turns into one `console.error` and a skipped widget.
 */
export function registerDropdownFacet(): void {
  registerWidgetType(WIDGET_TYPE, (config: MountConfig): Widget =>
    dropdownFacet({
      container: config.container,
      ...readMountConfig(config, {
        attribute: 'string',
        label: 'string?',
        allLabel: 'string?',
      }),
    })
  );
}

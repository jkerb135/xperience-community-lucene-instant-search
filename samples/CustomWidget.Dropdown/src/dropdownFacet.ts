/**
 * `myCompany.dropdownFacet` — a single-select `<select>` facet control built on the published
 * `withFacetList` behaviour (docs/custom-widgets.md, "A dropdown facet in 40 lines").
 */
import { escapeHtml, registerWidgetType } from '@yourco/xperience-search';
import type { MountConfig, Widget } from '@yourco/xperience-search';
import { withFacetList } from '@yourco/xperience-search/behaviors';
import type { FacetListBehaviorParams } from '@yourco/xperience-search/behaviors';

/** Widget-specific parameters; `attribute`, `limit`, `sortBy`… come from the behaviour. */
export interface DropdownFacetParams extends Record<string, unknown> {
  /** The element the control renders into. */
  container: HTMLElement;
  /** Visible label of the select. */
  label?: string;
  /** Text of the "no filter" option. */
  allLabel?: string;
}

export const WIDGET_TYPE = 'myCompany.dropdownFacet';

/** Ids must be unique across the page; a Page Builder mount element carries no id of its own. */
let sequence = 0;

const option = (value: string, text: string, selected: boolean): string =>
  `<option value="${escapeHtml(value)}"${selected ? ' selected' : ''}>${escapeHtml(text)}</option>`;

export const dropdownFacet = withFacetList<DropdownFacetParams>((renderOptions, isFirstRender) => {
  const { items, apply, canApply, params } = renderOptions;
  const { container } = params;
  const label = params.label ?? 'Filter';
  const allLabel = params.allLabel ?? 'All';

  if (isFirstRender) {
    const instance = container.id || container.getAttribute('data-xps-instance') || 'default';
    const selectId = `xps-${instance}-dropdown-facet-${++sequence}-select`;

    container.innerHTML = `<div class="xps xps-dropdown-facet xps-stack">
  <label class="xps-dropdown-facet__label" for="${selectId}">${escapeHtml(label)}</label>
  <select class="xps-dropdown-facet__select" id="${selectId}"></select>
</div>`;

    const control = container.querySelector('select');
    control?.addEventListener('change', () => {
      // The active value is read back from the DOM rather than from `renderOptions`, which is
      // rebuilt on every render and would be stale by the time this listener runs. It is written
      // back here as well: a render does not necessarily happen between two changes.
      const previous = control.dataset.xpsActive ?? '';
      control.dataset.xpsActive = control.value;
      if (previous !== '') {
        apply(previous); // single select: clear what was selected before
      }
      if (control.value !== '') {
        apply(control.value);
      }
    });
  }

  const select = container.querySelector('select');
  const root = container.querySelector('.xps-dropdown-facet');
  if (!select || !root) {
    return;
  }

  const active = items.find((item) => item.isActive);
  select.innerHTML =
    option('', allLabel, active === undefined) +
    items.map((item) => option(item.value, `${item.label} (${item.count})`, item.isActive)).join('');
  select.value = active?.value ?? '';
  select.dataset.xpsActive = active?.value ?? '';
  select.disabled = !canApply;
  root.classList.toggle('xps-dropdown-facet--disabled', !canApply);
});

/**
 * Makes the control resolvable from `data-xps-widget="myCompany.dropdownFacet"`.
 * Mount configuration is editor-supplied JSON, so every value is validated before use.
 */
export function registerDropdownFacet(): void {
  registerWidgetType(WIDGET_TYPE, (config: MountConfig): Widget => {
    const attribute = text(config.attribute);
    if (attribute === undefined) {
      throw new Error(`${WIDGET_TYPE}: "attribute" is required in data-xps-config.`);
    }

    const params: DropdownFacetParams & FacetListBehaviorParams = {
      container: config.container,
      attribute,
      label: text(config.label),
      allLabel: text(config.allLabel),
    };
    return dropdownFacet(params);
  });
}

const text = (value: unknown): string | undefined =>
  typeof value === 'string' && value !== '' ? value : undefined;

// The per-widget subpath entries (PK-1), kebab export name -> camelCase source module.
// rollup.config.mjs builds these, scripts/package-check.mjs asserts package.json agrees with them,
// and scripts/build-styles.mjs compiles the matching per-widget CSS. `clearFilters` ships inside
// `active-filters`, as it does in source.
export const WIDGET_ENTRIES = {
  'active-filters': 'activeFilters',
  'category-tree': 'categoryTree',
  'facet-list': 'facetList',
  'load-more': 'loadMore',
  pagination: 'pagination',
  'range-filter': 'rangeFilter',
  'result-stats': 'resultStats',
  results: 'results',
  'search-box': 'searchBox',
  'sort-select': 'sortSelect',
  suggestions: 'suggestions',
  'toggle-filter': 'toggleFilter',
};

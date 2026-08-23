# Spec amendment — `FacetValue.path` (ACCEPTED)

- **Status:** accepted — owner approval 2026-08-23; implemented by unit CR-3
- **Date:** 2026-08-23
- **Amends:** §4.2 (`FacetValue` in the response), §5.3 (`categoryTree` is no longer a reserved name without a widget)
- **ADR:** [ADR-0018](../../adr/0018-hierarchical-facets.md)

## §4.2 — `FacetValue` gains one optional member

```jsonc
{
  "value": "espresso",          // required — the tag code name
  "label": "Espresso",          // required — the tag title
  "count": 8,                   // required
  "path": ["coffee", "machines"] // OPTIONAL — new
}
```

`path` is the code names of the value's ancestors, **root first, excluding the value itself**. It is
**absent** — not `null`, not `[]` — for a root-level taxonomy value and for every non-taxonomy
attribute.

Two guarantees come with it, so a client can build a tree from one facet's values and nothing else:

- every ancestor a `path` names is itself present among the same facet's values, with its own count;
- a count rolls up — a document tagged *Espresso* is counted under *Espresso*, *Machines* and
  *Coffee* — and so does a filter: `{"attribute":"category","values":["coffee"]}` matches every
  document tagged with a descendant of *Coffee*.

The dimension itself stays flat. `value` is one tag code name, never a `"lvl0 > lvl1"` string, and
`filters.facets` is unchanged.

## Versioning

Additive and optional: an existing client that ignores `path` behaves exactly as before. This is a
**minor** version of both packages. `X-XpSearch-Api-Version` carries the semver *major*
(ADR-0006 decision 3) and stays at `1`.

## §5.3 — `categoryTree`

`categoryTree` was a reserved widget name with a markup contract and no implementation, because the
contract could not describe a hierarchy. With `path` it ships: the `withCategoryTree` behaviour, the
default renderer against `themes/fixtures/category-tree.html`, and the `XpSearch.CategoryTree` Page
Builder widget.

## Not amended

- The facet request shape (`facets`, `filters.facets`) — unchanged.
- The response's ordering rule (count descending, then value ascending) — unchanged, and applied
  across the whole dimension rather than per level.

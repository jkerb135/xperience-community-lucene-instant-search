# ADR-0018: hierarchical taxonomy facets are a flat dimension plus ancestors

- **Status:** accepted — owner approval 2026-08-23 (unit CR-3)
- **Date:** 2026-08-23
- **Spec reference:** §4.2 (`FacetValue`), §5.3 (`categoryTree`), §5.7 — see the [spec amendment](../spec/amendments/2026-08-23-facet-value-path.md)
- **Amends:** ADR-0001 (faceting approach), ADR-0010 (the owned contract)

## Context

An Xperience taxonomy is a tree: tags are organised in parent-child relationships, and *taxonomy
navigation* — drill down from *Coffee* to *Machines* to *Espresso* — is the facet UI editors ask for
by name. The product had a markup contract and a fixture for a `categoryTree` widget, a reserved
widget name, and nothing behind either, because the contract could not describe a hierarchy:
`FacetValue` was `{ value, label, count }` and `TaxonomyFacetProvider` emitted no ancestry
(the `categoryTree` entry in `docs/internal/KNOWN-LIMITATIONS.md`, now removed).

Two decisions were needed: what the wire carries, and how Lucene counts it.

## Options considered

### What the wire carries

| Option | Pros | Cons |
|---|---|---|
| Algolia's `lvl0`/`lvl1`/`lvl2` attributes | Familiar to a migrating team | One facet attribute per depth, all of them declared up front; the tree's depth becomes index configuration; `"Coffee > Machines"` is a string with a delimiter to escape |
| `FacetValue.parent?: string` | Smallest addition | A renderer walks parent links to find a node's depth, one lookup per level; a value's position needs the whole list resolved before anything can be drawn |
| **`FacetValue.path?: string[]`** | The value knows where it is in one member; the provider produces it in the pass it already makes; additive, so an existing client ignores it | Repeats the ancestors on every value (a few bytes per value) |

### How Lucene counts it

| Option | Pros | Cons |
|---|---|---|
| `FacetsConfig.SetHierarchical(dim, true)` and one `FacetField` per full path | The engine's own hierarchy support; `GetTopChildren(dim, "Coffee")` walks a level | The dimension stops being flat: `value` becomes a path, every existing drill-down and every stored term changes shape, and `DrillDownQuery.Add(dim, value)` in `ExecuteSearchStage` would need a path-aware overload. A hierarchical dimension also cannot be multi-valued in the way a tag set needs |
| Query-time roll-up: count leaves, sum into ancestors in the provider | No index change | The provider would have to know the taxonomy, and *filtering* on a parent still would not match its children — a second, different mechanism |
| **Index-time roll-up: write every ancestor as an ordinary value of the same flat dimension** | Counts roll up because the ancestor really is on the document; a drill-down on a parent matches its descendants with **no change to `ExecuteSearchStage`**; `value` stays one tag code name | The document carries a few extra terms; ancestry is fixed at index time, so a moved tag needs a rebuild |

## Decision

1. **`FacetValue.path?: string[]`** — the code names of the value's ancestors, root first, excluding
   the value itself. Absent (not empty) for a root-level value and for every non-taxonomy attribute.
   Additive, so it is a **minor** version: `X-XpSearch-Api-Version` is the semver *major* and stays
   at `1`.
2. **The dimension stays flat.** `XpSearchIndexingStrategy.AddTags` writes each ancestor of a tag as
   its own `FacetField` + `StringField`, deduplicated per document, **before** the tag itself. No
   `SetHierarchical`, no path-shaped values, no query-side change.
3. **Ancestry travels in the label term.** `<dimension>_label` already carried
   `code name ␟ title`; it now carries `code name ␟ path ␟ title`, with the ancestors joined by the
   ASCII record separator. The title comes last because it is the only free-text part.
   `TaxonomyFacetProvider` reads the whole map out of the term dictionary in one enumeration, as it
   already did for labels, so `path` costs no per-document lookup and no second request.
4. **Ancestry is resolved through a seam,** `ITagAncestrySource`, whose default implementation reads
   the tag table once through `IInfoProvider<TagInfo>` and caches it on `cms.tag|all`.
5. **The contract promises the tree is buildable from one facet's values alone:** every ancestor a
   `path` names is itself among the values, with its own count.

## Evidence

- **`Tag` does expose a parent.** `CMS.ContentEngine.Tag` has `ID` and `ParentID` (verified in the
  XML documentation shipped with `Kentico.Xperience.Core` 31.8.0; the
  [Taxonomies API examples](https://docs.kentico.com/api/content-management/taxonomies) document the
  same relationship on `TagInfo.TagParentID`). It is not enough on its own:
  `ITaxonomyRetriever.RetrieveTags(identifiers, language)` returns only the tags asked for, and
  `RetrieveTaxonomy` needs a taxonomy **name**, which nothing maps a tag identifier to. Hence the
  seam over `IInfoProvider<TagInfo>` rather than a second retriever call.
- **The roll-up is what makes the drill-down work.** A document tagged with the grandchild `latte`
  carries `drinks`, `espresso-drinks` and `latte`; `Topic:drinks` therefore matches it through the
  ordinary `DrillDownQuery` (`HierarchicalFacetTests`).
- **Ancestors first is not cosmetic.** Lucene's `TopOrdAndInt32Queue` breaks a count tie in favour of
  the lower taxonomy ordinal, and ordinals are assigned in the order the paths reach the taxonomy
  writer. Writing ancestors first means an ancestor never loses the top-N cut to its own descendant,
  so promise 5 holds for free on an index this library wrote. The provider still tops missing
  ancestors up from the full count list, for an index written before a tag was moved.

## Consequences

- **Index format change: existing indexes must be rebuilt.** A document written by the previous
  version carries no ancestors and a two-part label term. Both degrade rather than break — the
  two-part form still parses, with no path — but counts do not roll up until a rebuild.
- **`XpSearchIndexingStrategy` gained a constructor parameter.** A project that derives from it must
  add `ITagAncestrySource` to its own constructor and pass it through.
- `categoryTree` ships: `withCategoryTree`, the default renderer against the existing markup
  fixture, and the `XpSearch.CategoryTree` Page Builder widget.
- Selection in `categoryTree` is one value at a time. That is a consequence of the roll-up, not a
  simplification: *Coffee* already includes *Espresso*, so selecting both says nothing new.
- A deep taxonomy costs terms: a document tagged with a depth-4 tag carries four values in that
  dimension instead of one. The facet counts pay for it too — the top-N cut is per dimension across
  all levels, not per level (see `docs/internal/KNOWN-LIMITATIONS.md`).
- Pushed external documents are unaffected: they carry no `FacetField` at all
  (`ExternalDocumentFactory`), so they neither gain nor lose a hierarchy.

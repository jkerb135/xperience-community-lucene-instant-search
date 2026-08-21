# ADR-0010: the contract and JS API are owned, not an Algolia mirror

- **Status:** accepted — owner approval 2026-08-21; supersedes ADR-0006, amends ADR-0007
- **Date:** 2026-08-21
- **Spec reference:** §4.2, §4.3, §5.2, §5.3, §5.7, §6, §9.1, §11.3 — see the [spec amendment](../spec/amendments/2026-08-21-owned-contract.md)

## Context

The spec chose to mirror Algolia's wire shape and InstantSearch's API as a migration path. The owner has
decided the product's shape must be **custom-owned**, with Algolia compatibility delivered as a migration
guide instead. The current contract (ADR-0006) also carries two costs that exist only because of the
mirror: an open result object whose reserved members (`_score`, `_highlights`, `objectID`) share a
namespace with customer attributes (needing a hand-written C# extension-data partial), and two
string filter grammars (`attr:value`, `price<=50`) with escaping rules.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Keep the mirror (ADR-0006) | Done; drop-in for Algolia UIs | Shape not ours; collision-prone open object; string grammars; reads as a port |
| Rename fields only | Smallest diff | Keeps the structural costs |
| **Own shape + names, ship a migration guide** | Closed result object, typed filters, facet labels; API reads as one product; guide is versioned and honest | One coordinated rewrite across schema, js-core, theming, core-api, js-widgets |

## Decision (proposed)

Adopt the amendment: closed `results[].attributes`, structured `filters`, facet arrays with `label`,
one-based `page`/`pageSize`/`total`, `with*` behaviours, `SearchActions`, widget names `results`/`facetList`/…,
and a maintained `migrating-from-algolia.md` generated from `contract/algolia-map.json`.

## Evidence

Not a performance decision. The mechanical cost is bounded: the schema is the single source, both
type sets regenerate, and `contract:check` plus the themes `check` script fail until every consumer is
renamed — which is the point.

## Consequences

- The contract freeze is re-done once, before any external consumer exists. After this, changes are semver-major.
- `Hit.cs` and its KNOWN-LIMITATIONS entry disappear; the TS `Hit` generic becomes `Result<TAttributes>`.
- Algolia migration is a documented mapping that can say "no equivalent" where true.
- Anyone reading the code sees one naming system, not two.

# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/). Versioning: [SemVer](https://semver.org/).

Breaking changes to the public connector API (spec §5.7) or the JSON contract
(spec §4.2) are always major-version events.

## [Unreleased]

- Added: the JSON search contract is frozen — `contract/xpsearch-api.schema.json` generates the C#
  (`XpSearch.Core.Contract`) and TypeScript (`@yourco/xperience-search`) types for `/api/xpsearch/query`,
  `/suggest` and `/events`, versioned by the `X-XpSearch-Api-Version` response header (ADR-0006).
- Added: `XpSearch.Core` serves the contract — an ordered, injectable query pipeline behind
  `POST /api/xpsearch/query`, `/suggest` and `/events`, with taxonomy facets and drill-sideways counts,
  facet and numeric filters, sorting, XSS-safe highlighting, a short-TTL response cache invalidated on
  index writes, and `XpSearchIndexingStrategy`, which binds Xperience taxonomies as facets with no
  per-content-type code (spec §4, ADR-0008).

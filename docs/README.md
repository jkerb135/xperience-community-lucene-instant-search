# Documentation

| Folder | Purpose | Audience |
|---|---|---|
| `spec/` | Frozen product specification. Changes are versioned and dated. | Implementers |
| `adr/` | Architecture Decision Records — one per resolved open decision. | Implementers |
| `guides/` | Published user-facing documentation. Ships with the product. | Customers |
| `api/` | Generated API reference (C# XML docs, TSDoc). Do not hand-edit. | Customers |
| `internal/` | Build prompts, notes, competitive analysis. Never published. | You |

**Rule:** `spec/` describes what to build; `guides/` describes how to use what was built.
They drift apart the moment you let one serve both purposes.

## Wiki-readiness (standing requirement)

`guides/` is written so it can be published directly as a GitHub wiki (Home + one page per topic, cross-linked, no dangling context from this repo's internal layout). Every guide page:

- Stands alone — a reader who lands on this one page via search has enough context, without re-reading `spec/` or other guides first.
- Leads with a working code sample before the explanation — the sample is copy-pasteable and was run against the actual implementation, not invented from the spec's description of intended behaviour.
- Uses `##` for the page title (GitHub wiki convention) and relative `[[Page Name]]`-style or plain markdown links to other guide pages for cross-references, so the page set works both in this repo and pasted into a wiki.
- Names code identifiers, endpoints, and options exactly as implemented — verify against the current source before writing, the same rule that governs Xperience API citations.

This applies to every guide-facing doc a subagent produces (`docs/guides/*.md`, and any per-widget or per-connector reference pages added alongside `XpSearch.Client`). It is part of each subagent's Definition of Done, not a Phase 8 cleanup pass — write the guide page alongside the code it documents, in the same unit of work, so the sample is verified while the implementer still has the working example in front of them.

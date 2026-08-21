# ADR-0011: this library is its own repository

- **Status:** accepted — owner decision 2026-08-21
- **Date:** 2026-08-21
- **Spec reference:** §2, §2.1 (monorepo conventions), §11.1 (monorepo packaging notes)

## Context

The spec placed this library inside a `CommunityProjects` monorepo with root-owned shared build
settings, central package versions, a single solution and path-filtered CI. In practice the umbrella
folder held the Dancing Goat host application and two sibling repositories referenced by relative path;
no other library ever used the shared layer. On 2026-08-21 the owner created a dedicated GitHub
repository for this library (`jkerb135/xperience-community-lucene-instant-search`) and asked for the
development worktrees to live there.

## Decision

- This repository is canonical. The umbrella's history for the `libraries/xperience-search` subtree was
  imported with `git subtree split`, so Phase 0 onward is preserved.
- The shared settings move into the repository root: `Directory.Build.props` (target framework,
  language version, nullable, warnings-as-errors, analyzers, plus the library's version and package
  metadata) and `Directory.Packages.props` (central package versions). Projects still reference packages
  without versions.
- Default branch `main`. Unit branches are `unit/<name>`; agents work in `.claude/worktrees/<name>`
  (gitignored) inside this repository.
- The umbrella folder is no longer a git repository. It still hosts the Dancing Goat sample application
  and `CommProjects.sln`, which references this repository's projects by relative path; the full-solution
  build there remains the post-merge integration check, and the host wiring unit edits `src/` there.

## Consequences

- Spec §2.1's monorepo rules ("root owns shared settings", "no cross-library references", "solution
  folder per library", "CI path filtering") are superseded; the invariants that still matter — central
  package management, one shared props file, no inline versions, `XpSearch.*` vs the published package
  id — carry over unchanged.
- `.github/workflows/` in this repository is now where CI actually runs (the spec's path-filter note no
  longer applies).
- Branding (`YourCo.Xperience.Search.*`, `@yourco/xperience-search`) is unchanged; the owner deferred the
  rename to Phase 8.

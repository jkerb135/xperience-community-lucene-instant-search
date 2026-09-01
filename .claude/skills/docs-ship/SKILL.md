---
name: docs-ship
description: Reconcile docs/guides/ + CHANGELOG with code changes since the last docs ship, dispatch doc-specialist agents for gaps, regenerate breaking-changes, and sync the guides + images to the GitHub wiki. Run after unit merges or before a release.
---

# /docs-ship — update and publish the user docs

You are the lead (Fable). This skill runs the docs half of the spec→implement→review loop. Docs are WRITTEN by `doc-specialist` subagents (Opus) and REVIEWED by you; screenshots are captured by you in the in-app browser (subagents have no browser tools). Never let a doc-specialist review its own work.

Repo: `F:/Personal/CommunityProjects/libraries/xperience-search`. Wiki: `git@github.com:jkerb135/xperience-community-lucene-instant-search.wiki.git`. Scope every glob to `docs/` and `src/` — `.claude/worktrees/` holds full stale repo copies.

## 1. Gap check

- Find the last ship tag: `git tag --list 'docs-ship/*' --sort=-creatordate | head -1`. No tag yet → diff from the merge-base the caller names, or treat everything as in-scope on the first run.
- `git diff --name-only <tag>..HEAD -- src/ contract/ themes/` → changed surface. Cross-reference against:
  - `docs/guides/*.md` — does each changed user-facing surface (widget properties, admin pages, options classes, JS client API, contract) have a current guide section? Check identifiers in the guide against the source, not the diff summary.
  - `docs/internal/screenshot-manifest.md` — any manifest row whose *Source files* column intersects the diff is STALE; list those captures.
  - `CHANGELOG.md` `[Unreleased]` — every merged unit since the tag must have an entry (one per `merge:` commit).
- Report the gap list to the user before doing work if it is large or surprising; otherwise proceed.

## 2. Fill gaps

- For prose/sample gaps: write or update a mini-spec (`docs/internal/units/DOC-<n>.md` for big batches; inline spec sections in an existing DOC unit file for small ones — never inline-prompt-only, per the standing workflow rule) and dispatch `doc-specialist` agents. Review each report against the spec: APPROVED / REVISE (SendMessage, 3-cycle cap) / REJECT.
- For stale screenshots: recapture yourself in the in-app browser following the manifest row's reproduction steps, same viewport (1440-wide desktop, light theme), overwrite the PNG in `docs/guides/images/`, update the manifest row's date.

## 3. Changelog + breaking changes

- Source-breaking entries in CHANGELOG carry a `**Breaking (scope):**` lead (see CHANGELOG header). Legacy entries use `**Changed (breaking, …)**` or an inline bolded `**Breaking**` — scan for all of them (`grep -in 'breaking' CHANGELOG.md`, then judge each hit; header/prose mentions don't count). Regenerate `docs/guides/breaking-changes.md` from them: SemVer policy paragraph, then per-version (plus Unreleased) a table of breaking entries with migration notes. Keep entries verbatim-faithful to the CHANGELOG; add migration guidance only from verified source reading.
- On a release ship (caller says so, with a version): roll `## [Unreleased]` into `## [<version>] - <date>` per Keep a Changelog and start a fresh Unreleased section.

## 4. Verify

- Link check over `docs/guides/`: every relative `](x.md...)` and `![](images/...)` target exists. One-liner:
  `cd docs/guides && grep -oE '\]\(([^)#h][^)#]*)' *.md | sed 's/.*(//' | sort -u | while read f; do [ -e "$f" ] || echo "MISSING $f"; done`
- Every image in `docs/guides/images/` has a manifest row, and vice versa.
- Samples in touched pages were run (doc-specialist reports prove it; spot-check one yourself).

## 5. Sync to GitHub Pages (needs owner confirmation before push)

Publish target (owner decision 2026-09-01, supersedes the wiki): the `gh-pages` branch, rendered
by GitHub Pages' built-in Jekyll at
`https://jkerb135.github.io/xperience-community-lucene-instant-search/`.

- Clone the repo's `gh-pages` branch into the scratchpad (`git clone -b gh-pages --single-branch <origin> pages`; on the first ship create it orphan).
- Sync `docs/guides/*.md` + `docs/guides/images/` → branch root, deleting pages that no longer exist in `docs/guides/`. Keep the branch's site chrome: `_config.yml` (title, `theme: jekyll-theme-primer`, `relative_links: {enabled: true, collections: true}` so `[x](page.md)` links resolve to the rendered pages), `_layouts/default.html` + `_data/nav.yml` (the side navigation — add/remove a nav.yml row whenever a guide page is added or removed), and `index.md` (the landing page — replaced by the real Home/D3 page when it lands; until then a generated per-audience index).
- NEVER copy anything from `docs/internal/`, `docs/api/`, or the spec/ADRs.
- Show the owner a summary of pages added/changed/removed, then commit (`docs ship <date>`) and push ONLY after they confirm (a standing "ship it" from the owner in the session counts).
- Tag the main repo: `git tag docs-ship/<YYYY-MM-DD>` (suffix `-2` etc. if same-day) and push the tag.
- Spot-check 3 pages live (Pages build takes ~1 min; check the repo's Actions tab for the pages-build-deployment run), images and cross-links included. If Pages isn't enabled yet: `gh api repos/jkerb135/xperience-community-lucene-instant-search/pages -X POST -f "source[branch]=gh-pages" -f "source[path]=/"`.

## 6. Record

- Commit doc changes on main (docs-only, `docs: …` conventional commit) or merge the DOC unit branch per the normal loop.
- Append a phase-log row if this ship closed a unit or release; update session-state memory.

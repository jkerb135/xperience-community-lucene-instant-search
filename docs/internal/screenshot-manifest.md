# Screenshot manifest

Every image under `docs/guides/images/` has a row here; every row has an image. Captures are
taken by the lead in the in-app browser at a 1440-wide desktop viewport, light theme, against
the Dancing Goat host (`localhost:27340`, admin at `/admin`). A row is STALE when any file in
its *Source files* column changed since its *Captured* date — `/docs-ship` step 1 checks this.

File naming: `<page-slug>--<state>.png` (e.g. `rules--builder-boost.png`). Alt text lives in the
guide pages, not here.

| Image | URL / route | Reproduction steps (state + data prerequisites) | Source files (staleness triggers) | Captured |
|---|---|---|---|---|

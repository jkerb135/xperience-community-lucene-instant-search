## Experiments — A/B testing your tuning

You can tune a search index all day and still not know whether you improved it. An **experiment**
answers that: it splits the index's real traffic between the tuning that is live now (**variant A**)
and a copy of it that you edit freely (**variant B**), and reports what visitors did on each side.

You do not need a developer for anything on this page. Everything is done in the Xperience
administration.

### Where the experiments are

Experiments belong to one search index, like the rest of the tuning:

**Lucene Search → indexes → click the index → Experiments**

| Page | URL | What it is |
|---|---|---|
| **Experiments** | `…/{id}/experiments` | Every experiment of this index, with its state, split and outcome. |
| **Overview** | `…/{id}/experiments/{experiment}/detail` | The one experiment: its split, its report, and how to start or end it. |
| **Rules / Synonyms / Field weights / Stopwords** | `…/{id}/experiments/{experiment}/rules` … | Variant B's tuning — the same editors the live tuning uses. |

**Permissions.** As with the rest of the tuning: *View* on **Lucene Search** to read, *Create* to make
an experiment, *Update* to change its split, start it or conclude it.

### The whole walk, in six steps

1. **Create it.** Open **Experiments** → **New experiment**. Give it a name that says what you are
   testing ("Boost recent articles") and the share of traffic variant B should get (1–99, 50 is a
   sensible default). Creating it **copies the index's entire live tuning** — every rule, synonym,
   stopword and field weight — into variant B. Nothing is live yet, and nothing about the live tuning
   changed.
2. **Edit variant B.** Open the experiment and use the **Rules**, **Synonyms**, **Field weights** and
   **Stopwords** tabs inside it. They are the same editors as the live ones, with a
   *Variant B draft — <experiment>* banner on every page so you always know which set you are
   changing. Add, edit and delete freely: no visitor sees any of it yet.
3. **Try it before anyone else does.** The **Query tester** grows a **Variant** select while an
   experiment exists on the index. Pick *Variant B of <experiment>* and run a query to see exactly
   what a bucketed visitor would get.
4. **Start it.** Back on the experiment's **Overview**, press **Start experiment** and confirm. From
   that moment every visitor is bucketed: the configured share is answered from variant B, the rest
   from the live tuning. The split and variant B are frozen once it runs — a test whose halves change
   half-way through measures nothing.
5. **Read the report.** The Overview shows both variants side by side: searches, zero-result rate,
   click-through rate and average clicked position, over the time the experiment has been running.
6. **Conclude it.** **Promote B to live** deletes the index's live tuning rows and makes variant B's
   rows the live ones. **Discard B** deletes variant B and leaves the live tuning untouched. Both are
   confirmed first, both take effect immediately for every visitor, and neither can be undone.

### Reading the report honestly

The report shows **observed rates and the sample sizes they were observed over** — nothing else:

| Figure | What it means |
|---|---|
| **Searches** | How many searches that variant answered. This is the sample size. |
| **Zero-result rate** | Searches that found nothing, over searches. Lower is better. |
| **Click-through rate** | Searches that led to a click, over searches. Higher is usually better. |
| **Average clicked position** | Where in the list visitors clicked. Lower is better — they found it sooner. |

There is deliberately **no winner badge, no p-value and no "significant" label**. Two rates measured
over a few hundred searches differ by chance most of the time, and a badge saying otherwise would be
a lie dressed as a number. Let the experiment run until the sample sizes are large enough that you
would bet your own money on the difference, then conclude it yourself.

The metrics are the same ones the **Analytics** page computes, split by the variant stamped on each
logged search — so an experiment's numbers and the dashboard's numbers can never drift apart.

### What a visitor experiences

- **Bucketing is sticky.** Each visitor gets a first-party cookie, `xpsearch_bucket`, holding a random
  identifier that means nothing on its own. The variant is derived from that identifier and the
  experiment's own id, so the same visitor stays in the same half for the whole experiment, on every
  server.
- **It works without tracking consent.** The cookie is registered at Xperience's **Essential** cookie
  level, because it identifies nothing and is needed for the site to answer consistently. Visitors who
  refuse tracking are still part of the experiment — unlike search *activities*, which need consent.
- **The first server-rendered paint leans towards A.** The **Search - Results** widget renders its
  first page on the server. If the visitor has no bucket cookie yet, that very first render is
  answered from the live tuning, and the cookie is set for every search after it. In practice this
  means the first page view of a brand-new visitor counts towards A; every search they run afterwards
  is bucketed normally.
- **Nothing in the JSON contract changes.** The search response is identical whichever variant answered,
  and a JavaScript client needs no changes to work while an experiment runs.

### Rules of the road

- **One experiment per index at a time.** A new one can only be created once the current one has been
  concluded. (You can run experiments on different indexes at the same time.)
- **A draft never leaks.** Every read of the live tuning asks for rows with no experiment on them, so
  variant B is invisible to everyone until the experiment starts, and to variant A always.
- **Variant B is frozen once the experiment runs.** Its editors turn read-only, and a save submitted
  anyway is refused.
- **Concluding is immediate.** Promoting or discarding drops the affected caches, so the next search —
  on any server — is answered by the tuning you chose. No restart.
- **The zero-result "Create rule" shortcut on the Analytics page always writes a live rule**, never a
  variant-B one. Seed the live tuning there; copy it into an experiment by creating the experiment
  afterwards.

### After the experiment

Concluding does not delete the experiment: it stays in the listing with its outcome (*Promoted* or
*Discarded*), its start and end times, and its final report — the same figures, bounded to the window
it actually ran in. That is your record of why the tuning looks the way it does.

If you promoted variant B, the live tuning **is** variant B now: open **Rules**, **Synonyms**,
**Field weights** or **Stopwords** in the index sidebar and you will find the rows you edited in the
draft. To carry on testing, create a new experiment — it clones the new live tuning in turn.

### This is a search-tuning A/B, not a page A/B

An experiment tests what the search **returns** on one index, and measures itself. If what you want to
test is what a **page says** — a banner, a headline, a call to action — reach for the
**Search - A/B bucket** personalization condition instead: it splits visitors into two sticky buckets
with the same cookie, on any widget on any page, and you read the outcome in Analytics rather than in
a built-in report. See [Search-driven personalization](search-personalization.md). The two are
independent (each split is hashed with its own name), so running both at once is fine.

### See also

- [Search-driven personalization](search-personalization.md) — personalizing any widget by what a
  visitor searched for, and page-level A/B splits.
- [Relevance tuning](relevance-tuning.md) — the rules, synonyms, stopwords and field weights an
  experiment tests.
- [Analytics](analytics.md) — the same metrics for all traffic, and where the query log comes from.

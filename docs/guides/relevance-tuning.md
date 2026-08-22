## Relevance tuning

You know your visitors better than any ranking algorithm does. Tuning is where you tell search what
to do about that — promote a product during a campaign, hide a discontinued page, teach it that
"sofa" and "couch" are the same word.

You do not need a developer for anything on this page. Everything is done in the Xperience
administration.

### Where the tuning pages are

Tuning belongs to one search index, so it lives inside the index:

**Lucene Search → indexes → click the index → the *Tuning* sidebar**

The sidebar has one entry per kind of tuning:

| Sidebar entry | URL | What it is |
|---|---|---|
| **Settings** | `/admin/lucene/indexes/tuning/{id}/settings` | The index's own configuration (strategy, analyzer, channels) — the Lucene integration's form. |
| **Rules** | `…/{id}/rules` | Pin, bury, boost, filter and redirect rules. |
| **Synonyms** | `…/{id}/synonyms` | Words that mean the same thing. |
| **Stopwords** | `…/{id}/stopwords` | Words ignored when someone searches. |
| **Field weights** | `…/{id}/weights` | How much a match in one field counts. |
| **Query tester** | `…/{id}/query-tester` | The same query with and without your tuning. |
| **Analytics** | `…/{id}/analytics` | What visitors searched for, clicked, and did not find. |
| **Status** | `…/{id}/status` | Document counts, last write, and the rebuild button. |

Everything you see on these pages applies to that one index. You never pick an index on a form; the
index you clicked is the index you are editing, and it is shown read-only at the top of every form.

Two pages are **not** per-index and stay in their own application, **Search ingestion** (under
*Development*): **API keys** and **Ingestion log**.

**Permissions.** These pages are governed by the **Lucene Search** application, not by *Search
ingestion*. A role needs *View* on Lucene Search to read them, *Create*/*Update*/*Delete* to change
them. See *Permissions* at the end of this page.

### Your first rule, in five steps

Say the search *espresso machine* should always show your flagship machine first.

1. Open **Lucene Search** in the administration menu (it sits under **Development**) and select the
   index you want to tune, for example *Products*.
2. Select **Rules** in the sidebar, then **New rule**.
3. Fill in:
   - **Rule name** — `Flagship machine first`. Give it a name you will recognise in six months.
   - **Enabled** — leave selected.
   - **When the visitor's search** — *Contains the words below*.
   - **Words to look for** — `espresso machine`.
   - **Then** — *Pin a result to a position*.
   - **Result id** — the id of the product, for example `f3c1…:en` (see *Finding a result id* below).
   - **Pin to position** — `1`.
   - **Priority** — leave at `100`.
4. Select **Save**.
5. Search for *espresso machine* on the site. Your flagship machine is first.

Nothing is rebuilt and nothing is republished. The change is live within half a minute at worst, and
usually immediately.

### The five things a rule can do

| Then… | What happens | What you fill in |
|---|---|---|
| **Pin a result to a position** | The result is moved to the position you name. If the search did not find it at all, it is added there — as long as it still matches the filters the visitor has selected. | **Result id**, **Pin to position** |
| **Boost a result** | The result is pushed up, but the search still decides the final order. A very relevant result can still beat it. | **Result id** (or **Filter**), **Boost multiplier** |
| **Bury a result** | The result is removed from this search entirely. | **Result id** |
| **Filter the results** | Only results matching the filter are shown. | **Filter** |
| **Redirect the visitor** | The search returns a destination next to its results, and the search box sends the visitor there. | **Redirect URL** |

**Pin or boost?** Pin when the answer is "this exact thing, first, no argument" — a campaign landing
page, a flagship product. Boost when you mean "lean this way" — for example, make everything in the
*Offers* category count a bit more during a sale. Boost keeps the search's own judgement; pin
overrules it. If you are unsure, use boost first: it degrades gracefully when your content changes.

**Bury** is for the page you cannot delete but do not want found: an old campaign, a superseded
product, a legal page that keeps outranking the thing people actually wanted.

**Filter** is written as `Field:value` pairs, separated by commas — for example
`Category:coffee, Tags:brewing`. Both must match. The field names are the attribute names that appear
in your search results — the same ones the facet **Attribute** drop-down lists, including the four every
document has (`title`, `url`, `contentType`, `language`); ask your developer for the list once and keep
it somewhere.

### When a rule runs

- **Enabled** — clear it to switch a rule off without deleting it.
- **Runs from** / **Runs until** — leave both empty and the rule runs forever. Fill them in for a
  campaign and the rule switches itself on and off. Times are UTC.
- **When the visitor's search**:
  - *Contains the words below* — the words appear anywhere in what the visitor typed.
  - *Is exactly the words below* — the visitor typed exactly that and nothing else.
  - *Starts with the words below* — what they typed begins with your words.
  - *Is anything at all* — the rule applies to every search. Use this with **Filter** or **Boost**,
    not with pin.

Upper and lower case never matter.

### When two rules disagree

This is the part worth reading twice.

1. Rules with a **lower priority number run first**. Priority 10 beats priority 100. (Think "first in
   the queue", not "more important".)
2. If two rules have the same priority, the one that was **created first** runs first.
3. For pin and bury, the **first rule to name a result wins**. If rule A pins product X to position 1
   and rule B buries product X, and A has the lower priority number, X is pinned and B is ignored for
   that result.
4. Boost and filter rules all apply, in that same order. Two boosts on the same result both count.
5. For redirect, the **first matching rule wins**, and a redirect rule with an empty **Redirect URL**
   is skipped, so a later one can still fire.

If a rule seems not to be working, the usual cause is another rule with a lower priority number that
got to the same result first.

### Redirect rules

Use one when a search has a single right answer that is not a search result: *returns* should land on
the returns policy, *careers* on the jobs site. Fill in **Redirect URL** with a root-relative path
(`/support`) or a full address (`https://jobs.example.com`).

What happens is deliberately narrow, so a rule cannot trap anyone:

- The search still runs. The response carries the normal results **and** the destination; the page
  decides what to do with it.
- The shipped search box navigates only when the visitor **submits** the search — presses Enter or the
  search button. It never navigates while they are still typing, and never when a search runs because
  someone opened or reloaded a link. So a visitor searching for *returns policy of our supplier* can
  keep typing past the pattern.
- A developer can switch the behaviour off entirely with `followRedirects: false` on the search box
  widget, and read the destination themselves.

The **Query tester** shows the rule as `rule:<name>` in the explanation, the same as every other rule.

### Synonyms

A synonym tells search that different words mean the same thing.

1. **Synonyms → New synonym** in the index's Tuning sidebar.
2. **Direction**:
   - *Two-way* — every word finds every other. `sofa, couch, settee` means someone searching for
     *settee* finds the sofas, and someone searching for *sofa* finds the settees.
   - *One-way* — the **Words** find the **Replacements**, but not the other way round. Use this when
     the words are not really equivalent: `laptop` → `notebook` is fine one-way if you do not want
     someone searching for *notebook* to be shown laptops.
3. **Words** — comma-separated: `sofa, couch, settee`.
4. **Replacements** — only for a one-way synonym. Leave it empty for two-way.
5. **Save**.

Phrases work: `sofa bed, futon` is a valid two-way group, and a search for *cheap sofa bed* uses it
rather than the plain `sofa` group, because the longer phrase always wins.

Synonyms widen a search. They never narrow it: *red sofa* with `sofa = couch` still requires
something red.

### Stopwords

Stopwords are words that are ignored when someone searches — *the*, *a*, *of*, or your own noise
words like *buy* on a shop.

1. **Stopwords → New stopword list** in the index's Tuning sidebar (one list per index; edit the
   existing one if there already is one).
2. **Words to ignore** — one word per line.
3. **Save**.

Two cautions. Removing a word makes searches *broader*, not better — if you make *free* a stopword,
*free shipping* becomes *shipping*. And if a visitor searches for nothing but stopwords, the search
is left alone rather than turned into "show me everything".

### Field weights

A field weight decides how much a match in one place counts compared to another. A match in a title
usually deserves more than a match halfway down a body of text.

1. **Field weights → New field weight** in the index's Tuning sidebar.
2. **Field** — the field name as it appears in your search results, for example `Title`.
3. **Weight** — `1` is normal. `3` makes a match in that field count roughly three times as much.
   `0.5` halves it.
4. **Save**.

Start small. Move one field to `2` or `3`, look at the results, then adjust. Weights of 20 tend to
turn search into "whatever has the word in the title", which is rarely what anyone wanted.

### Finding a result id

Pin, bury and boost all need the **result id** of the thing you are pointing at. It is the `id` in
the search response — a developer can read it from the browser's network tab in a few seconds, or
your site can be configured to expose it. Ask once for the ids of the pages you care about and keep
the list; they are stable and do not change when you edit the page.

### Checking your work: the Query tester

**Query tester** in the index's Tuning sidebar answers the only question that matters after you save
a rule: did it do what you meant?

1. The **Index** is the one you are in — it is shown above the form and cannot be changed.
2. Type the **Query** a visitor would type. Leave **Language** empty unless you are checking one
   language in particular (`en`, `de`, …).
3. Press **Run**.

You get two columns of the same search:

- **With rules** — exactly what a visitor gets right now: your rules, synonyms, stopwords and field
  weights all applied.
- **Without rules** — the same query with none of them. This is the "before" picture.

Every result on both sides shows:

- its **position** and title,
- **score** — the final relevance number, after everything,
- **base score** — the raw text-match score, before any rule or weight touched it. If the two are the
  same, nothing changed that result's score,
- one line per rule, weight or synonym that applied to *that* result, for example
  `rule:Flagship machine first`.

Above the results, **How the query was rewritten** lists what applied to the whole search:
`synonym:couch` (the search was widened with this word), `weight:Title×3` (this field weight applied),
`rule:Winter campaign` (a boost or filter rule applied at query time).

Results that differ between the two columns are marked:

| Mark | Means |
|---|---|
| ▲ Moved up by a rule | Your pin or boost lifted it. |
| ▼ Moved down by a rule | Your bury, or someone else's boost, pushed it down. |
| + Added by a rule | It was not in the plain results at all — a pin put it there. |
| − Removed by a rule | It was in the plain results and your bury or filter took it out. |

Reading it: if the two columns are identical, your rule did not match — check the **When the
visitor's search** condition and the **Words to look for**, and check the schedule. If a result moved
but not far enough, raise the boost or use a pin instead.

Two things worth knowing. The tester always runs a fresh search, so a rule you saved a second ago is
already visible even though live searches may still be served from cache for a moment. And testing
never shows up in **Analytics** — tester runs are not written to the query log.

### The other pages in the sidebar

- **Settings** — the index's own configuration: which content it holds, its strategy and its
  analyzer. This is the Lucene integration's form; it is here so that everything about an index is in
  one place.
- **Analytics** — what visitors searched for on *this* index, what they clicked and, above all, what
  found nothing. See `docs/guides/analytics.md`; every zero-result row has a **Create rule** button
  that opens the rule form with the query already filled in.
- **Status** — how many documents this index holds and where they came from, plus a **Rebuild index**
  button. A rebuild empties the index and writes it again; search results are incomplete while it
  runs, so do it out of hours.

### And two pages that are not per-index

They live in the **Search ingestion** application (under *Development*), because they are about
systems pushing data in rather than about one index:

- **API keys** — for systems that push data into search. When you create a key it is shown **once**,
  in the message at the top of the screen. Copy it then; it cannot be shown again.
- **Ingestion log** — a record of every push into search: who, which index, how many documents and
  whether it worked. Filter it by index. This is the page that answers "who deleted our catalogue".

### Permissions

Because the tuning pages sit inside an index, they are governed by the **Lucene Search** application
in *Role management*, not by *Search ingestion*:

| To… | Grant on **Lucene Search** |
|---|---|
| Read the tuning pages, run the query tester, read analytics | *View* |
| Create rules, synonyms, stopword lists, field weights | *Create* |
| Edit them, edit index settings | *Update* |
| Delete them | *Delete* |
| Rebuild an index | *Rebuild* |

Grants on **Search ingestion** now only cover API keys and the ingestion log.

One wrinkle worth knowing: the index's own edit URL requires *Update*, so a *View*-only role reaches
the sidebar by clicking the index row in the listing (which is what the row click does), not by
opening the index's configuration form first.

### Things that do not work yet

- Pin and bury act on the page of results the visitor is looking at. Pinning to position 3 affects
  the page that contains position 3, not the others.

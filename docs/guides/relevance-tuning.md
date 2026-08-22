## Relevance tuning

You know your visitors better than any ranking algorithm does. **Search tuning** is the application
where you tell search what to do about that — promote a product during a campaign, hide a
discontinued page, teach it that "sofa" and "couch" are the same word.

You do not need a developer for anything on this page. Everything is done in the Xperience
administration.

### Your first rule, in five steps

Say the search *espresso machine* should always show your flagship machine first.

1. Open **Search tuning** in the administration menu (it sits under **Development**).
2. Select **Rules**, then **New rule**.
3. Fill in:
   - **Index** — the search index the rule applies to, for example *Products*.
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

### The four things a rule can do

| Then… | What happens | What you fill in |
|---|---|---|
| **Pin a result to a position** | The result is moved to the position you name. If the search did not find it at all, it is added there — as long as it still matches the filters the visitor has selected. | **Result id**, **Pin to position** |
| **Boost a result** | The result is pushed up, but the search still decides the final order. A very relevant result can still beat it. | **Result id** (or **Filter**), **Boost multiplier** |
| **Bury a result** | The result is removed from this search entirely. | **Result id** |
| **Filter the results** | Only results matching the filter are shown. | **Filter** |

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

If a rule seems not to be working, the usual cause is another rule with a lower priority number that
got to the same result first.

### Synonyms

A synonym tells search that different words mean the same thing.

1. **Search tuning → Synonyms → New synonym**.
2. **Index** — the index it applies to.
3. **Direction**:
   - *Two-way* — every word finds every other. `sofa, couch, settee` means someone searching for
     *settee* finds the sofas, and someone searching for *sofa* finds the settees.
   - *One-way* — the **Words** find the **Replacements**, but not the other way round. Use this when
     the words are not really equivalent: `laptop` → `notebook` is fine one-way if you do not want
     someone searching for *notebook* to be shown laptops.
4. **Words** — comma-separated: `sofa, couch, settee`.
5. **Replacements** — only for a one-way synonym. Leave it empty for two-way.
6. **Save**.

Phrases work: `sofa bed, futon` is a valid two-way group, and a search for *cheap sofa bed* uses it
rather than the plain `sofa` group, because the longer phrase always wins.

Synonyms widen a search. They never narrow it: *red sofa* with `sofa = couch` still requires
something red.

### Stopwords

Stopwords are words that are ignored when someone searches — *the*, *a*, *of*, or your own noise
words like *buy* on a shop.

1. **Search tuning → Stopwords → New stopword list** (one list per index; edit the existing one if
   there already is one).
2. **Index** — the index.
3. **Words to ignore** — one word per line.
4. **Save**.

Two cautions. Removing a word makes searches *broader*, not better — if you make *free* a stopword,
*free shipping* becomes *shipping*. And if a visitor searches for nothing but stopwords, the search
is left alone rather than turned into "show me everything".

### Field weights

A field weight decides how much a match in one place counts compared to another. A match in a title
usually deserves more than a match halfway down a body of text.

1. **Search tuning → Field weights → New field weight**.
2. **Index** — the index.
3. **Field** — the field name as it appears in your search results, for example `Title`.
4. **Weight** — `1` is normal. `3` makes a match in that field count roughly three times as much.
   `0.5` halves it.
5. **Save**.

Start small. Move one field to `2` or `3`, look at the results, then adjust. Weights of 20 tend to
turn search into "whatever has the word in the title", which is rarely what anyone wanted.

### Finding a result id

Pin, bury and boost all need the **result id** of the thing you are pointing at. It is the `id` in
the search response — a developer can read it from the browser's network tab in a few seconds, or
your site can be configured to expose it. Ask once for the ids of the pages you care about and keep
the list; they are stable and do not change when you edit the page.

### Checking your work: the Query tester

**Search tuning → Query tester** answers the only question that matters after you save a rule: did it
do what you meant?

1. Pick the **Index** you tuned.
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

### The other pages in this application

- **Analytics** — what visitors searched for, what they clicked and, above all, what found nothing.
  See `docs/guides/analytics.md`; every zero-result row has a **Create rule** button that opens the
  rule form with the query already filled in.
- **API keys** — for systems that push data into search. When you create a key it is shown **once**,
  in the message at the top of the screen. Copy it then; it cannot be shown again.
- **Index status** — how many documents each index holds and where they came from, plus a **Rebuild
  index** button. A rebuild empties an index and writes it again; search results are incomplete while
  it runs, so do it out of hours.
- **Ingestion log** — a record of every push into search: who, which index, how many documents and
  whether it worked. Filter it by index. This is the page that answers "who deleted our catalogue".

### Things that do not work yet

- **Redirect** appears in the **Then** list and is saved, but nothing acts on it: the search response
  has no redirect field yet. Do not rely on it.
- Pin and bury act on the page of results the visitor is looking at. Pinning to position 3 affects
  the page that contains position 3, not the others.

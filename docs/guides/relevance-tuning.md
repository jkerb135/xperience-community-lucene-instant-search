## Relevance tuning

You know your visitors better than any ranking algorithm does. Tuning is where you tell search what
to do about that — promote a product during a campaign, hide a discontinued page, teach it that
"sofa" and "couch" are the same word.

You do not need a developer for anything on this page. Everything is done in the Xperience
administration.

### Where the tuning pages are

Tuning belongs to one search index, so it lives inside the index:

**Lucene Search → indexes → click the index → the *Edit index* sidebar**

The sidebar has one entry per kind of tuning:

| Sidebar entry | URL | What it is |
|---|---|---|
| **Settings** | `/admin/lucene/indexes/edit/{id}/settings` | The index's own configuration (strategy, analyzer, channels) — the Lucene integration's form. |
| **Rules** | `…/{id}/rules` | If/then rules: pin, hide, boost, bury, filter, rewrite, redirect, custom data. |
| **Synonyms** | `…/{id}/synonyms` | Words that mean the same thing. |
| **Stopwords** | `…/{id}/stopwords` | Words ignored when someone searches. |
| **Field weights** | `…/{id}/weights` | How much a match in one field counts. |
| **Query tester** | `…/{id}/query-tester` | The same query with and without your tuning. |
| **Analytics** | `…/{id}/analytics` | What visitors searched for, clicked, and did not find. |
| **Experiments** | `…/{id}/experiments` | A/B tests of this tuning against a draft copy of it — see [Experiments](experiments.md). |
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
2. Select **Rules** in the sidebar, then **New rule**. The rule builder opens.
3. In the settings strip along the top, type the **Rule name** — `Flagship machine first`. Give it a
   name you will recognise in six months. Leave **Enabled** selected and **Priority** at `100`.
4. Under **Condition(s) — If**, select **Add condition**. A panel slides in from the right. Turn
   **Query** on, leave the operator at *Contains*, type `espresso machine`, and select **Apply**. The
   panel closes and the condition reads back as `Query contains “espresso machine”`.
5. Under **Action(s) — Then**, select **Add action** and pick **Pin an item**. A panel slides in.
   Type a few letters of the machine's name in **Find the item**, select it in the result list, set
   **Position** to `1`, and select **Apply**. The action reads back as
   `1 · Pin an item — Pin Flagship Machine to position 1`. Select **Save rule**.

Search for *espresso machine* on the site. Your flagship machine is first.

Nothing is rebuilt and nothing is republished. The change is live within half a minute at worst, and
usually immediately.

### The rule builder, region by region

The builder is one screen. Nothing is saved until you select **Save rule**, including anything you
did in the side panel.

**The settings strip.** Rule name, Enabled, Priority, and **Runs** - one date-range picker for the
validity window (UTC days; clear it and the rule runs forever; a migrated rule with an open-ended
window shows a note and keeps its single bound until you pick a range). These describe the rule,
not what it matches.

**The If column.** One card per condition, each showing a read-only summary of what it matches —
every part that is turned on, joined with a middle dot:

```
Condition 1   Query contains “grinder” · plurals & synonyms
Condition 2   Filter ProductFieldCategory is Grinders · Contact group Grinder shoppers · any language
```

You do not edit a condition on this screen. **Edit** on a card, or **Add condition** in the dashed
area below them, opens the side panel. A brand-new rule shows a *Start with a condition* tip instead
of cards, and **Save rule** stays disabled until there is one — see *What stops a save* below.

**The side panel.** Three switches: **Query**, **Filters** and **Context**. Turn one on and its
fields appear under it.

- **Query** — the operator (*Contains*, *Is exactly*, *Starts with*), the words, and
  **Match plurals & synonyms** (this is the *analyzed* comparison described below).
- **Filters** — one or more `attribute is value` rows, all of which must be selected on the request.
  The same attribute rows the action panel uses: attribute and value are both drop-downs fed by the
  index, with **Edit as text** behind them.
- **Context** — **Contact group** (*Everyone* by default) and **Language** (*Any* by default).

**Apply** writes your changes back to the card's summary and closes the panel — it does not save
anything. **Discard**, the close button, `Esc` and a click outside all throw the changes away. While
the panel is open the keyboard stays inside it. On a narrow screen it covers the page instead of
sitting beside it.

A rule matches **one** query pattern and has **one** contact group and language, so only one card can
own the Query switch and only one can own Context. Turning a second one on is refused with a message
rather than silently overwriting the first. Filters add up across cards.

**The Then column.** One read-only row per action, applied top to bottom, numbered so the order is
on screen:

```
1 · Pin an item        Pin Hario Skerton Plus to position 1
2 · Filter results     Filter results to Category:Grinders
```

The order is not cosmetic. Query rewrites chain, and custom data merges in order, so the last rule to
set a key wins it. Change it by dragging a row's **grip** — the six dots at its left edge — and
dropping it where the line shows it will land.

The grip works without a mouse, because a drag is nothing a keyboard can do. Tab to it and:

| Key | What happens |
| --- | --- |
| `Space` or `Enter` | Lifts the row ("*Pin an item* grabbed, position 1 of 3…"). Press again to drop it. |
| `↑` / `↓` | Moves the lifted row one place ("*Pin an item* moved to position 2 of 3."). |
| `Esc` | Puts it back where it was lifted from, and says so. |

The list re-orders as you move, so the screen and the announcement always agree, and focus stays on
the grip you are holding. Every step is spoken through a polite live region, because the rows
themselves are silent when they swap.

You do not edit an action on this screen either. **Edit** on a row opens its side panel; **Add
action** opens the menu of the ten kinds listed below and then opens the panel on a blank one of that
kind. A kind already used stays available, because a rule may pin several items or chain rewrites.
An action you discard before filling anything in is not added at all.

**The action panel.** The same panel as a condition's — **Apply** writes back to the row and closes,
**Discard**, the close button, `Esc` and a click outside throw the changes away, the keyboard stays
inside it while it is open, and it covers the page on a narrow screen. Its body depends on the kind:

- **Pin**, **Hide**, **Boost** and **Bury** get the **item picker**: type into **Find the item** and
  the index is searched as you stop typing. Select a result, or walk the list with the `↑` and
  `↓` keys and press `Enter`. The chosen item is shown by title and URL — the stored result id
  lives behind **Details**, because it is not something anyone should have to read. Pin adds
  **Position**; Boost adds **Multiplier**.
- **Filter results**, and the "everything matching" half of **Boost** and **Bury**, get **attribute
  rows**: **Attribute** is a drop-down of the fields this index can facet on, and **Value** a
  drop-down of the values the index really holds right now, each with the number of documents
  carrying it. Nothing is typed from memory. An attribute the index cannot facet falls back to a
  plain text value.
- **Remove word**, **Replace word**, **Replace query**, **Redirect** and **Return custom data** keep
  their single fields.

**Edit as text.** Under any set of attribute rows — including a condition's **Filters** — is an
**Edit as text** button. It swaps the rows for the raw expression the rule stores
(`Category:coffee, Tags:brewing`), and **Back to rows** swaps back. It is the same string either way,
so you lose nothing by using whichever is faster.

**An item that has left the index.** A rule that pins something you have since deleted or
unpublished does not quietly forget it: the row keeps the stored id and marks it *no longer in the
index*. Fix it by picking a new item, or remove the action. Nothing is dropped behind your back.

**What stops a save.** **Save rule** is disabled while the rule has no condition at all. Anything
else is checked when you select it: the page shows a *Friendly warning* summary at the top and puts
the specific message on the field that has to change. The checks are:

| Refused when | Message on |
|---|---|
| The rule has no name | Rule name |
| The rule has no condition at all | The If column |
| **Query** is on but the words are blank | The words field |
| A filter row has an attribute but no value, or the other way round | The filters |
| **Pin** has no item, or a position below 1 | That action |
| **Boost** has neither an item nor an expression, or a multiplier of 0 or less | That action |
| **Hide**, **Bury**, **Filter results**, a rewrite or **Redirect** has an empty required field | That action |
| **Return custom data** is not valid JSON, or is not a JSON **object** | That action |

The action checks also run when you select **Apply** in its panel, so you find out there rather than
after a refused save.

Nothing is written when a save is refused.

**Rules converted from the previous release** open with a note: *“Converted from the previous format
— one condition, one action. Nothing about its behaviour changed.”* It appears once; saving the
rule clears it. You do not have to do anything about it.

**The Rules listing** shows the same condition summary in its *Conditions* column, so you can read
what every rule matches without opening it, next to *Contact group* (*Everyone* when the rule is not
scoped), *Priority* and *Enabled*.

### Personalise rules by contact group

Every rule so far applies to everyone. Leave the side panel's **Context** switch off and it still
does — the Rules listing shows *Everyone* in that column. Point it at a contact group and the rule only fires for
visitors in that group; everyone else gets the plain ranking.

The group is the one you already built in **Digital marketing → Contact groups**. Nothing about it
changes here: you pick it, and the rule follows whatever the group's condition says, rebuild after
rebuild.

#### A worked example, on Dancing Goat

Say people who search the site and find nothing are worth catching. Give them a group, then tune
their search.

1. **Build the group from search behaviour.** In **Digital marketing → Contact groups**, create
   *Grinder shoppers* with a dynamic condition over the **Search without results** activity — the
   activities this library logs are described in the analytics guide under
   [Search activities in contact groups](analytics.md#search-activities-in-contact-groups). Save and
   let it rebuild.
2. **Scope a rule to it.** Open **Lucene Search → Products → Rules → New rule**:
   - **Rule name** — `Promote grinders to grinder shoppers`. Leave **Enabled** selected.
   - **Add condition** → turn **Query** on, *Contains*, `coffee`; turn **Context** on and select
     *Grinder shoppers* as the **Contact group** → **Apply**. Both live on one card, whose summary
     reads `Query contains "coffee" · Contact group Grinder shoppers · any language`.
   - **Add action** → **Boost matching results**. Find the grinder you want lifted in
     **Find the item**, select it, and put `2` in **Multiplier** → **Apply**.
3. **Save rule.**

The rule is now live for members of *Grinder shoppers* and invisible to everybody else. It obeys the
same schedule, priority and conflict rules as any other rule — the contact group is one more
condition on top, not a different kind of rule.

#### Seeing it work without joining the group

You are almost certainly not in *Grinder shoppers*, so the **Query tester** lets you borrow the
group. Its **Contact group** drop-down offers **Real visitor (your contact)** (the default — the
tester behaves exactly as it does for your own browsing) and every contact group in the system.

Pick *Grinder shoppers*, type `coffee`, press **Run**. Both columns are computed as a member of that
group would see them, so the comparison still means "with tuning / without tuning" and not "member
versus stranger". The boosted result carries the line

```
rule:Promote grinders to grinder shoppers (contact group grinder-shoppers)
```

which is how a group-scoped rule always shows up in the explanation — the code name in brackets tells
you *why* the rule fired for this run.

To confirm it end to end on the real site, open the site in a browser session that has accepted
tracking, do whatever puts you in the group, then search. If nothing changes, the usual causes are:

- **The visitor has not consented to tracking.** Contact groups are only consulted for visitors whose
  cookie level is *Visitor* or higher. Below that, the visitor is treated as being in no group at all
  and only your unscoped rules apply. This is deliberate — a search must not depend on data the
  visitor did not agree to.
- **The group has not rebuilt yet.** A dynamic condition adds members on a rebuild; until then the
  contact is not a member as far as the rule is concerned.
- **The contact is not the one you think.** Contacts are per browser cookie; a private window is a
  different person.

#### What this cannot do

- A rule targets **one** group. Two audiences means two rules — or one group whose condition covers
  both, which is where "A and not B" belongs anyway.
- There is no per-person tuning. The unit is the group, deliberately: a group has a name and a member
  count you can point at, and everyone in it shares the same cached results.

### What a rule is: if this, then that

A rule is a list of **conditions** and a list of **actions**. Every condition has to hold for
the rule to fire; when it does, its actions are applied in the order they are listed. A rule
with no conditions at all never fires — an "if" that is always true would change every search on the
site, so it is treated as unfinished rather than as a wildcard.

#### The conditions

| If… | Holds when | Notes |
|---|---|---|
| **The visitor's search is / contains / starts with** *words* | The query compares that way against your words | Upper and lower case never matter |
| **Attribute is value** | The visitor has that facet value selected — `category is coffee` | Several pairs all have to be selected |
| **The visitor is in contact group** *group* | The visitor is a known member of that group | Empty means everyone; see [Personalise rules by contact group](#personalise-rules-by-contact-group) |
| **The language is** *language* | The search asked for that language | Empty means any language |

The query condition can be compared two ways.

- **Against what the visitor typed** (the default): a plain text comparison. *contains "shoe"* matches
  *running shoes* — and also *shoehorn*, because it is looking at the letters.
- **Against the analyzed search** (*match the analyzed query*): your words and the visitor's are both
  put through the index's own language analysis first, then compared word by word. Plurals and word
  endings line up (*shoe* matches *shoes*), your synonyms count (*sofa* matches a search for *couch*),
  and *shoehorn* no longer matches *shoe*, because it is a different word.

**Neither one tolerates typos.** *esspresso* matches no rule about *espresso*. If a misspelling
matters, add it as a synonym or write a second rule for it.

### The ten things a rule can do

| Then… | What happens | What you fill in |
|---|---|---|
| **Pin a result to a position** | The result is moved to the position you name. If the search did not find it at all, it is added there — as long as it still matches the filters the visitor has selected. | **Item**, **Position** |
| **Boost a result** | The result is pushed up, but the search still decides the final order. A very relevant result can still beat it. | **Item** (or attribute rows), **Multiplier** |
| **Bury a result** | The result is dropped from the page that comes back. | **Item** (or attribute rows) |
| **Hide a result** | The result is taken out of the search entirely — it is on no page, and the result count does not include it. | **Item** |
| **Filter the results** | Only results matching the filter are shown. | Attribute rows |
| **Remove a word** | The word is dropped from the search before it runs: *cheap espresso machine* searched as *espresso machine*. | **Word** |
| **Replace a word** | One word is swapped for another before the search runs. | **Word**, **Replacement** |
| **Search for something else** | The whole query is replaced before the search runs. | **Query** |
| **Redirect the visitor** | The search returns a destination next to its results, and the search box sends the visitor there. | **Redirect URL** |
| **Return custom data** | A snippet of JSON travels back with the results, for the page to do something with — a banner, a layout switch, a promo block. | **JSON** |

**Pin or boost?** Pin when the answer is "this exact thing, first, no argument" — a campaign landing
page, a flagship product. Boost when you mean "lean this way" — for example, make everything in the
*Offers* category count a bit more during a sale. Boost keeps the search's own judgement; pin
overrules it. If you are unsure, use boost first: it degrades gracefully when your content changes.

**Bury or hide?** Bury is a demotion: the result leaves the page that came back, but the search still
counted it. Hide is a removal: the search never sees it, the total goes down by one, and nothing —
not even a pin in another rule — can bring it back for that query.

**Filter** is edited as attribute rows and stored as `Field:value` pairs separated by commas — for
example `Category:coffee, Tags:brewing`. All of them must match. The **Attribute** drop-down lists
the fields this index can facet on, including the ones every document carries (`contentType`,
`language`); **Edit as text** shows you the stored form if you want it.

**The three rewrites** — remove a word, replace a word, search for something else — change the query
*before* it runs, so synonyms, the results, the facet counts and the highlighted snippets all follow
the rewritten wording. Two things do not: the rule's own conditions, which were judged on what the
visitor actually typed, and the search reports, which record what the visitor typed as well. That is
deliberate — *what people search for* is a question about people, not about your rules.

**Return custom data** is the escape hatch for everything the list above cannot express. The JSON
object comes back as `ruleData` on the response, and a developer can read it in a widget
([JavaScript client](js-client.md#data-attached-by-a-rule)). When several matching rules return data
it is merged into one object in rule order, so a later rule wins a key it shares with an earlier one.

### When a rule runs

- **Enabled** — clear it to switch a rule off without deleting it.
- **Runs** - one date-range picker for the validity window (UTC days); clearing it means the rule always runs. A migrated rule with an open-ended window shows a note and keeps its single bound until you pick a range. Fill them in for a
  campaign and the rule switches itself on and off. Dates are `yyyy-mm-dd`, UTC.
- The **Query** condition's operator:
  — *Contains* — the words appear anywhere in what the visitor typed.
  — *Is exactly* — the visitor typed exactly that and nothing else.
  — *Starts with* — what they typed begins with your words.

Upper and lower case never matter.

To make a rule apply to **every** search, do not add a Query condition at all — turn on **Filters**
or **Context** instead, so the rule still says something about when it fires. Use that with **Filter
results** or **Boost**, not with pin.

### When two rules disagree

This is the part worth reading twice.

1. Rules with a **lower priority number run first**. Priority 10 beats priority 100. (Think "first in
   the queue", not "more important".)
2. If two rules have the same priority, the one that was **created first** runs first.
3. For pin and bury, the **first rule to name a result wins**. If rule A pins product X to position 1
   and rule B buries product X, and A has the lower priority number, X is pinned and B is ignored for
   that result.
4. Boost, filter, hide and the three rewrites all apply, in that same order. Two boosts on the same
   result both count, and two rewrites chain: remove a word, then replace another.
5. Custom data is merged in that order too, so the last rule to set a key owns it.
6. For redirect, the **first matching rule wins**, and a redirect rule with an empty **Redirect URL**
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

1. **Synonyms → New synonym** in the index's Edit index sidebar.
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

You do not have to think of every pair yourself. The **Synonym suggestions** page next to this one
lists pairs mined from real searches that failed and were immediately retried with different words —
approve one and it becomes an ordinary group here. See
[Popularity boosts and mined suggestions](popularity-boosts.md#suggested-synonyms).

### Stopwords

Stopwords are words that are ignored when someone searches — *the*, *a*, *of*, or your own noise
words like *buy* on a shop.

1. **Stopwords → New stopword list** in the index's Edit index sidebar (one list per index; edit the
   existing one if there already is one).
2. **Words to ignore** — one word per line.
3. **Save**.

Two cautions. Removing a word makes searches *broader*, not better — if you make *free* a stopword,
*free shipping* becomes *shipping*. And if a visitor searches for nothing but stopwords, the search
is left alone rather than turned into "show me everything".

### Field weights

A field weight decides how much a match in one place counts compared to another. A match in a title
usually deserves more than a match halfway down a body of text.

1. **Field weights → New field weight** in the index's Edit index sidebar.
2. **Field** — pick one from the list. It offers the index's searchable fields alphabetically, the
   only ones a weight can affect; a weight you saved earlier for a field the index no longer has
   stays selected (first in the list) until you change it.
3. **Weight** — `1` is normal. `3` makes a match in that field count roughly three times as much.
   `0.5` halves it.
4. **Save**.

Start small. Move one field to `2` or `3`, look at the results, then adjust. Weights of 20 tend to
turn search into "whatever has the word in the title", which is rarely what anyone wanted.

The header of this page also carries **Boost by popularity**, the index-wide opt-in that lets the
results your visitors click most rank a little higher — off by default, and explained in
[Popularity boosts](popularity-boosts.md). The same feature is where the *Suggestions* page next to
**Rules** comes from.

### Finding a result id

You should not have to. Pin, hide, bury and boost point at an item through the panel's item picker,
which searches this index and stores the id for you; the id itself is behind **Details**.

It is still worth knowing what it is. The id is the `id` field of the search response, stable across
edits to the item, and it is what the rule stores — which is why a rule survives a rename but not a
delete. If you are reading a rule someone else wrote and a row says *no longer in the index*, the
item behind that id is gone; pick a new one.

### Checking your work: the Query tester

**Query tester** in the index's Edit index sidebar answers the only question that matters after you save
a rule: did it do what you meant?

Before you run anything the page shows a quick tip explaining what the comparison is, and two empty
panels where the columns will appear. There is nothing else to read, because there is nothing to
compare yet.

1. The **Index** is the one you are in — it is named under the headline and cannot be changed.
2. Type the **Query** a visitor would type. It is required: until you type something, **Run** is
   disabled and the field reads *Enter a query to compare results. Required.*
3. **Language** offers the content languages this index is configured for, plus **Any language**
   (the default).
4. **Page size** — how many results each side shows: 10, 25 or 50.
5. **Contact group** — **Real visitor (your contact)** by default, or any contact group to see what a
   member of it would get. See
   [Personalise rules by contact group](#personalise-rules-by-contact-group).
6. Press **Run**.

You get two cards holding the same search:

- **With tuning** — exactly what a visitor gets right now: your rules, synonyms, stopwords and field
  weights all applied.
- **Without tuning** — the same query with none of them. This is the "before" picture.

Each card opens with a strip reading *N results · N ms · N changed*, so you can see at a glance
whether the tuning moved anything at all.

Every result on both sides shows:

- its **position** and title, and its URL,
- **score** — the final relevance number, after everything,
- **base score** — the raw text-match score, before any rule or weight touched it. If the two are the
  same, nothing changed that result's score,
- a tag saying how it differs from the other side,
- one line per rule, weight or synonym that applied to *that* result, for example
  `rule:Flagship machine first`.

Under the columns, **Rewritten query per pipeline stage** lists what applied to the whole search, one
line per stage: `synonym:couch` (the search was widened with this word), `weight:Title×3` (this field
weight applied), `rule:Winter campaign` (a boost or filter rule applied at query time).

Every result carries a tag, so a row is never marked by colour alone:

| Tag | Means |
|---|---|
| ▲ Moved up by a rule | Your pin or boost lifted it. |
| ▼ Moved down by a rule | Your bury, or someone else's boost, pushed it down. |
| + Added by a rule | It was not in the plain results at all — a pin put it there. |
| ⃠ Removed by a rule | It was in the plain results and your bury or filter took it out. |
| – Unchanged | Same position on both sides. |

**When the query cannot be run** — the index is not registered, or Lucene has nothing searchable for
the language you picked — a friendly-warning callout replaces both columns, with **Open status** to
go straight to the index's Status page and, when the index holds another language, a button to try
that one instead.

Below 1366 px the two columns become a **With tuning / Without tuning** toggle over one list, and the
pipeline stages collapse.

Reading it: if the two columns are identical, your rule did not match — open the rule, check what
its condition rows actually say, and check the schedule. If a result moved
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
  runs, so do it out of hours. See [Reading the Status page](#reading-the-status-page).

### Reading the Status page

The page answers three questions, top to bottom.

**Is the index healthy?** The first card carries a **Healthy** or **Degraded** tag — the word, not
just a colour — next to the document count, the number of sources and the last external write.

- *Healthy* means every queued external write reached Lucene. Counts are eventually consistent while
  work is queued; a short lag is normal and is **not** reported as degraded.
- *Degraded* means a queued write was **rejected** by Lucene and will not be retried on its own. The
  card then shows **Failed writes** instead of **Sources**, and a warning above the tables explains
  what to do: read the failed entries, ask the source system to push the batch again, and rebuild if
  you cannot tell which documents were lost. **Copy failure details** puts the failed rows on the
  clipboard as tab-separated text for a ticket.

**Where did the documents come from?** *Documents by source* stacks one bar segment per `_source` and
lists the counts and shares. `xperience` is content indexed by the CMS; every other source is an
external system pushing through the ingestion API, so its count changes without a content update in
Xperience. A source that appears only in failed log entries is called out under the table — it has
never written successfully, so none of its documents are in the index.

**What happened recently?** *Recent ingestion* is the last ten log entries for this index, newest
first. While the index is degraded the failed entries are lifted to the top of those ten; each is
marked with the invalid-row treatment **and** a **Failed** tag, so the state never depends on colour
alone. The full history, across every index, is on the **Ingestion log** page.

**Rebuild index** always asks for confirmation before it runs. Once triggered, the health tag is
replaced by a **Rebuild in progress** tag and spinner with the start time. There is no progress
percentage: the Lucene integration reports no rebuild progress, so the page does not invent one, and
reloading the page returns it to the ordinary health view (see
`docs/internal/KNOWN-LIMITATIONS.md`).

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
- A rule is scoped to **one** contact group and **one** language. Two audiences means two rules, or
  one group whose condition covers both.
- The **Attribute** drop-down offers the fields the index can *facet* on. A filter on any other
  field has to be typed, and a wrong name simply never matches.
- **Value** offers the values the index holds right now. A value that no documents currently carry —
  a category you have prepared but not published into — is not in the list; use **Edit as text** to
  write it.
- Reordering actions by **dragging** the grip needs a mouse or a trackpad: it is built on the
  browser's own drag events, which most touch browsers do not raise. On a tablet, use the keyboard
  grab from an attached keyboard — there is no touch drag.
- The item picker searches the index the rule belongs to. It cannot find something that was never
  indexed, which is the same reason a rule pointing at it would not have worked anyway.

### Appendix: how a rule is stored

You do not need this to use the builder. It is here for whoever has to read the database, write a
migration, or feed rules in from somewhere else.

A rule lives in one row of `XpSearch_Rule`. Everything about its *if* and its *then* is in two JSON
columns; the rest of the row is the name, the schedule, the priority and the index.

**`RuleConditions`** — one object. `query` is absent when the rule matches any query.

```json
{
  "query": { "operator": "contains", "pattern": "grinder", "matchAnalyzed": true },
  "filters": [{ "attribute": "ProductFieldCategory", "value": "Grinders" }],
  "contactGroup": "CoffeeGrinders",
  "language": "en"
}
```

`operator` is `is`, `contains` or `startsWith`. A rule that is scoped to nobody in particular reads
`{"filters":[],"contactGroup":"","language":""}`.

**`RuleActions`** — an array, in the order the rule applies them, each tagged with `type`:

| `type` | The rest of the object |
|---|---|
| `pin` | `targetId`, `position` |
| `hide` | `targetId` |
| `boost` | `targetId`, `filterExpression`, `multiplier` |
| `bury` | `targetId`, `filterExpression` |
| `filterResults` | `filterExpression` |
| `removeWord` | `word` |
| `replaceWord` | `word`, `replacement` |
| `replaceQuery` | `query` |
| `redirect` | `url` |
| `customData` | `json` — your text, exactly as you typed it |

```json
[{ "type": "pin", "targetId": "doc-1:en", "position": 1 },
 { "type": "customData", "json": "{\"banner\":\"Grinder week\"}" }]
```

**Upgrading from the previous release.** Rules written before this release used nine separate columns
for one condition and one action. They are converted the first time the application starts, in
place, automatically, and nothing changes meaning — including the two odd corners of the old model:
*Is anything at all* becomes "contains nothing in particular", which still fires on every search, and
a rule whose pattern was blank under any other operator comes back **disabled**, because it never
matched anything anyway. Converted rules show the note described above until you save them. The nine
old columns are removed once every row has been converted.

The `then` of a rule was briefly stored in a column called `RuleConsequences`. If your database has
one, the same start-up pass copies it into `RuleActions` unchanged — the array itself, `type`
discriminators and all, is the same — and then removes it. There is nothing to do by hand.

The full specification, including why the conversion needs no flag to be safe, is in
[ADR-0022](../adr/0022-if-then-rule-engine.md#addendum--storage-and-migration-unit-cr-4b-2026-08-24).

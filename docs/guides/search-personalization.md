## Search-driven personalization — personalize any widget by search behaviour

Xperience can show a different version of a **widget** to different visitors: that is
[widget personalization](https://docs.kentico.com/documentation/business-users/digital-marketing/widget-personalization).
Which visitors get which version is decided by a **personalization condition**. This library adds two
of them:

| Condition | What it asks |
|---|---|
| **Search - searched for** | Did this visitor search for *{term}* in the last *{N}* days? |
| **Search - A/B bucket** | Is this visitor in bucket *A* or *B* of a *{percentage}* split named *{name}*? |

They work on **any** widget, not only the search ones: a hero banner, a call to action, a promo. The
first personalizes by what people looked for; the second turns ordinary widget variants into a sticky
page-level A/B test.

### Before you start

- **An Advanced license.** Widget personalization is an Xperience **Advanced** tier feature. On a Core
  licence the *Personalize* option is not offered at all. This is a licensing fact about the platform;
  the library neither checks nor cares.
- **The package installed.** The conditions ship in `XperienceCommunity.Search.Widgets`, the same
  package as the search widgets, and appear as soon as it is referenced. No configuration.
- **Contact tracking**, for the *searched for* condition only — see
  [contact configuration](https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/contact-configuration).
  It reads the search activities this library logs, which are only logged for visitors who consented
  to tracking (see [Analytics](analytics.md)).

### How an editor uses them

The setup is the platform's, identical for both conditions:

1. Open a page in **Page Builder** and select the widget you want to personalize.
2. Select the **Personalize** icon in the widget's header.
3. Choose the condition type — **Search - searched for** or **Search - A/B bucket** (each has its own
   icon and a one-line description). On an already personalized widget, open the variant list and
   press **Add variant** instead.
4. Fill in the condition's fields (below), enter a name for the variant, and select **Apply**.
5. Configure the widget's content *inside that variant* — what you set now applies only to visitors
   the condition is true for.
6. **Save** the page.

Variants are evaluated **top to bottom**: the first variant whose condition is true wins. Visitors
matching none of them see the **original** widget, which is the one you configured before adding any
variant. Reorder variants with the priority handle. Note that all variants of one widget share the
**same condition type** — you can give each variant different values, but you cannot mix "searched
for" and "A/B bucket" on a single widget.

### Search - searched for

| Field | Meaning |
|---|---|
| **Searched term** | The variant applies when one of the visitor's searches *contains* this text, ignoring case. `espresso` matches a search for "best espresso machine". Leave it empty and the condition matches **nobody** — an empty term is a misconfiguration, not "everyone". |
| **Within the last (days)** | How far back to look. Default 30. |

A recipe: an outdoor retailer puts a hero banner on the home page, adds a variant with
*term = `tent`, days = 14*, and fills it with the camping range. Anyone who searched for a tent in the
last fortnight lands on a camping home page; everyone else sees the normal one.

**What makes it false** — and therefore renders the original widget:

- The visitor has no contact yet, or has not consented to tracking. Search activities are only logged
  with consent, so a visitor who refused tracking is never personalized this way. That is also what
  makes it **crawler-safe**: Googlebot has no contact and no activities, so it always indexes the
  original content, which is exactly the behaviour
  [Kentico recommends](https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/content-personalization/develop-personalization-condition-types).
- The visitor searched, but not for that term, or not recently enough.

Only the visitor's **100 most recent searches** are considered, read once per page render however
many personalized widgets the page carries.

### Search - A/B bucket

| Field | Meaning |
|---|---|
| **Bucket** | Which half this variant is for, **A** or **B**. |
| **Percentage in bucket B** | 1–99. How much of the traffic is in B; the rest is in A. |
| **Split name** | The name of the split. Conditions carrying the **same name** bucket a visitor identically. Default `default`. |

The bucket comes from the same first-party cookie the search experiments use, `xpsearch_bucket`: a
random id that says nothing about the visitor, registered at the **Essential** cookie level. So the
split works for anonymous visitors who refused tracking, and it is **sticky** — the same visitor is in
the same bucket tomorrow, next month, and on every server, because the bucket is a hash of their id
and the split name.

**The pairing recipe.** To run one page-level A/B test across several widgets, give every condition
the same split name and the same percentage:

1. On the hero widget, add a variant: *bucket B, 50 %, split name `spring-hero`*. Configure the new
   hero inside it.
2. On the call-to-action widget below, add a variant with **exactly the same** three values, and
   configure the matching call to action.
3. Publish. Half your visitors now see both new pieces, half see both originals — never one of each.

Then measure it the way you already measure anything else: the built-in
[Analytics / activities](analytics.md), your own custom activities, or your web analytics tool. This
condition deliberately has **no experiment entity and no report** — it is a splitter, not a testing
suite. If what you want to test is *search relevance* rather than page content, use
[Experiments](experiments.md) instead: those measure themselves.

**What makes it false:**

- The visitor has no bucket cookie and none can be given to them — they are below the Essential cookie
  level, or the response has already started streaming. They see the original widget. A brand new
  visitor can therefore see the original on the very first paint of their first page; from the next
  request on they are bucketed normally and stay put.
- Changing the split name re-shuffles everybody. Treat the name as the identity of the test.

**Crawlers are not special-cased**, and Xperience does not special-case them either. A crawler keeps
no cookies between requests, so it is handed a fresh bucket on each crawl and can land in either
bucket — like any anonymous visitor. If a page must show search engines one fixed version, personalize
it with **Search - searched for** (which a crawler never satisfies, having no contact) rather than with
a bucket split, and read Kentico's note on
[cloaking](https://docs.kentico.com/documentation/business-users/digital-marketing/widget-personalization).

### Which A/B should I reach for?

- **Testing what the search *returns*** — rules, synonyms, field weights, stopwords? Use
  [Experiments](experiments.md). They are scoped to one index, they freeze their variant while running,
  and they report searches, zero-result rate, click-through rate and clicked position per variant.
- **Testing what the *page* says** — a banner, a headline, a call to action, a layout? Use the
  **Search - A/B bucket** condition on widget variants. Any widget, any page, no report.

Both use the same cookie, so a visitor's bucket in a page test is unrelated to their variant in a
search experiment (each is hashed with its own name), and running both at once is fine.

### See also

- [Experiments](experiments.md) — A/B testing the search tuning itself.
- [Analytics](analytics.md) — the search activities the *searched for* condition reads, and the
  consent that gates them.
- [Relevance tuning](relevance-tuning.md) — personalizing the *results* by contact group, which is the
  other half of this story.

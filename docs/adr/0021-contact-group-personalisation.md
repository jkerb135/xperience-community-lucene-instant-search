# ADR-0021: Relevance rules are personalised by contact group, not per contact

- **Status:** accepted
- **Date:** 2026-08-23
- **Spec reference:** §8.2, §8.3, §8.5
- **Builds on:** ADR-0014 (relevance tuning), ADR-0015 (analytics and the consent gate)

## Context

A marketer who has segmented visitors — *Grinder shoppers*, *Wholesale buyers*, *Trial members* —
wants the same query to rank differently for each segment: boost accessories for the people who
already bought a machine, bury the retail range for wholesale, redirect *pricing* to the partner
page for partners. Today a rule is global: it fires for everyone whose query matches it.

Xperience already has the segmentation. [Contact groups](https://docs.kentico.com/documentation/business-users/digital-marketing/contact-groups)
are built and rebuilt by the platform from dynamic conditions, and a contact's memberships are
bindings in `OM_ContactGroupMember`. AN-2's work makes search activities usable inside those
conditions, so the group a rule targets can itself be defined by search behaviour ("searched and
found nothing").

The design question was the unit of personalisation, and what a personalised search does about
consent and about the response cache.

## Decision

**A rule is scoped to at most one contact group, by code name.** `XpSearch.Rule` gains a nullable
`RuleContactGroup` column and `TuningRule` a trailing `ContactGroup` member; empty means "everyone",
which is what every existing rule stays. The admin form uses the documented
[object selector](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-form-components/reference-admin-ui-form-components#object-selector)
over `om.contactgroup` with `IdentifyObjectByGuid` left false, so the stored value is the code name
the pipeline compares — no join, and a renamed display name does not break a rule.

**Group-scoped, not per-contact.** The alternative was scoring against contact attributes directly:
a boost expression over the contact's own fields, personalised per person. It was rejected on three
counts. It has no admin surface a marketer can reason about (a contact group has a name, a page and
a member count; an expression has neither). It cannot be cached — every visitor would be their own
cache key, whereas a group-scoped response is shared by everyone in the same groups. And it
duplicates segmentation the platform already owns and rebuilds; the marketer's mental model stays
"I build the group in Contact management, then I point a rule at it".

**One group per rule.** Two rules can target two groups; a single rule cannot target a set. The
combination cases people ask for ("A and not B") are exactly what a contact group's dynamic
condition already expresses, so the ceiling belongs there rather than in a second scoping UI.
Recorded in KNOWN-LIMITATIONS.

**The scope is checked in exactly one place.** `RuleSelection.Active` is already the single
selection point for a rule's *enabled*, *scheduled* and *pattern matches* conditions; the group
check joins it as a fourth predicate. `SynonymExpansionStage` calls it once per request, so every
downstream stage — boost, filter, redirect, pin, bury — inherits the scoping without knowing it
exists.

**Consent decides whether we look at all.** A new `ResolveContactGroupsStage` (order 150, between
normalize and the tuning load) fills `SearchContext.ContactGroups` once per request. It reads the
cookie level through `ICurrentCookieLevelProvider` and only proceeds at *Visitor* or above — the
same gate `SearchActivityLogger` applies before logging an activity, reached through the same
service rather than through a copy of the rule. The contact is read with
[`ICurrentContactProvider.GetExistingContact`](https://docs.kentico.com/documentation/developers-and-admins/digital-marketing-setup/contact-recognition-logic),
never `GetCurrentContact`, so searching never creates an anonymous contact. No consent, no contact,
no HTTP context, or any failure at all: the empty set, and only unscoped rules apply. A visitor who
declines tracking gets the un-personalised search, which is the correct outcome and also the safe
default.

**Membership is one query per request and is never cached across requests.** Group membership
changes under the visitor's feet — a rebuild, a form submission, a new activity — and a stale
membership would silently show the wrong ranking. The resolver memoizes on `HttpContext.Items` so
the two callers within a request (the cache decorator and the stage) cost one query, and nothing
more.

**The response cache key carries the groups.** `CachedSearchPipeline` resolves the groups before it
computes the key and `SearchCacheKey.Compute` hashes them in, sorted. Without this, the first
visitor in *Grinder shoppers* would fill the cache with their personalised results and every
subsequent visitor would be served them. Visitors in the same groups still share an entry, which is
the whole reason for choosing groups over per-contact scoring.

**The query tester simulates a group instead of resolving one.** A marketer testing a rule for
*Grinder shoppers* is not a member of *Grinder shoppers*, and making them one to test a rule is
absurd. The tester's **Contact group** drop-down offers *Real visitor (your contact)* plus every
group; choosing one swaps `ResolveContactGroupsStage` for a stage that seeds that single code name,
on **both** sides of the comparison, so "with tuning / without tuning" keeps meaning what it meant.
Simulation is admin-side only and never touches a live search.

## Consequences

- `TuningRule` gained a positional member. Anyone who implemented `IRelevanceTuningSource` or
  constructed the record must add the trailing argument; passing `string.Empty` restores today's
  behaviour exactly.
- Every search now asks the resolver once. For a visitor below the *Visitor* cookie level this is a
  cookie-level read and nothing else; for a consented visitor it is one indexed query against
  `OM_ContactGroupMember`.
- The response cache is partitioned by group membership. An installation with many groups sees more
  cache entries per query; an installation with no group-scoped rules sees exactly the partitioning
  it had, because visitors who consent and belong to no group share the empty-set key with everyone
  who did not consent.
- Personalisation is invisible in the ranking explanation unless the rule is scoped, in which case
  `ranking.boosts` reads `rule:<name> (contact group <code name>)`.

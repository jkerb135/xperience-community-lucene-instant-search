# ADR-0027 — How Xperience finds a page command (and what "command not found" really means)

- **Status:** accepted (unit CD-1)
- **Context:** the intermittent `Command 'X' was not found on the 'Y' page` seen on the host,
  and the convention it produced ("`[PageCommand]` on abstract bases or on overrides is suspect —
  declare every command as a plain method on the final page class")

## Context

That convention was a guess. XP-1b and AD-8 both bent code around it. This ADR replaces it with the
rule read out of `Kentico.Xperience.Admin.Base` **31.8.0**, decompiled read-only.

The whole discovery path is four members:

- `UITree.BuildPages()` — runs once, inside a `Lazy<UITree>` static singleton, over
  `[assembly: UIPage]` attributes from every discoverable assembly
  (`AttributeCollector.GetAssemblyAttributes<T>` in `Kentico.Xperience.Admin.Base.Shared`, which is
  `AssemblyDiscoveryHelper.GetAssemblies(true)`). One node per registered page type.
- `UITree.GetPageCommands(Type)` — **`type.GetMethods(BindingFlags.Instance | BindingFlags.Public)`**
  filtered by `method.GetCustomAttribute<PageCommandAttribute>() != null`. No `DeclaredOnly`. The
  command's name is `PageCommandAttribute.CommandName ?? method.Name` (`Command..ctor`).
- `UITree.AddPageCommands(UITreeNode)` / `AddExtenderCommands` — `node.Commands.TryAdd(name, command)`
  and **`throw new InvalidOperationException` when the name is already there**.
- `PageCommandInvoker.InvokeCommand` — resolves the node with
  `IUITreeRouter.GetUINode(commandRequest.Path, …)`, then
  `node.Commands.TryGetValue(commandRequest.Command, out command)` on a
  `Dictionary<string, Command>(StringComparer.Ordinal)`; the miss is the "was not found" message.

What follows from that, and contradicts the old convention:

- **Inherited commands are found.** `GetMethods` without `DeclaredOnly` returns public instance
  methods declared on base classes, including an abstract one. `RuleBuilderPage`'s commands are
  registered on `RuleEdit`.
- **A re-annotated override is found, once.** For an override, `GetMethods` returns only the
  most-derived `MethodInfo`; `GetCustomAttribute` with attribute inheritance keeps a single
  non-`AllowMultiple` attribute, so there is no duplicate and no `AmbiguousMatchException`. Kentico
  does this itself (`ModelEditPage.Change` re-annotates `EditPageBase.Change`).
- **`ListingPage.Delete` carries no `[PageCommand]`** — it is `public virtual` and unattributed. A
  listing that wires `AddDeleteAction(nameof(Delete))` *must* supply the attribute itself (AD-8), by
  override or by a distinctly named method (XP-1b's `DeleteRow`). Both shapes work.
- **A name can never go missing silently; it can only collide loudly.** Two commands with one name on
  a page (a `new`-hidden method, or an extender that reuses a name) fail while the tree is built, and
  the cached `Lazy` exception takes the whole admin down. There is no per-request cache, no scan
  order to lose to, nothing keyed on anything unstable: the tree is built once and is deterministic.

So discovery cannot be the intermittent part. CD-1 rebuilt the real `UITree` in-process over the real
assemblies (`tests/XpSearch.Admin.Tests/PageCommandDiscoveryTests.cs`) and every command the client
sends resolves — including the three that failed on the host. The commit clock explains those: the
`Delete` commands were authored 2026-08-31 23:00 and merged 23:01, the rule builder's `SearchItems`
merged 22:23, and the host builds the library by `ProjectReference` into the *main* worktree — so a
host started at ~23:00, or built while another branch was checked out there, is running an assembly
whose page genuinely has no such command. "Intermittent across fresh builds" is that, not reflection.

## Decision

The rule for a page command in this library:

1. Declare it in whatever shape reads best — plain method, inherited from an abstract base, or an
   override that re-annotates. All three are discovered. Prefer the one that keeps the behaviour
   with the page that owns it.
2. The **name** must be unique across the page's whole public instance surface, its bases and its
   extenders. That is the only shape rule the platform enforces, and it enforces it by refusing to
   start.
3. Every name the client sends must exist on the page the client is on. `PageCommandDiscoveryTests`
   asserts both, for the whole Admin assembly, against Kentico's own `UITree`.
4. When the host says "command not found", check what the host is actually running before touching
   the code — the message names the page, and a stale build produces it exactly.

## Consequences

- `docs/internal/agent-primer.md` no longer warns off inherited/overridden commands.
- No page changes: the shapes on main are all valid, and re-shaping them would be churn.
- `PageCommandDiscoveryTests` is the guard. It fails if a new page's command cannot be reached, and
  it was proven red by removing the two attributes whose absence produced the reported symptom.

# Known limitations

Intentional simplifications, one entry each: where it lives, what was simplified, the ceiling it hits,
and how to lift it.

## `NoWarn NU5104` in `Directory.Build.props`

- **Simplified:** the packages are marked stable while depending, transitively, on the
  `Lucene.Net 4.8.0-beta00017` prerelease. `Kentico.Xperience.Lucene 15.0.5` pins that version, so there
  is no stable Lucene.Net to depend on; NU5104 ("a stable release of a package should not have a
  prerelease dependency") is suppressed for the whole library instead of being resolved.
- **Ceiling:** consumers see a prerelease package in their transitive dependency graph, and tooling that
  refuses prerelease dependencies (some corporate feed policies, `--no-prerelease` restore gates) will
  reject the package. The suppression is repo-wide for this library, so a *new*, genuinely wrong
  prerelease dependency would also go unnoticed.
- **Upgrade path:** drop the `NoWarn` when Kentico.Xperience.Lucene moves to a stable Lucene.Net release,
  and let the warning fail the build again.

## `Hit.Attributes` in `XpSearch.Core/Contract/Hit.cs`

- **Simplified:** `Hit` is generated from `contract/xpsearch-api.schema.json` like every other contract
  type, but quicktype's C# backend ignores the schema's `additionalProperties`, so the open half of the
  object — every non-reserved attribute a query retrieves — is hand-written as a `[JsonExtensionData]`
  property on a partial class next to the generated file. The TypeScript side needs no such help.
- **Ceiling:** one member of the contract exists in two places. If quicktype ever stops emitting `Hit` as
  a `partial class`, the hand-written half silently stops applying and hits lose their attributes; the
  `contract:check` script asserts against exactly that, plus the property names and the extension data
  attribute, so the failure is loud rather than silent. Reading an attribute in C# costs a dictionary
  lookup and a `JsonElement` unwrap.
- **Upgrade path:** delete `Contract/Hit.cs` and its assertions in `scripts/contract.mjs` if quicktype
  learns to emit `[JsonExtensionData]` for `additionalProperties`; otherwise leave it.

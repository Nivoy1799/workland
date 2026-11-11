---
id: 056
title: Offline dish names (generate once with the spec 006 pipeline, review, ship a name file, look it up in the game)
status: approved
owner: architect
builder: sonnet       # 056a, 056b, 056c: sonnet
---

> Approved by the human on 2026-11-11 (inbox questions 25 and 26) with the recommended defaults of its Open questions.

# 056: Offline dish names

## Goal
D38: no live AI gateway at Early Access. Instead, a developer runs the spec 006 pipeline (strict schema, vegan rule,
moderation seam, cache) once on their own PC, for every item the menus can produce. A human reviews the results, and the
approved names and descriptions ship with the game as a data file. The game looks a name up by item hash and falls back
to today's built-in name. No key, no hosting, no running cost; the LLM still only names, never decides (D5, AGENTS.md).

## This is an integration spec
It touches /sim, /ai-gateway and /game, so it is built in three parts, in order, each with its own branch, commit and
review: **056a** (/sim), **056b** (/ai-gateway), **056c** (/game). Each builder touches only its part's files.

## Scope
**056a: the reachable items (/sim, one script)**
- `sim/Runtime/Cooking/KitchenItems.cs` (new: every base item the shipped kitchen uses, in the game's order: potato,
  apple, tomato, onion with the exact values of `DemoIngredients`, then `StreetFoodItems.All`, the grill vegetables,
  `Drinks.Items`; the same design as 055a's `KitchenItems`, so 055a later skips that step).
- `sim/Runtime/Cooking/Reach/` (new, namespace `Worktown.Sim.Cooking`): `RouteKind.cs`, `RouteStep.cs`, `RouteChain.cs`,
  `RouteDef.cs`, `MenuRoutes.cs`, `ReachLimits.cs`, `ReachableItem.cs`, `ReachableItems.cs`, `ReachableItemsText.cs`.
- `sim/Tools~/ItemList/ItemList.csproj`, `Program.cs` (new console app, net10.0, refs `../../Sim.csproj`); `sim/Sim.sln`
  (change: add it). `scripts/item-list.sh` (new): `dotnet run --project sim/Tools~/ItemList -c Release -- "$@"`.
- `sim/Sim.Tests~/Cooking/ReachableItemsTests.cs`, `KitchenItemsTests.cs` (new).

**056b: the bake tool (/ai-gateway, one script, README)**
- `ai-gateway/src/Worktown.Gateway/GatewayServices.cs` (new: `AddItemNamePipeline(IServiceCollection, IConfiguration)`,
  moved out of `Program.cs`); `Program.cs` (change: calls it; web behaviour identical); `Config/GatewayOptions.cs`
  (change only if needed so the service-token rule is checked by the web host, not by the bake tool).
- `ai-gateway/src/Worktown.Gateway/Cache/FileContentCache.cs` (new).
- `ai-gateway/tools/NameBake/` (new console app, net10.0, refs the gateway project): `NameBake.csproj`, `Program.cs`,
  `BakeCommand.cs`, `PublishCommand.cs`, `ReachableFile.cs`, `ReviewSheet.cs`, `ReviewRow.cs`, `NameBookWriter.cs`,
  `ReviewFlags.cs`. `ai-gateway/Gateway.sln` (change: add it).
- `ai-gateway/offline/` (new data, committed): `reachable.v1.json` (from 056a's script), `name-cache.jsonl`, `review.tsv`.
- `ai-gateway/tests/Worktown.Gateway.Tests/` (new: `FileContentCacheTests.cs`, `BakeCommandTests.cs`,
  `ReviewSheetTests.cs`, `PublishCommandTests.cs`; change: csproj refs `NameBake`).
- `scripts/bake-names.sh` (new), `ai-gateway/README.md` (change: section "Offline names").

**056c: the lookup (/game)**, paths under `game/Assets/_Project/`
- `Scripts/Cooking/DemoIngredients.cs` (change: forwards to `KitchenItems`; `All`, `Catalog` and the look-byte order
  unchanged).
- `Scripts/Cooking/ShippedNames.cs` (new, pure: parse, validate, `IItemNameSource`), `ShippedNamesLoader.cs` (new: loads
  `Resources/ItemNames` once), `KitchenState.cs` (change: `ShownNameOf(Item)`), `KitchenService.cs` (change: the food
  label uses `ShownNameOf`, see Design).
- `Resources/ItemNames.json` (new: a valid book with `"items": []`).
- `Tests/EditMode/ShippedNamesTests.cs`, `MenuRoutesDriftTests.cs` (new); the D27 word test (change: covers the book).

## Out of scope
- A live gateway, Steam auth, cost limits (055, deferred). Choosing the moderation approach (O6 stays OPEN).
- Changing the prompt, schema, cache key or model of spec 006. Other content types (NPC lines, recipe text).
- Names for items outside the reachable set (free combinations keep the fallback name).
- Showing descriptions anywhere in the game (the lookup returns them; a later spec shows them).
- Running the bake with a real key (a human step), filling `ItemNames.json` with real names.

## Design

### Which items (056a): the reachable set
`MenuRoutes.Default` holds one `RouteDef(string Id, string Name, RouteKind Kind, IReadOnlyList<RouteChain> Chains,
string? TemplateId)` per dish of the game's `Menu.All` (13 dishes, the same names) and one per drink (lemonade, iced tea,
soda). `RouteKind { Single, Mix, Build, Drink }`. `RouteChain(IReadOnlyList<string> BaseIds, IReadOnlyList<RouteStep>
Steps)`; `RouteStep(string OperationId, IReadOnlyDictionary<string, string> Target)`: `Target` is the bucket the menu asks
for (as `Menu.cs` `DishNeed`: fries `cut shape=sticks`, then `deep_fry` or `fry` with `time=medium`).
`ReachableItems.Enumerate(ItemFactory factory, IReadOnlyList<RouteDef> routes, ReachLimits limits)` applies these rules,
using only `ItemFactory` (an `Apply` error on a target form throws: the route is wrong):
1. **Chains:** every prefix of every chain, each step in **every reachable bucket**: Choice params take each bucket;
   Number params sweep `limits.SweepSteps + 1` (default 49) evenly spaced values from `Min` to `Max`, plus each bucket
   threshold, across all param combinations of the op; results are deduplicated by hash (wrong prep included: a burnt
   potato gets a name too).
2. **Combined dishes** (Mix, the plate, Build) take each chain only in its **target form**, so the set stays linear.
   Single and Mix dishes also get `plate` of the target dish.
3. **Builds:** the template's layers in template order. Required layers in target form, one item per base candidate
   (e.g. red cabbage or lettuce, either sauce, either seitan heat); the optional salad layer: none or each candidate in
   target form (cut `slices`); sauce amount `right`; every prefix (open build) plus the template's close and pack (if
   any). Order modifiers ("no onion") are not enumerated.
4. **Drinks:** the cup, each drink, `fill` in every ml bucket, `lid` on each fill.
5. `ReachableItem(string Hash, string CanonicalTree, string FallbackName, IReadOnlyList<string> Tags,
   IReadOnlyList<string> Routes, bool IsTarget, IReadOnlyList<string> LabelWords)`. `FallbackName` is
   `ItemNaming.FallbackName(item, NameCatalog.Empty)`; `LabelWords` are the words of the item's own non-empty bucket
   labels. Sorted by hash; an item reached by several routes lists them all (ordinal).
6. `ReachLimits.MaxItems` (default 2,500): more throws. Expected size: about 1,000-1,800 (056a pins the real count).

`ReachableItemsText.Format(items) -> string` (pure, LF, deterministic, ASCII-only after validation):
```json
{ "format": "worktown-reachable-items", "formatVersion": 1, "hashVersion": "v1", "setHash": "<16 hex>",
  "items": [ { "hash": "<64 hex>", "canonicalTree": "...", "fallbackName": "...", "tags": ["..."],
               "routes": ["fries"], "target": true, "labelWords": ["crispy"] } ] }
```
`setHash` = first 16 hex of SHA-256 over the sorted hashes joined by `\n`. `bash scripts/item-list.sh --out
ai-gateway/offline/reachable.v1.json` writes it and prints the count and the set hash.

### The bake (056b), run locally by a developer
`bash scripts/bake-names.sh bake [--limit 500] [--dry-run]` loads `.env` if present (gitignored; `set -a`), then runs
`NameBake bake`. The tool builds the spec 006 services in-process through `AddItemNamePipeline` (same upstream client,
`SchemaValidator`, `VeganContentRule`, the registered `IModerator`, the same `CacheKey`), with `FileContentCache` instead
of the in-memory cache. It needs `GATEWAY_UPSTREAM_BASE_URL`, `GATEWAY_UPSTREAM_API_KEY`, `GATEWAY_MODEL` (plus the 006
optional vars), never `GATEWAY_SERVICE_TOKEN`.
- For each reachable item (hash order): an `ItemNameRequest` from its hash, tree, fallback name and tags, through
  `ItemNamePipeline.RunAsync(request, "offline-bake", ct)`, one call in flight. Items that fail `RequestValidator` are
  counted as `skipped` (none expected; report each key prefix).
- Stops after `--limit` upstream misses or 5 upstream errors in a row (exit 3). `--dry-run` makes no upstream call and
  prints how many items are cached and how many would be asked.
- `FileContentCache : IContentCache`: one JSON line per `CachedContent` plus its key, loaded at start, appended on set,
  first write wins (as 006), `Remove(key)` rewrites the file atomically (`.tmp` then replace). Sorted on rewrite.
- Output: counts only (items, cached, generated, schema-invalid, vegan-rejected, moderator-rejected, errors, skipped).
  Never the key, the prompt, raw upstream output or the `Authorization` header (006 logging rules hold).

### Human review (`ai-gateway/offline/review.tsv`)
After each bake, `ReviewSheet` merges one row per cached reachable item: `key12, routes, target, plain_name, name,
description, flags, decision, note`, sorted by first route, then target first, then plain name. Decisions of existing
rows (same cache key) are kept; new rows get an empty decision. `ReviewFlags` (help for the reviewer, not a verdict):
`doneness` (a `LabelWords` word is missing from the name), `same` (name equals the plain name, case-insensitive),
`dup` (the same name on another route), `long` (name over 30 chars). The reviewer sets `decision` to `ok` or `no` in a
spreadsheet or editor, checking: it describes the item, it is vegan (D27), no real people, brands or real-world history,
the only past referenced is the game's own (D28), a burnt or raw item is not called golden or perfect (D21). On the next
bake every `no` row's cache entry is removed and regenerated once (counted in `--limit`); its old text moves to the
row's `note` as `rejected: <name>`.

### Publish
`bash scripts/bake-names.sh publish` takes rows with `ok` whose hash is in the current `reachable.v1.json`, re-checks
each against the item_name.v1 schema, `VeganContentRule` and the registered `IModerator` (any failure: exit 2, nothing
written) and writes `game/Assets/_Project/Resources/ItemNames.json` (`.tmp` then replace), sorted by hash, LF, no
timestamps (same inputs, byte-identical file):
```json
{ "format": "worktown-item-names", "formatVersion": 1, "hashVersion": "v1", "reachableSet": "<16 hex>",
  "contentVersion": "item_name.s1.p1", "model": "<GATEWAY_MODEL>", "moderator": "<moderator class name>",
  "items": [ { "hash": "<64 hex>", "name": "Golden Spud Sticks", "description": "Crisp sticks of fried potato." } ] }
```
It prints published, pending (no decision), rejected and stale (not reachable any more) counts.

### Moderation (O6 OPEN, nothing decided here)
The bake uses whatever `IModerator` the gateway registers (today the placeholder) plus the vegan rule; the file names the
moderator. Human review is an extra gate, not a choice of O6. Filling the shipped file with real names is a human step
that waits for the human's go-ahead (O6, or an explicit decision that review is enough for a given build).

### The lookup (056c)
- `ShippedNames.Parse(string json) -> ShippedNames` (pure, `JsonUtility` DTOs as `ShopSave`): text over 2 MB, a wrong
  `format`, a newer `formatVersion` or another `hashVersion` gives `ShippedNames.Empty` and one warning. Per entry: hash
  `^[0-9a-f]{64}$`, name and description against the item_name.v1 patterns and lengths (copied, with a test that they
  equal the schema file's), `ContentRules.FindForbidden == null` for both (D27). A bad entry is dropped; a duplicate
  hash keeps the first; one warning with the counts. `TryGetName` (`IItemNameSource`), `TryGetDescription`, `Count`.
- `ShippedNamesLoader.Current`: loads `Resources/ItemNames` once on first use; a missing asset is `Empty`.
- `KitchenState.ShownNameOf(Item)` = `ItemNaming.Resolve(item, NameCatalog.Empty, ShippedNamesLoader.Current)`.
  `KitchenState.NameOf` stays the plain name (rules, orders, tickets, menu, the aim prompt and tests keep it, so D21's
  bucket words stay visible when aiming). Only the food label (`Food.DisplayName`, set by the server through
  `KitchenService`) uses `ShownNameOf`. Clients get it through the existing SyncVar: no new network message.

### Regenerating
Cooking data changes (a new base item, bucket or dish) change the set: 056a's pinned count and set hash fail with
"reachable set changed: update the pin and rerun scripts/item-list.sh and bake-names.sh (spec 056)". Then: `item-list.sh`,
`bake` (only new items cost money), review the new rows, `publish`, commit the four files. A new prompt, schema or model
changes every cache key, so everything regenerates and needs a full review. Stale names are dropped at publish.

### No key in git
The key lives only in `.env` (already gitignored) or the shell. `name-cache.jsonl`, `review.tsv` and `ItemNames.json`
hold only generated text, keys of the 006 cache (hashes) and model ids. Test keys follow 006 (`test-upstream-key-not-a-
secret-0001`); `scripts/verify.sh`'s secret scan covers the new files.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes after each part.
- [ ] AC2 (056a): `KitchenItems` equals `DemoIngredients.Catalog` field by field (test copies the four literal defs);
      `Enumerate` is deterministic (two runs, equal text), contains the spec 005 fries fixture
      `fry(temp=hot,time=medium;cut(shape=sticks;potato))` and a burnt fries variant, has an `IsTarget` item for every
      route, stays under `MaxItems`, and its count and set hash are pinned. A route whose target fails throws.
- [ ] AC3 (056a): `item-list.sh --out <tmp>` writes the format above; every item passes 006's request rules (tree,
      fallback name, tags regexes), checked by a /sim test with the same regexes.
- [ ] AC4 (056b): With `StubUpstreamHandler`: a bake of 3 items makes 3 upstream calls; a second bake makes 0; `--limit 1`
      makes 1; schema-invalid and `forbiddenword` outputs are counted and not cached; `--dry-run` makes none.
- [ ] AC5 (056b): `FileContentCache`: survives a restart, first write wins, `Remove` then set works, a crash mid-rewrite
      (fake file layer) keeps the old file.
- [ ] AC6 (056b): Review: decisions survive a re-bake; a `no` row is regenerated once and its old name lands in `note`;
      each flag has a positive and a negative test.
- [ ] AC7 (056b): Publish writes only `ok` and reachable rows; a row that fails the vegan rule after a hand edit stops
      publish with exit 2 and no file change; two publishes give byte-identical files.
- [ ] AC8 (056b): Captured stdout, stderr and logs of a stub bake contain neither the test key nor the system prompt;
      the bake starts without `GATEWAY_SERVICE_TOKEN`; the web host still refuses to start without it (006 AC12).
- [ ] AC9 (056c): `ShippedNames`: every rejection case above has a fixture; a valid book returns names and descriptions;
      the copied patterns equal `ai-gateway/schemas/item_name.v1.schema.json`.
- [ ] AC10 (056c): With the empty shipped book every food label equals today's name (`ShownNameOf == NameOf` on the
      route items); with a test book the fries label shows the book's name and `NameOf` is unchanged.
- [ ] AC11 (056c): `MenuRoutesDriftTests`: every `Menu.All` dish has a route of the same name whose target item gets
      `OrderRules.Match == OrderMatch.Exact`; `DemoIngredients.All` keeps its order (existing look tests green).
- [ ] AC12 (056c): `bash scripts/visual-check.sh --tours cook` passes with 0 Exception lines and unchanged names.

## Test plan
- 056a: `dotnet test sim/Sim.sln` (`ReachableItemsTests`, `KitchenItemsTests`; no network, no randomness).
- 056b: `dotnet test ai-gateway/Gateway.sln`; stub upstream at `https://upstream.test/v1`, temp folders for the offline
  files, fixture `reachable` files with 3 items.
- 056c: EditMode `ShippedNamesTests` (string fixtures: valid, newer version, wrong hash version, bad hash, bad name,
  forbidden word, duplicate, 3 MB), `MenuRoutesDriftTests`, the D27 word test.
- Command: `bash scripts/verify.sh`; then `bash scripts/visual-check.sh --tours cook --out .verify/visual056`.

## Human steps
- Put an OpenRouter key in `.env`; run `scripts/item-list.sh`, `bake-names.sh bake` (repeat until no pending items),
  review `review.tsv`, `publish`, commit. Expected cost: about 1,500 small calls.
- Decide when real names may ship (O6, see Moderation). Steam's AI-content disclosure for pre-generated text.

## Open questions (recommended default in brackets)
1. Builds: only target forms plus base choices, open builds, close and pack (rule 3). [yes; about 300-500 builds instead
   of tens of thousands; other builds keep the fallback]
2. Review depth: every row gets a decision. [yes, about 1,500 short rows, sorted by dish; alternative: review every
   target form and a 10 % sample of the rest]
3. Where generated names show. [the food label only; aim prompt, orders, tickets and menu keep plain names]
4. Commit `name-cache.jsonl` and `review.tsv`. [yes: reruns are free and reviews are kept; they contain no secret]
5. Descriptions: generate and ship now, show later. [yes]
6. Moderation and release (O6). [OPEN: not decided here]

## Implementation notes
(Builder fills this in, one subsection per part.)

## Blockers
(Builder fills this in after 3 failed attempts.)

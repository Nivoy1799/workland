---
id: 005
title: Cooking process model (items, operations, identity, rating) in /sim
status: approved
owner: architect
builder: sonnet
---

# 005: Cooking process model in /sim

## Goal
Replace the spec 002 "list of ingredients -> dish" model with the process model from D19 to D22:
one `Item` type, operations that turn items into new items, identity from a bucketed process
tree, and a rating that every item gets. This is the deterministic core that the cooking station
(spec 007) and AI naming (spec 006) build on.

## Scope
Only `/sim`. All runtime files in `sim/Runtime/Cooking/`, namespace `Worktown.Sim.Cooking`.
- Rename and evolve (use `git mv`, then edit; move any `.meta` file along with its `.cs`):
  `IngredientStats.cs` -> `ItemStats.cs`, `Ingredient.cs` -> `BaseItemDef.cs`,
  `DishResult.cs` -> `ItemRating.cs`, `DishScorer.cs` -> `ItemRater.cs`.
- New: `DonenessWindow.cs`, `ItemComponent.cs`, `Item.cs`, `ItemCatalog.cs`, `OperationKind.cs`,
  `ParamKind.cs`, `BucketDef.cs`, `ParamDef.cs`, `HeatProfile.cs`, `OperationDef.cs`,
  `OperationCatalog.cs`, `ParamValue.cs`, `OperationError.cs`, `OperationResult.cs`,
  `ItemFactory.cs`, `CookingLimits.cs`, `ItemCanonical.cs` (internal), `NamedItem.cs`,
  `NameCatalog.cs`, `IItemNameSource.cs`, `ItemNaming.cs`.
- Tests in `sim/Sim.Tests~/Cooking/`: `DishScorerTests.cs` -> `ItemRaterTests.cs` (port), new
  `TestItems.cs` (fixtures), `ItemCatalogTests.cs`, `ItemIdentityTests.cs`,
  `OperationTransformTests.cs`, `ItemNamingTests.cs`.
- Unchanged: `sim/Runtime/Core/*` and `SeededRandomTests.cs` (no randomness is needed here).
- This spec file (`## Implementation notes`).

## Out of scope
- Any file outside `/sim` (Unity, networking, gateway). Spec 003 AC6 is fixed separately (Human steps).
- JSON or any file loading of definitions, and the real starting list of base items.
- Calling the AI gateway or async naming (spec 006). Station/player checks and wire format (spec 007).
- Rating texture (Crunch, Moisture), seasoning beyond Mix, new operations (Mash, Bake, ...).
- Design-time LLM drafting of base item stats: allowed later as a tool whose output humans review
  and commit as data. Not part of this spec's code.

## Design

### Definitions are records passed in, no loader in /sim
/sim does no file IO, netstandard2.1 has no `System.Text.Json`, and Unity's `JsonUtility` lives in
`UnityEngine`. So /sim only accepts plain records via constructors. A later /game or /tools spec
loads data files (Newtonsoft in Unity) and maps them to these records. Tests hard-code records.
Operations are a fixed built-in set (D22): `OperationCatalog.CreateDefault()` in /sim holds the
table below; transforms are generic code per `OperationKind`, tuned only by data fields.

### Ids
Every id (base item, operation, param name, bucket id, tag) must match `^[a-z0-9_]{1,32}$`.
This keeps the canonical serialization free of escaping. Catalog constructors throw
`ArgumentException` on violations, duplicate ids, stats NaN/infinite/outside 0..1, empty
display name, or a window with not `0 <= Min <= Max <= 1`.

### Types (sketch)
```csharp
// was IngredientStats; all 0..1. Doneness: 0 raw .. 1 charred. Moisture: 0 dry .. 1 wet.
public sealed record ItemStats(float Sweet, float Salty, float Sour, float Bitter, float Umami,
    float Fat, float Crunch, float HeatTolerance, float Doneness, float Moisture);
public sealed record DonenessWindow(float Min, float Max);           // ideal doneness, inclusive
// was Ingredient. Stats.Doneness is the starting doneness.
public sealed record BaseItemDef(string Id, string DisplayName, ItemStats Stats,
    DonenessWindow IdealDoneness, IReadOnlyList<string> Tags);
public sealed record ItemComponent(string BaseId, float Doneness, DonenessWindow Ideal);

public sealed class Item          // immutable; constructor internal, only ItemFactory creates items
{
    public string Hash { get; }                         // identity, 64 lowercase hex chars
    public string CanonicalTree { get; }                // readable tree, see Canonicalization
    public string? BaseId { get; }                      // set for base items
    public string? OperationId { get; }                 // set for operation items
    public IReadOnlyDictionary<string, string> Buckets { get; } // param name -> bucket id
    public IReadOnlyList<Item> Inputs { get; }          // canonical order
    public ItemStats Stats { get; }                     // exact values, server-only
    public float Surface { get; }                       // 0 whole .. 1 fine; set by Cut
    public IReadOnlyList<ItemComponent> Components { get; } // sorted (BaseId ordinal, Doneness)
    public IReadOnlyList<string> Tags { get; }          // sorted distinct union of inputs + AddedTags
    public int Depth { get; }                           // base = 1, op = 1 + max input depth
    public int Weight => Components.Count;
}

public enum OperationKind { Cut, Heat, Mix, Plate }
public enum ParamKind { Number, Choice }
// Number: Value = inclusive lower bound (upper = next bucket's Value, last one up to ParamDef.Max).
// Choice: Value = kind-specific number (Cut: surface of the shape).
public sealed record BucketDef(string Id, float Value, string Label);
public sealed record ParamDef(string Name, ParamKind Kind, float Min, float Max, IReadOnlyList<BucketDef> Buckets);
public sealed record HeatProfile(float? FixedTemperatureC, float FatGain, float MoistureDelta,
    float CrunchGain, float UmamiGain, float Dilution);
public sealed record OperationDef(string Id, string Station, OperationKind Kind, bool Commutative,
    int MinInputs, int MaxInputs, IReadOnlyList<ParamDef> Params, HeatProfile? Heat,
    IReadOnlyList<string> ForbiddenInputTags, IReadOnlyList<string> AddedTags, string NameTemplate);

public readonly struct ParamValue { public static ParamValue Number(float v); public static ParamValue Choice(string id); }
public enum OperationError { None, UnknownBaseItem, UnknownOperation, InvalidParam, ArityViolation,
    ForbiddenInput, DepthLimit, ComponentLimit }
public sealed record OperationResult(Item? Item, OperationError Error) { public bool Ok => Error == OperationError.None; }

public sealed class ItemFactory
{
    public ItemFactory(ItemCatalog items, OperationCatalog operations);
    public OperationResult CreateBase(string baseId);
    public OperationResult Apply(string operationId, IReadOnlyList<Item> inputs,
                                 IReadOnlyDictionary<string, ParamValue> parameters);
}
public static class CookingLimits { MaxDepth = 8; MaxComponents = 16; MaxMixInputs = 6; MaxPlateInputs = 4; }
```
Invalid player intents are normal on a server, so `Apply` returns an error code instead of
throwing. Only programmer errors throw: null list or dictionary `ArgumentNullException`, null
element in `inputs` `ArgumentException`.
`InvalidParam` = missing, unknown name, wrong kind, unknown choice, NaN/infinite, outside Min..Max.

### Default operations (`OperationCatalog.CreateDefault()`; boundaries provisional, see Open questions)
| id | station | kind | inputs | commutative | params (bucket id [lower, upper) "label") |
|---|---|---|---|---|---|
| cut | board | Cut | 1 | no | shape (Choice): halves 0.2 "halves", slices 0.5 "slices", sticks 0.6 "sticks", dice 0.8 "cubes" |
| fry | pan | Heat | 1 | no | temp 120..220: medium [120,170) "", hot [170,220] "crispy"; time 10..600: short [10,60) "lightly", medium [60,180) "", long [180,600] "dark" |
| saute | pan | Heat | 1 | no | same temp and time buckets as fry |
| boil | pot | Heat | 1 | no | time 30..1800: short [30,300) "lightly", medium [300,900) "", long [900,1800] "soft"; fixed 100 C |
| mix | counter | Mix | 2..6 | yes | none |
| plate | counter | Plate | 1..4 | yes | none |

Heat profiles (FatGain, MoistureDelta, CrunchGain, UmamiGain, Dilution): fry (0.35, -0.6, 0.5,
0.15, 0); saute (0.15, -0.4, 0.25, 0.2, 0); boil (0, +0.5, -0.8, 0, 0.1).
Every operation has `ForbiddenInputTags = ["plated"]`; plate has `AddedTags = ["plated"]`.
Name templates: cut `"{in} {shape}"`, fry `"{temp} {time} fried {in}"`, saute
`"{temp} {time} sauteed {in}"`, boil `"{time} boiled {in}"`, mix `"{in} mix"`, plate `"plate of {in}"`.
Stations are labels for spec 007; /sim does not check where a player stands.

### Transforms (deterministic float math on a copy; clamp01 every stat and D_c after each step)
- **Cut**: `s = shape.Value`. If `s <= input.Surface`: no-op, return the input item itself (same
  hash). Else new item with `Surface = s`, stats and components unchanged.
- **Heat**: `T` = temp param or `FixedTemperatureC`, `t` = time in seconds.
  `dose = max(0, (T - 60) / 120) * (t / 180) * (0.5 + Surface)`, `g = min(1, dose)`.
  Every component: `D_c' = D_c + dose * (1 - 0.5 * HeatTolerance)`. `Doneness' = mean(D_c')`.
  `Fat' = Fat + FatGain*g*(1-Fat)`; `Umami' = Umami + UmamiGain*g*(1-Umami)`;
  Moisture: delta < 0: `M*(1 + delta*g)`, else `M + delta*g*(1-M)`;
  Crunch: gain >= 0: `C + gain*g*(1-Moisture')*(1-C)`, else `C*(1 + gain*g)`;
  then Sweet, Salty, Umami `*= (1 - Dilution*g)`; `Bitter' = Bitter + 2*max(0, Doneness' - 0.9)`.
  Sour, HeatTolerance, Surface unchanged.
- **Mix**: flatten (an input that is itself a mix contributes its inputs), so
  `Mix(Mix(a,b),c)` equals `Mix(a,b,c)`. After flattening, 2..6 inputs else `ArityViolation`.
  Duplicates are kept (two potatoes differ from one). Stats and Surface = Weight-weighted mean
  of the inputs in canonical order (Doneness = mean of components); components concatenated.
- **Plate**: no flattening (plated items are forbidden as input). Stats like Mix (for economy
  and display); the rating judges each part separately (see Rating).

### Canonicalization, identity and limits
- Canonical input order: ordered ops keep the given order (chains are nesting, so order is kept).
  Commutative ops sort inputs by `Hash` ordinal; ties broken by Stats fields in declaration order,
  then Surface (float `CompareTo`), so float sums are order independent and results identical.
- Hash = lowercase hex SHA-256 of the UTF-8 preimage (fixed algorithm, never `GetHashCode`):
  - base: `v1|b|<baseId>`
  - op: `v1|o|<opId>|<p1>=<bucket1>,<p2>=<bucket2>|<childHash1>,<childHash2>` with params
    sorted by name ordinal (empty segment if none) and child hashes in canonical order.
  Bucket ids are hashed, not boundaries: retuning boundaries keeps existing identities. Any change
  to these rules bumps the prefix to `v2`. Exact values never enter the hash, so identity is
  platform stable even if float math differs.
- `CanonicalTree`: base `<baseId>`, op `<opId>(<p>=<bucket>,...;<child>,<child>)`, e.g.
  `fry(temp=hot,time=medium;cut(shape=sticks;potato))`. For logs and gateway prompts, not hashed.
- Checks in this order: arity (after mix flattening), forbidden tags, params, no-op collapse, then `Depth > MaxDepth`
  -> `DepthLimit`, `Weight > MaxComponents` -> `ComponentLimit`.
- Network note for spec 007: operations run only on the server. The input item objects are
  consumed and replaced by the new item; clients only ever receive results.

### Rating (`ItemRater.Rate(Item) -> ItemRating(float Quality, float Taste, float DonenessFactor)`)
- `Taste(Stats)` = the spec 002 formula unchanged: `0.5*balance + 0.25*umamiScore + 0.25*fatScore`.
- Per component: `dist = max(0, Ideal.Min - D_c, D_c - Ideal.Max)`, `f_c = clamp01(1 - dist / 0.2)`.
  `DonenessFactor = mean(f_c)` over all components (each part of a mix counts equally).
- `Quality = Taste * (0.2 + 0.8 * DonenessFactor)`, in 0..1.
- Plate: Quality, Taste and DonenessFactor are the Weight-weighted means of each input's rating.
- Monotonic expectations: more distance from any component's window never raises Quality;
  inside the window, Quality equals Taste; Taste keeps the spec 002 monotonic cases.

### Naming (display only, D5: never affects stats or rating)
- `NamedItem(string Hash, string Name)`; `NameCatalog(IEnumerable<NamedItem>)` (fixed names such
  as "fries"); throws on duplicate hash or empty name.
- `IItemNameSource { bool TryGetName(string itemHash, out string name); }`: synchronous lookup that
  spec 006 implements over the gateway cache. /sim never waits for the network.
- `ItemNaming.FallbackName(Item, NameCatalog)`: base = DisplayName; op = template with `{in}` =
  input names (catalog name, else their fallback, recursively; joined `", "` with `" and "` before
  the last) and `{param}` = bucket label; collapse runs of spaces and trim. Built only from the
  tree, so equal hash means equal name.
- `ItemNaming.Resolve(Item, NameCatalog, IItemNameSource?)` = catalog, else source, else fallback.

### Migration from spec 002
`IngredientStats`, `Ingredient`, `DishResult`, `DishScorer` are renamed as in Scope, not kept in
parallel. `ComboHash` is replaced by `Item.Hash` of a mix (new preimage, so the pinned "salt" vector
changes). All 22 `DishScorerTests` cases are ported to the new API: permutation tests become mix
permutations, id/stat validation tests move to `ItemCatalogTests`, empty list becomes
`ArityViolation`, null list `ArgumentNullException`, null element `ArgumentException`.
The 5 `SeededRandomTests` stay unchanged.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes, including `PASS sim: dotnet test` and
      `PASS sim: no UnityEngine references`. Total test count is >= 27.
- [ ] AC2: Hash of `potato` differs from `Fry(Cut(potato, sticks), 190, 150)`, and from `Cut(potato, sticks)`.
- [ ] AC3: Mix of 5 fixtures in all 120 permutations gives identical Hash, CanonicalTree, Stats,
      Surface, Components (sequence equal) and Rating. `Mix(Mix(a,b),c)` equals `Mix(a,b,c)` on the same fields.
- [ ] AC4: Order of an ordered chain matters: `Fry(Cut(potato))` and `Cut(Fry(potato))` (same params) differ in Hash.
- [ ] AC5: `Fry(Cut(potato, sticks), 190, 100)` and `(190, 170)` have equal Hash and fallback name
      but different Quality.
- [ ] AC6: With `TestItems` data: raw potato Quality < raw apple Quality, raw potato < fries
      (`Fry(Cut(potato, sticks), 190, 150)`), burnt potato (`..., 190, 400`) < fries.
- [ ] AC7: `Cut(Cut(potato, sticks), sticks)` and `Cut(Cut(potato, dice), halves)` return the input item's Hash.
- [ ] AC8: Depth 9 returns `DepthLimit` (chain `Fry` x 8 on potato); 17 components returns
      `ComponentLimit` (e.g. mix of three `Cut(Mix(...), halves)` items with 6 + 6 + 5
      components, since cut mixes are not flattened); 7 mix inputs after flattening returns `ArityViolation`; a plated input returns
      `ForbiddenInput`; temperature 250 or NaN returns `InvalidParam`.
- [ ] AC9: Pinned vectors: potato Hash equals the literal output of
      `printf '%s' 'v1|b|potato' | shasum -a 256`; `Cut(potato, sticks).Hash` equals SHA-256 of
      `"v1|o|cut|shape=sticks|" + potatoHash`; fries `CanonicalTree` equals
      `fry(temp=hot,time=medium;cut(shape=sticks;potato))`.
- [ ] AC10: Fries fallback name is `crispy fried potato sticks`; with a `NameCatalog` entry for its
      hash, `Resolve` returns `fries`; a stub `IItemNameSource` is used only when the catalog misses.
- [ ] AC11: Each heat transform has a test with exact expected stats (tolerance 1e-5) for one input.
- [ ] AC12: No file named `DishScorer.cs`, `Ingredient.cs`, `IngredientStats.cs` or `DishResult.cs` remains under `sim/`.

## Test plan
- `TestItems.cs`: catalog with the 6 spec 002 fixtures (same taste stats, Doneness 0,
  Moisture 0.5, window [0,1], HeatTolerance as before) plus:
  potato (0.1, 0, 0, 0.1, 0.1, fat 0, crunch 0.5, ht 0.6, doneness 0, moisture 0.8), window [0.55, 0.85];
  apple (0.6, 0, 0.3, 0.05, 0, fat 0, crunch 0.7, ht 0.3, doneness 0, moisture 0.85), window [0, 0.3].
  Expected rough Quality: raw potato ~0.13, apple ~0.40, fries ~0.69, burnt ~0.30.
- One test class per file listed in Scope. Command: `bash scripts/verify.sh`
  (fast loop: `dotnet test sim/Sim.sln`).

## Human steps
- Done 2026-11-05: spec 003 AC6 now uses `ItemFactory` and the pinned potato hash instead of
  `DishScorer.Score` and the old "salt" ComboHash.

## Open questions (answered 2026-11-05, human approved the defaults)
1. Bucket boundaries and labels: use the provisional values above; tune in playtesting
   (boundaries are not hashed, so retuning keeps identities).
2. Starting base items: only `TestItems` fixtures in this spec. Later about 15 items as a data
   file in `/game`, drafted with design-time LLM help and reviewed by the human.
3. Plate is the serving unit (NPC orders in spec 008 require a plated item). No presentation bonus in v1.
4. Crunch and Moisture are tracked only; a texture term comes in a later spec.
5. Fallback names are English only for now; localization later via bucket label ids.
6. Raw single foods (e.g. apple ~0.40) stay below cooked dishes in v1 by design; retune after playtests.

## Implementation notes
- 69 tests total (5 SeededRandom + 64 cooking). `bash scripts/verify.sh` passes. AC12 holds.
- Actual fixture Quality: raw potato 0.125, apple 0.397, fries 0.697 (doneness 0.695, in window), burnt 0.296
  (doneness clamped to 1, factor 0.25). All match the rough spec values; no formula was tuned.
- Deviation: `ItemNaming.FallbackName(Item, NameCatalog)` needs the name template and bucket labels, which the
  sketch does not put on `Item`. Added internal members `Kind`, `DisplayName`, `NameTemplate`, `BucketLabels` on
  `Item` (not public API). `ItemRater` uses internal `Kind` to detect Plate.
- Heat params are found by name by convention: `temp` and `time` (enforced in `OperationCatalog` validation,
  also `shape` for Cut). Added extra catalog validation (bucket ascending, exactly one of temp param or fixed temp).
- `NameCatalog.Empty` and `ItemRater.Taste(ItemStats)` are extra public helpers. Input names in a mix fallback follow
  canonical (hash) order, not alphabetical.
- Heat on a cut/mixed item uses the item's (weighted mean) HeatTolerance and Surface for every component.
- AC11 expected values are derived by hand in the test comments (fry/saute: T=180,t=90; boil: t=600).

## Blockers
(Builder fills this in after 3 failed attempts.)

---
id: 062c
title: Furniture shop and upgrades (buy pieces at the terminal, tiers with real effects, sell back, climb purchases as pieces)
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything") with the recommended defaults of its Open questions.

# 062c: Furniture shop and upgrades

## Goal
Money gets visible, touchable uses (pillar 3, "money always has something worth buying"). The crew buys kitchen pieces
at the office terminal, places them in build mode, replaces a piece with a better tier (a fryer with more baskets, a
stove that cooks faster) and sells pieces back for part of the price. The climb's purchases that are things in a room
(052, 052b) become real pieces instead of invisible unlocks.

**Integration spec** (/sim and /game).

## Depends on
062a (layout, `Tier` and `PaidCents` fields), 062b (storage pallet, build mode, door Closed), 059 (terminal apps,
change log), 057 (`EconomyService.TrySpend`, rent owed blocks purchases, `BoardConfirm`, stage 2), 052/052b
(`ClimbCatalog` purchases, `MaxCount`, `CostOf`), 053 (`FoodCornerRules.Scale`), D39 (cents), D40 (crew-size curve,
tuned for 4 players).

## Scope
**/sim** `sim/Runtime/Layout/` (new): `PieceTier.cs` (record), `FurnitureCatalog.cs` (`CreateDefault`, validation),
`FurniturePricing.cs` (pure: price, crew factor, sell-back, upgrade). Tests in `sim/Sim.Tests~/Layout/`.

**/game** (under `game/Assets/_Project/`):
- `Scripts/Layout/` (new): `FurnitureService.cs` (server: buy, upgrade, sell), `FurnitureAppRules.cs` (pure: rows and
  texts), `CrewPricing.cs` (pure: the crew permille at the last service start, the one place D40 is read).
- `Scripts/Office/TerminalApp.cs` (change: `Furniture` app), `OfficeTerminalView.cs` (draw its rows).
- `Scripts/Layout/PieceBehaviour.cs` (change: `SyncVar<byte> Tier`), `LayoutSpawner.cs` (tier prefab variants; swap in
  place).
- `Scripts/Cooking/FryerRules.cs` (change: `BasketsPerFryer` becomes `MaxBaskets = 4` and a per-fryer count),
  `FryerBasket.cs`, `KitchenService.cs` (the stove's cook speed on its burners' heat step), `Tutorial/TutorialTargets.cs`
  (basket array sized by `MaxBaskets`).
- `Scripts/Climb/ShopStage.cs` (change: unlocks follow owned pieces; `CrewAtServiceStart`, saved).
- `Editor/PiecePrefabBuilder.cs` (fryer tier 2 with 3 baskets; stove tier 2 stand-in; `corner_sign`, `plant` pieces),
  the regenerated prefabs, `DefaultPrefabObjects.asset`.
- `Scripts/Save/` (piece `tier` and `paidCents` already exist; `climb.crewAtServiceStart`; next version, defaults only).
- `Scripts/Net/ProtocolSurface.cs` (+1), `Scripts/Dev/FurnitureTourRunner.cs` (new), `scripts/visual-check.sh`.
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.

## Out of scope
Machines whose mechanics do not exist yet (dishwasher, auto-fill drinks, fry dump: listed, "comes later"). Staff.
Tables and buffet (stage 3). Cars and the food truck (stage 5). Decor art (art pass). Fridge and shelf capacity tiers.
Price haggling, loans, second-hand markets.

## Design

### Catalog (`FurnitureCatalog`, data in /sim, every amount in cents)
`record PieceTier(string TypeId, int Tier, string Name, long PriceCents, string? ClimbPurchaseId, int MinStage,
bool ScalesWithCrew, bool Buyable, int Baskets, int CookSpeedPermille)`. A climb-linked row takes its price from the
climb catalog (`CostOf(count)` scaled with `FoodCornerRules.Scale`, as 057 does), so 052b's tuning flows in.

| Type | Tier | Name | Price | Climb id | Stage | Crew-scaled | Effect |
|---|---|---|---|---|---|---|---|
| `worktop` 500 mm | 1 | Short worktop | $40 | - | 2 | no | - |
| `worktop` 1000 mm | 1 | Worktop | $70 | - | 2 | no | - |
| `front_counter` 1000 mm | 1 | Counter piece | $90 | - | 2 | no | - |
| `fryer` | 1 | Fryer | $250 | - | 2 | yes | 2 baskets |
| `fryer` | 2 | Three-basket fryer | climb | `second_basket` | 1 | yes | 3 baskets |
| `stove` | 1 | Stove | $200 | - | 2 | yes | cook speed 1000 |
| `stove` | 2 | Quick stove | $450 | - | 2 | yes | cook speed 1250 |
| `corner_sign` | 1 | Corner sign | climb | `corner_sign` | 1 | yes | its climb effect |
| `plant` | 1 | Plant | climb | `decor_1` | 2 | yes | its climb effect |
| `dishwasher`, `auto_fill_drinks`, `fry_dump` | 1 | as named | climb | same ids | 4 | yes | `Buyable = false` |
All numbers are provisional (tuning changes numbers, not rules). Validation: tiers of a type share the footprint and the
surface layout (a test compares the prefabs), tiers are 1..N without gaps, prices > 0, climb ids exist.

### Prices (`FurniturePricing`, pure)
- `Price(row, crewPermille) = row.ScalesWithCrew ? floor10(row.PriceCents * crewPermille / 1000) : row.PriceCents`
  (`floor10` rounds down to whole 10 cents, so cash change stays coin-exact).
- `crewPermille` comes from `CrewPricing.PermilleAtServiceStart()`: the D40 curve for N = players connected at the
  last service start (stored in `ShopStage.CrewAtServiceStart`, saved; 1 before a first service). D40 is not yet written
  in docs/decisions.md; until a D40 function exists in /sim, `CrewPricing` returns 1000 (open question 1).
- `SellBack(paidCents) = floor10(paidCents * 500 / 1000)`. `UpgradePrice(next, current) = max(0, Price(next) -
  SellBack(current.PaidCents))`.

### Buying, upgrading, selling (`FurnitureService`, server; every check server-side)
- **Buy** (Furniture app row "Buy · $250"): two presses within 4 s by the same player (`BoardConfirm`), then
  `TrySpend`. Refused: short of money, rent owed (057: "Pay the rent first"), stage below `MinStage`, `Buyable` false
  ("Comes later"), the climb `MaxCount` reached. The new piece gets a fresh id and goes to the storage pallet (062b) with
  `PaidCents`; the feed says "Mira bought a fryer: it waits on the pallet". A climb-linked piece adds its id to the
  climb unlocks at once (its effect starts at the next service, as 057's purchases do).
- **Upgrade** (row "Upgrade · $320" on a placed or stored piece of that type): same confirm and refusals, plus the door
  must be Closed for a placed piece. The piece keeps its id, pose and surface ids; the server despawns the old prefab and
  spawns the tier prefab with the same `PieceId` (items on its surfaces stay on the same spots; baskets in use refuse:
  "Empty the fryer first"). `PaidCents` becomes old paid plus the upgrade price.
- **Sell** (row "Sell · $125"): door Closed for a placed piece. Refused for `Required` pieces and for the last piece the
  current stage dish needs (the fryer at stages 1 and 2). Items on it go to the nearest free worktop spot. A climb-linked
  piece removes its id from the unlocks (its effect ends at the next service).
- Every action writes a 059 change-log entry and an autosave `LayoutChanged`.

### Tier effects in the game
- **Fryer baskets:** `FryerRules.MaxBaskets = 4`; each fryer's `BasketCount` comes from its tier (2 or 3). Option ids
  for basket buttons keep their packing; the third basket uses the next option values. The tutorial and the cook tour
  use baskets 0 and 1 only.
- **Stove cook speed:** where the stove advances a pan or pot on its burner, the seconds fed to the heat step are
  `real * CookSpeedPermille / 1000`. D20 buckets read those effective seconds, so the item identity rules are unchanged.
- **Climb-linked pieces:** their effect is the climb purchase's, read where 057/058 already read owned purchases.

### Furniture app (`FurnitureAppRules`)
Pages: "Buy" (every buyable row for this stage, then "comes later" rows greyed), "Your pieces" (by type: placed and
stored counts, and the upgrade or sell price of the selected one). Plain words: "Quick stove: food on it cooks a
quarter faster. $450." Never a table (059's line check).

### Wire
`PieceBehaviour.Tier` (SyncVar<byte>), `ShopStage.CrewAtServiceStart` (SyncVar<byte>); terminal presses use 059's
options; no new RPC. `ProtocolSurface.Revision` + 1.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: /sim: catalog validation (footprint mismatch, tier gap, unknown climb id all throw); `Price`, `SellBack`,
      `UpgradePrice` literals at crew permille 1000 and 1500 (with the 10-cent floor); climb-linked prices follow a test
      climb catalog with other costs.
- [ ] AC3: Buy: each refusal (short, owed, stage, not buyable, max count, one press only, another player's second
      press); a bought piece is on the pallet with its paid cents; the unlock is added.
- [ ] AC4: Upgrade: the id, pose and surface ids stay; an item on the stove's top stays on its spot; a full basket
      refuses; while Open a placed piece refuses.
- [ ] AC5: Sell: required and last-fryer refusals; sell-back paid; the unlock removed; items moved off.
- [ ] AC6: Effects: a tier 2 fryer has 3 working baskets (scene test); the stove's effective seconds at 1250 permille
      are 1.25 times real (pure test); a tier 1 kitchen behaves as before (the cook tour unchanged).
- [ ] AC7: Save: tiers and paid cents round-trip; `crewAtServiceStart` defaults to 1.
- [ ] AC8: Surface tests change only by the two synced fields; `Revision` + 1.
- [ ] AC9: `--tours furniture`: buy a worktop, place it, upgrade the fryer to 3 baskets, fry three portions at once,
      sell the worktop. 0 Exception lines. Earlier tours still pass.
- [ ] AC10: `WhatsNew.json` entry; every name and line passes the D27 word test.
- [ ] AC11 (manual, human): buying and upgrading feels worth the money.

## Test plan
`sim/Sim.Tests~/Layout/`: `FurnitureCatalogTests`, `FurniturePricingTests`. `Tests/EditMode/`:
`FurnitureServiceTests`, `FurnitureAppRulesTests`, `FryerTierTests`, `StoveSpeedTests`, `CrewPricingTests`, updated
save and surface tests. Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours furniture`.

## Human steps
Write D40 into docs/decisions.md with its numbers (open question 1). AC11 play-test.

## Open questions (recommended default in brackets)
1. D40 in code: where the crew curve lives and which purchases scale. [D40 gets a /sim function in its own spec;
   until then `CrewPricing` returns 1000; rows marked crew-scaled above]
2. Sell-back share. [50 %, rounded down to 10 cents]
3. Upgrade price. [the new tier's price minus the old piece's sell-back]
4. Which tiers now. [fryer baskets and stove speed only; fridge and shelf capacity later]
5. Where a bought piece appears. [on the storage pallet; the crew places it in build mode]
6. Stage 1 purchases (`second_basket`, `corner_sign`), before build mode exists for the crew. [buyable at stage 1;
   the fryer upgrade happens in place; the server puts the corner sign at an optional default pose listed in
   `default.json`, instead of the pallet]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

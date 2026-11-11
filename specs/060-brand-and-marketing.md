---
id: 060
title: Brand and marketing (your name, colours and logo; flyers, radio, neon, a food critic, samples; brand awareness)
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything") with the recommended defaults of its Open questions.

# 060: Brand and marketing

## Goal
The restaurant becomes yours to name and dress (pillar 2), and money gets a growth use besides furniture: marketing that
brings measurably more guests for a while, computed deterministically in /sim through 052's guest-choice model, and a
slow brand awareness that rewards steady good service. The crew sees the effect in Results (059), in plain words.

**Integration spec** (/sim and /game).

## Depends on
058 (`RivalDemand`, your attraction, the rankings board, reputation), 059 (terminal apps, Results, change log), 062c
(furniture rows: the neon sign is a piece), 057 (`TrySpend`, `BoardConfirm`, rent owed blocks spending, stage 2),
D27, D28, D32, D38, D39, D40. O6 (moderation, OPEN) is avoided: no free-text names in this spec.

## Scope
**/sim** `sim/Runtime/Marketing/` (new, `Worktown.Sim.Marketing`): `MarketingOption.cs` (record),
`MarketingKind.cs` (enum), `MarketingCatalog.cs` (`CreateDefault`, validation), `MarketingState.cs` (record),
`MarketingEngine.cs` (pure), `CriticOutcome.cs` (record), `AwarenessRules.cs` (pure). `sim/Runtime/Layout/`
`PieceCatalog.cs` and `FurnitureCatalog.cs` (change: a `neon_sign` row). Tests in `sim/Sim.Tests~/Marketing/`.

**/game** (under `game/Assets/_Project/`):
- `Scripts/Brand/` (new, `Worktown.Game.Brand`): `BrandWords.cs` (pure word lists), `Brand.cs` (struct),
  `BrandPacking.cs`, `BrandRules.cs` (pure: name, logo letter, texts), `ShopBrand.cs` (NetworkBehaviour, synced brand),
  `BrandAppRules.cs`, `ShopSignView.cs` (client: name and logo on the shop front), `LogoView.cs` (client stand-in).
- `Scripts/Marketing/` (new): `MarketingService.cs` (server: buy, run, apply at service end), `MarketingAppRules.cs`
  (pure), `MarketingLines.cs` (hand-written feed and paper lines).
- `Scripts/Office/TerminalApp.cs`, `OfficeTerminalView.cs` (Brand and Marketing apps), `ResultsRules.cs`,
  `ServiceRecord.cs` (the marketing line and `extraGuests`).
- `Scripts/Rivals/RivalDemand.cs` or `RivalRules.cs` (change: your attraction times the bonus), `RankingBoardView.cs`
  (your row shows the brand name and colours). `Scripts/Shop/MenuBoardView.cs` (name header). Cup and apron tint hooks
  in `Scripts/Art/` (palette colours only). `FoodCornerRules.ShopName` stays the default name.
- `Scripts/Save/` (`brand` and `marketing` parts; next version), `Scripts/Net/ProtocolSurface.cs` (+1),
  `Editor/PiecePrefabBuilder.cs` (neon sign stand-in), `Scripts/Dev/BrandTourRunner.cs`, `scripts/visual-check.sh`.
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.

## Out of scope
Free-text shop names (waits for O6). Logo editors beyond the shape picker. Real ad partners, real brands or products.
Marketing in the climb simulation (open question 5). Events, competitions, the town paper as a system (058 "later").
Any AI text (D38). Art beyond stand-ins (the human's art pass).

## Design

### Brand (`Brand`, packed into one int: `ShopBrand.SyncVar<int> Packed`)
- **Name:** an adjective and a noun from `BrandWords` (24 each, 5 bits each), for example "Sunny Spoon", "Little
  Lantern", "Green Kettle", "Corner Garden". Every combination passes `ContentRules.FindForbidden` (D27), refers to no
  past (D28), differs from every rival name (058), and is not on a small list of real chain names (test).
- **Colours:** sign main, sign trim, uniform, cup: four of the 16 palette colours (D32), 4 bits each.
- **Logo:** a shape (circle, square, diamond, leaf, sun, wave: 3 bits) in the sign colours, with the name's first letter.
- **Where it shows:** the shop sign on the street front, the menu board header, cups (body tint), players' aprons,
  your row on the rankings board, the HUD and summary lines that say "Your restaurant" today.
- **When:** the Brand app is open from stage 2; until a brand is chosen, today's names stay ("Your food corner",
  "Your restaurant"). Changing it is free and allowed only while the shop is Closed (it repaints the sign).

### Marketing options (`MarketingCatalog.CreateDefault()`, provisional, cents)
| Id | Name | Stage | Cost | Crew-scaled | Lasts | Bonus permille | Awareness |
|---|---|---|---|---|---|---|---|
| `flyers` | Flyers | 2 | $20 | no | 2 services | +60 | +20 at the end |
| `radio` | A retro radio spot | 2 | $150 | yes | 4 services | +120 | +60 at the end |
| `neon_sign` | Neon sign (a 062c furniture row, bought and sold there) | 2 | $400 | yes | while placed | +40 | +2 per service |
| `critic` | Invite a food critic | 2 | $80 | no | the next service | 0 | see below |
| `samples` | Samples at the market | 2 | $10 and 8 stage-1 dish ingredients from stock | no | 1 service | +80 | +40 |
- One of each kind at a time (a second buy is refused: "Already running"); the critic has a 5-service cooldown.
- Buying uses 059's app, two presses within 4 s, `TrySpend`, refused while rent is owed. Crew-scaled costs use 062c's
  `CrewPricing`. Samples also need the stock (refused: "Not enough potatoes for samples").

### Effects (/sim `MarketingEngine`, pure, no RNG)
- `Bonus(state, catalog) = min(400, sum of running bonuses + neon + awareness * 150 / 1000)` permille.
- In 058's hourly split your attraction becomes `A_you * (1000 + bonus) / 1000` (after `GuestChoice.Attraction`, so the
  `Min` floor and the rivals are unchanged). The split's RNG draws are the same in number and order.
- **Measured:** `ExpectedExtra(attractions, bonus, units)` = `units * (share with bonus - share without)`, from the same
  attractions, with no extra draws; summed per service into `ServiceRecord.extraGuests`. Results says "Marketing: the
  radio spot brought about 4 more guests (2 services left)."
- **Awareness** (`AwarenessRules`, 0..1000): at each service end, `a += gains + min(20, served / 2)`, then `a -= a * 20 /
  1000` (2 % fades per service played). Nothing changes while nobody plays.
- **Food critic** (`CriticOutcome`, deterministic on the critic's service stars, `GateRules.ServiceStarsTenths`):
  - 4.2 stars or more: reputation +80 permille, awareness +150, feed "The critic loved it!", and the line on the
    rankings board's ticker (058).
  - 3.5 to 4.1: reputation +20, awareness +60, "The critic liked it."
  - below 3.5: reputation -60 (never below the stage floor, 058's clamp), awareness +30, "The critic was not impressed."
  - A service with no guests counts as below 3.5. The risk is the crew's own play, not a die roll.
- Rivals react only as 058 says (shares), so marketing that wins guests can trigger a rival's price cut.

### Synced state and save
`ShopBrand.Packed` (SyncVar<int>); `MarketingService`: `SyncVar<long> RunningPacked` (per kind: services left 4 bits,
active 1), `SyncVar<short> Awareness`, `SyncVar<short> BonusPermille`. Save: `brand` (packed int, `chosen`) and
`marketing` (running kinds with services left, critic cooldown, awareness). Migration fills defaults; clamps on load.
`ProtocolSurface.Revision` + 1; surface tests change only by these lines.

### Texts
Every name, word, feed line and ticker line is hand-written in `BrandWords` and `MarketingLines`: no animal products (D27),
no real atrocities or the dark past as a joke (D28), no real brands, no harm words (058's list).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: /sim: catalog validation; `Bonus` literals including the 400 cap; `ExpectedExtra` literals (two equal shops,
      +100 bonus); awareness growth and fade literals over 10 services; the critic's three bands and the floor clamp;
      the same inputs give the same state.
- [ ] AC3: The split with bonus 0 equals 058's split exactly (same seed, same tick); with a bonus your share rises and
      the RNG draw count is unchanged.
- [ ] AC4: Buying: each refusal (short, owed, running, cooldown, stage, stock for samples, one press only); a running
      option counts down per service and ends with its awareness gain.
- [ ] AC5: `BrandPacking` round-trips every field at its maximum; every name combination passes the D27 check, the
      no-harm list, the rival-name check and the real-brand list.
- [ ] AC6: The brand shows on the sign, the menu board header and the rankings board row (scene test on the views'
      texts); cups and aprons take palette colours only.
- [ ] AC7: Results shows the marketing line with `extraGuests`; Save round-trips brand and marketing; version 99 newer.
- [ ] AC8: Surface tests change only by the listed fields; `Revision` + 1; no new RPC.
- [ ] AC9: `--tours brand`: name the shop at the terminal, the sign changes, buy flyers, a service runs, Results shows
      the extra guests, invite the critic, the next service ends with a critic line. 0 Exception lines.
- [ ] AC10: earlier tours still pass; `WhatsNew.json` entry.
- [ ] AC11 (manual, human): a radio spot feels worth it; the critic feels exciting, not random.

## Test plan
`sim/Sim.Tests~/Marketing/`: `MarketingCatalogTests`, `MarketingEngineTests`, `AwarenessRulesTests`,
`CriticOutcomeTests`. `Tests/EditMode/`: `BrandPackingTests`, `BrandWordsTests` (D27, rivals, real brands),
`MarketingServiceTests`, `MarketingSplitTests`, `MarketingAppRulesTests`, updated Results, save and surface tests.
Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours brand --out .verify/visual060`.

## Human steps
Read the word lists and change any word you dislike; AC11 play-test.

## Open questions (recommended default in brackets)
1. Shop name entry while O6 is OPEN. [two-word picker from fixed lists; free text only after O6 is decided]
2. Stage gates for marketing. [everything from stage 2; D34 puts "better marketing" in stage 5, so stage 5 later adds
   bigger campaigns (052's `campaign_1..3`)]
3. Does the critic use luck? [no; the outcome follows the service's stars]
4. Bonus cap and awareness weight. [cap +400 permille; awareness at most +150]
5. Marketing in the climb simulation (052). [not now; a later /sim spec adds it and re-runs the report, because more
   guests change the stage pace]
6. Brand changes. [free, only while Closed]
7. Samples cost. [8 portions of the stage 1 dish's ingredients plus $10]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

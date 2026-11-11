---
id: 058
title: NPC rivals in the game (named shops in town, guests choose between shops, rivals react, rankings board)
status: draft
owner: architect
builder: opus
---

# 058: NPC rivals

## Goal
The town gets the 052 rival teams as named shops with an owner and a sign. Guests choose between your shop, the rival
on your channel and "elsewhere" with 052's attraction model, decided on the server. Rivals react to you with price cuts
and quality steps, and their standing is visible on a rankings board, in the shift summary and in what guests say. No
crime and no harm (pillar 5). This is the game side of docs/game-design.md "Opponents and status" and critique P3.

## Depends on
D27, D28, D33, D37, D38; spec 052 (`RivalDef`, `RivalState`, `GuestChoice`, `ClimbEngine.NextReputation`,
`ChoiceWeights`, `CounterOf`, `RivalsOn`); spec 053 (save, stage 1); **spec 057 built and merged** (save v2, stage 2,
`GateRules.ServiceStarsTenths`, the frozen menu). 052b is not needed (it changes no rival code), but see open question 2.

## Scope
Only `/game` (plus the tour hook in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.
- `Scripts/Rivals/` (new, namespace `Worktown.Game.Rivals`): `RivalLooks.cs` (pure table: owner, look, colour,
  tagline, street spot), `RivalRules.cs` (pure: attractions, active rivals, your quality and wait), `RivalDemand.cs`
  (pure: the hourly split), `RivalPacking.cs` (pure: the synced bit layout), `RivalLines.cs` (pure: hand-written lines
  and the picker), `RankingRules.cs` (pure: board rows), `RivalBook.cs` (server NetworkBehaviour: state, split,
  reactions, synced fields), `RivalFront.cs` (client: sign, tags, owner, queue figures), `RankingBoardView.cs` (client).
- `Scripts/Npc/CustomerService.cs` (change: visible demand minus rival-held units; the split runs first in `OnHour`;
  rival lines in leave texts), `Shift.cs`, `ShiftSummary.cs` (change: total wait of served guests).
- `Scripts/Save/ShopSave.cs`, `ShopSaveCodec.cs`, `SaveMigrations.cs` (v2 to v3), `SaveService.cs` (change).
- `Scripts/UI/ShiftSummaryView.cs` (change: the town line), `Scripts/Net/ProtocolSurface.cs` (Revision + 1).
- `Editor/GrayboxSceneBuilder.cs` (change: four rival fronts, the rankings board, the `RivalBook` object) and the scene.
- `Scripts/Dev/RivalTourRunner.cs` (new), `scripts/visual-check.sh` (change: `rivals` tour).
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.
- No change in `/sim`; rival names and numbers come from `ClimbCatalog`. No change to specs 040, 046, 050, 051.

## Out of scope
- Cooking competitions, events, the town paper, a "beat the rival" goal, delivery and tables rivals competing (their
  channels are stage 3), Steam leaderboards, rivals copying dishes or bidding for events (see "Early Access and later").
- Any rival action on your shop: no stock, wallet, station, guest or queue writes besides the demand split.
- Player-set prices (037), crew-size scaling of guests (a 052b follow-up), new art beyond graybox (040), the street
  life dressing of 050 (flip letters, fans, pennants).
- Any name or line from the AI at runtime (D38); every text here is hand-written.

## Design

### Rivals and their faces (`RivalLooks`, one row per catalog rival; a test fails if a rival has no row)
| Rival (`RivalDef.Name`) | Channel | Owner | Tagline | Active |
|---|---|---|---|---|
| Corner Kiosk | corner_counter | Tobi | "Fries since sunrise" | stage 1 |
| Mira's Wraps | street_counter | Mira | "Rolled by hand, every one" | stage 2 |
| Dash Bowls | delivery | Lu | "Hot bowls, quick feet" | stage 3 (shown, inactive) |
| The Long Table | tables | Rosa | "One long table, room for all" | stage 3 (shown, inactive) |
- Shop names come only from `ClimbCatalog.Rivals` (one source; 052 checks them with `ContentRules.FindForbidden`).
  Owners and taglines are hand-written here, D27-checked, with no reference to the past (D28).
- Each `RivalFront` is a graybox facade on an existing street house (no interior, no collider beyond the wall): an
  awning and sign in one palette colour (D32), a hatch, the owner standing at it (a 024 character, fixed look index,
  idle loop, not networked), up to 3 static queue figures, and two tags: "Price cut" (cuts > 0) and "New recipe"
  (quality steps > 0). An inactive front shows "Opening soon". Spots: four facades that do not block the shop entry, the
  queue path or the bin; the builder lists them in `RivalLooks` and the scene test checks that.

### Who is active (`RivalRules.Active(stage)`)
`catalog.RivalsOn(catalog.CounterOf(stage).Id)`: stage 1 the Corner Kiosk, stage 2 Mira's Wraps; stage 0 (open
kitchen, tours, tests): none, so the open kitchen is unchanged.

### Guests choose between shops (`RivalDemand`, server, once per in-game hour)
- When the shift is open, `CustomerService.OnHour` calls `RivalBook.ServerSplitHour(tick)` before `Sync()`. For each
  market that sends guests at this stage (`CustomerRules.WishOrder` order), every unit of the hour's open demand (all
  new: spec 008's NPC businesses fill the rest at each tick) gets one draw `GuestChoice.Pick` over `[you, active rivals
  in catalog order, outside]`. RNG: `EconomyRng.ForTick(worldSeed ^ RivalSalt, tick)` (`RivalSalt = 0x58`), separate from
  every order RNG, so the guests' own draws do not change.
- `Held(m)` = units won by rivals or outside this hour. Your visible demand becomes `max(0, OpenDemand(m) - Held(m))`
  (the reconcile and `MaxVisible` work as today). Held resets at the next hour.
- Attractions (`GuestChoice.Attraction`, all in sim units, `catalog.Weights`):
  - you: `q = clamp((lastStarsTenths - 10) x 20, 0, 1000)` (the inverse of 052's stars), 500 before a first service;
    `rep` = your reputation; price offset 0 (until 037); `avgRef` = mean `ReferencePrice` of your frozen menu's catalog
    dishes; `wait` = last service's mean wait of served guests in real seconds (60 before a first service); dishes =
    menu count;
  - rival: `state.QualityOf(def)`, `def.ReputationPermille`, `state.OffsetOf(def, catalog.MoneyScale)`, your `avgRef`,
    `def.WaitSeconds`, 1 dish; outside: `Weights.Outside`.
- **What this means today:** the town makes about 40 snack units per 6-hour service (120 NPCs x 0.8 a day over 14
  hours), so a 35-45 % share (about 15 guests) still exceeds a solo cook's 4.4 and the stage 1 pace does not change.
  Larger crews and stage 2 feel the rival. Open question 2.

### Reputation and the rivals' reactions (server, at each service end with a stage >= 1)
- `tenths = GateRules.ServiceStarsTenths(summary)` (057); if the service counted: `rep = ClimbEngine.NextReputation(rep,
  tenths, StageOf(stage).ReputationFloorPermille)`; start `max(ClimbEngine.StartReputation, stage 1 floor)`. A move to
  stage 2 lifts rep to at least the new floor. `lastStarsTenths` and `lastWaitSeconds` are stored.
- Per active rival: `state = state.React(def, yourChosen, rivalChosen)` with the service's summed draws (052 step 7).
  A change raises one feed line from `RivalLines`: "Mira's Wraps cuts its prices", "Mira's Wraps has a new recipe", or
  "Mira's Wraps is back to its old prices" (undo). Inactive rivals keep their state.
- Rivals never act otherwise. `RivalBook` exposes no method that touches the wallet, stock, stations or guests (a
  reflection test pins its public surface).

### Visible status
- **Rankings board** ("Town favourites"), a graybox board on the street in sight of the shop: you ("Your food corner" /
  "Your restaurant", fixed until O6) and the four rivals, ranked by rating stars = `permille / 200` with one decimal
  (yours = reputation; a rival's = its `ReputationPermille`), ties by catalog order with you last; one ticker line with
  the newest reaction. `RankingRules.Rows(...)` is pure; `RankingBoardView` draws it from the synced fields.
- **Shift summary** line: "In town today: you 9 · Mira's Wraps 12 · elsewhere 15" (chosen units of the service).
- **Guests mention rivals** (`RivalLines`): at most 1 in 5 leaving guests, picked by `(guestObjectId x 31 + day) % 5 ==
  0` (no RNG), and only with an active rival. Templates (`{r}` = the active rival's name):
  - served, 4+ stars: "Better than {r}!", "I'm telling {r} about this."
  - served, the rival has a price cut: "{r} is cheaper, but this is worth it."
  - missed (patience): "Maybe {r} is quicker today."
  - the rival has a new recipe: "Heard {r} has a new recipe."
- **Owners:** on a reaction the owner's bubble shows one short line for 6 s ("Prices down today!", "Try my new
  recipe!", "Good luck in the big kitchen!" for the kiosk after your move).
- Lines are friendly rivalry: never an insult, a threat or harm. A test runs every string through the D27 check and a
  small no-harm word list (steal, sabotage, poison, burn, break, fight, hurt, kill, destroy, attack).

### Synced state (`RivalBook`, server writes, no RPC)
`SyncVar<long> RivalsPacked` (per catalog rival slot 0..3, 14 bits: cuts 4, steps 4, queue figures 2, active 1, last
reaction 3; `RivalPacking` pure and tested), `SyncVar<short> YourRatingPermille`, `SyncVar<long> TownLinePacked` (you,
rival, elsewhere: 21 bits each). Written at most once per in-game hour and once per service end. Queue figures = `min(3,
rivalChosenThisHour / 3)`. `ProtocolSurface.Revision` + 1; surface tests change only by these lines.

### Save format v3
`formatVersion = 3`; new `RivalPart[] rivals` (`id`, `priceCuts`, `qualitySteps`, `aheadStreak`, `behindStreak`) and
`rep` (`reputation`, `lastStarsTenths`, `lastWaitSeconds`). Migration v2 to v3: every catalog rival at `Start`,
reputation as above, 0 tenths (no service), 60 s. Clamps: cuts 0..`MaxPriceCuts`, steps 0..`MaxQualitySteps`, streaks
0..2, reputation floor..1000; unknown rival ids ignored but kept.

## Early Access and later (what this spec does not build)
- **Before EA, later specs:** Dash Bowls and The Long Table compete once delivery and tables exist (a stage 3 spec maps
  those channels to markets; `RivalRules.Active` already follows the catalog); a "beat the rival" goal step (out-rate
  them over 5 services, critique P3); the town paper as templates over sim facts; **one cooking competition** against
  the best NPC team (stage 5, 052 `cook_off`, design v1 "Early Access").
- **After EA:** rivals copy your most popular dish, bid for event contracts, announce grand openings; more rivals per
  channel; weekly challenge with Steam leaderboards per crew size (needs a real app id and a leaderboard-integrity call).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `RivalLooks` has a row for every `CreateDefault()` rival; every name, owner, tagline and line passes the D27
      check and the no-harm list.
- [ ] AC3: `RivalDemand` on hand-built attractions: 10,000 units between two equal shops split 50 % +- 3 %; held never
      exceeds open demand; the same tick and seed give the same split; stage 0 holds nothing; the order RNG draws the same
      values with and without rivals.
- [ ] AC4: `RivalRules` literals: your quality from stars (1.0 -> 0, 3.5 -> 500, 5.0 -> 800), first-service defaults,
      rival quality and offset after cuts and steps, active rivals per stage 0/1/2.
- [ ] AC5: Reactions: three services ahead give one cut; cuts used up give a quality step; maximums hold; three
      services far behind undo one step; each change raises exactly one feed line.
- [ ] AC6: Reputation follows `NextReputation`, never below the stage floor, lifted at the move to stage 2.
- [ ] AC7: `RankingRules`: order, ties, star text, ticker; `RivalPacking` round-trips every field at its maximum.
- [ ] AC8: Save: a v2 fixture (057) migrates to v3 with defaults; a v3 fixture round-trips equal; out-of-range values
      clamp; a version 99 file is `NewerVersion`.
- [ ] AC9: `RivalBook`'s public surface (reflection) has no member that takes a wallet, stock, station or guest type;
      surface tests change only by the three synced fields; Revision + 1.
- [ ] AC10: `bash scripts/visual-check.sh --tours look,walk,cook,menu,corner,restaurant` still pass, 0 Exception lines.
- [ ] AC11: `--tours rivals` (stage 2 fixture save): the four fronts with owners and signs, the rankings board, a forced
      reaction (a fixture with streaks at 2) showing the "Price cut" tag and its feed line, a guest line naming Mira's
      Wraps, the summary town line. 0 Exception lines. The tour logs per hour: units, yours, each rival's, outside.
- [ ] AC12 (manual, human): play stage 2 for three services; say whether the rival is noticeable and fair.
- [ ] AC13: `WhatsNew.json` entry.

## Test plan
- EditMode: `RivalLooksTests` (AC2), `RivalDemandTests` (AC3), `RivalRulesTests` (AC4), `RivalReactionTests` (AC5),
  `RivalReputationTests` (AC6), `RankingRulesTests` and `RivalPackingTests` (AC7), updated `ShopSaveCodecTests` (AC8),
  `RivalBookSurfaceTests` and updated surface tests (AC9), the D27 word test and a `RivalLinesTests` no-harm check.
- Fixtures as string constants: v2 stage 2, v3 full, v3 out of range, version 99.
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours rivals --out .verify/visual058`, then the
  AC10 tours.

## Human steps
AC12 play-test; answer or accept the open questions (owner names especially).

## Open questions (recommended default in brackets)
1. The "outside" option in the game. [on, as 052 (`Weights.Outside`), so the game matches what the sim tuned]
2. Demand basis: the town economy (about 40 snack units per service) or 052's channel ceilings. [the economy for now;
   rivals then bite only for larger crews. A calibration spec after 057's AC12 and 052b decides whether the town demand
   follows 052's ceilings and crew scaling, which would make the rival felt by small crews too]
3. The Corner Kiosk competes at stage 1. [yes, as 052 models it; with today's demand it does not slow the solo pace]
4. Your price in the attraction before 037. [offset 0: you sell at the reference]
5. Wait measure. [mean real seconds of served guests, as 052's seconds; the rivals' `WaitSeconds` are tuned later if
   the measured game waits sit far above them]
6. Ranking metric. [reputation as stars; the rivals' fixed 052 values, their quality steps show as a tag only]
7. Owner names and taglines. [Tobi, Mira, Lu, Rosa as above; note: the storyboard draft calls the farmer "Mira" too,
   so one of them should be renamed]
8. Guest line rate. [at most 1 in 5 leaving guests]
9. The 050 overlap (rankings board, rival stalls). [058 builds the working graybox board and fronts; 050 later dresses
   them; 050 is not edited]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

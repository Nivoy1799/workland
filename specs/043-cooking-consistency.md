---
id: 043
title: Cooking consistency (perfect means best, names match the windows, one pace)
status: approved
owner: architect
builder: opus
---

# 043: Cooking consistency

## Goal
Reviews found the same pattern on every heat station:
- the best quality comes at the end of the good window, not at the "perfect" cue (spit 70 s vs 55 s; the fryer and
  griddle the same);
- names and menu buckets start before the good window (the griddle's "grilled" at 9 s, the window at 12 s), so an
  undercooked plate can be an exact order.

Players learn the cue, so the cue must be the truth.

## Design
- **Perfect is best.** The rating's doneness term peaks exactly at the Perfect zone. Good scores a little less, and
  overdone falls off. This applies to the pan, pot, fryer, griddle and spit.
- **Names and menu buckets follow the zones.** "Golden", "grilled" and "cooked" start where Good starts. Undercooked
  food is named "pale" or "half-grilled" and matches an order only as Close.
- **One pace table** (docs/cooking-roadmap.md): every station's Good, Perfect and Burnt in one file, with the inbox's
  "2x faster" default.
- Old hashes change, because the buckets move. All pinned values are re-pinned with a written reason.

## Acceptance criteria
- [x] AC1: `verify.sh`.
- [ ] AC2: Sim tests per station:
  - the quality maximum lies inside Perfect;
  - the name boundaries equal the zone boundaries;
  - an undercooked order is Close, not Exact.
- [ ] AC3 (manual, human): shave or raise at "perfect" and get the best stars.
- [ ] AC4: `WhatsNew.json`.

## Implementation notes
Built 2026-11-11 (builder, opus), on top of 042. Merged into main (2026-11-11, review: approve with nits; the nits went into the
044 round). AC1: verify.sh passed. Tests: sim 400/400 (baseline 356, +44), EditMode 1182/1182
(baseline 1172, +10).

**Design.**
- `sim/Runtime/Cooking/DonenessZones.cs` (+ `DonenessZone`): the zones in the sim. Perfect is the window's second half
  (`DonenessWindow.PerfectFrom`, its middle), as on the 027c ring. The rating's doneness term (`Factor`): 1 in Perfect,
  `GoodFactor` 0.9 in Good, and outside the window that value times the old 0.2 falloff. Untouched raw food with a window
  from 0 (lettuce, cucumber) stays at 1, so no salad, raw layer or old raw rating moves.
- **Zone buckets** (`HeatProfile.ZoneBuckets`, set on every default heat op): the "time" bucket (and the griddle's long
  side) is picked from the zone of what the op cooked, not from the seconds. Undercooked gives short, Good or Perfect gives
  medium, Overdone gives long. Per-ingredient curves (fries 25 s, falafel 20 s, seitan 12 s, veg 10 s) and the pan/pot
  dose (cut and heat tolerance) cannot share fixed second thresholds, so this is the only way names and buckets equal the
  zones everywhere. The bucket values stay in the catalog only to order the buckets.
- With zone buckets the stat changes (fat, crunch, umami, ...) **saturate at the Perfect start**, not the window end: the
  whole Perfect zone rates the same, best. The pan and pot saturate where every component reaches its Perfect half.
- An undercooked heat layer in a build is "prepared wrong" (`RecipeTemplates.Fits`), so it is one step short and the order is Close.
  Menu dishes get Close from the existing time-rank rule now that the bucket is "short".
- **Pace table**: `sim/Runtime/Cooking/CookingPace.cs` holds every curve. DeepFry, Griddle and Spit point at it, so their
  public names are unchanged. `PanPotSpeed` = 2 (inbox question 3, default A) multiplies the pan and pot dose. The table is in
  docs/cooking-roadmap.md ("Cooking pace"). The pan and pot rows are measured, not set.
- Game: `HeatZoneRules.Build` now measures `PerfectFrom` (every part in its Perfect half; `HeatTimeline` takes it as an
  optional sixth argument; by hand it is still the middle). For one food this equals the old middle. For a mix
  (flatbread + mushroom) it moves to where the sim rates best. The look on the heat (`KitchenState.CurrentBucket`) takes the
  sim's bucket of the take-now preview, not the seconds.

**Re-pinned (written reasons in each test).**
- The fries fixture is now 80 s at the 2x pan. That is Perfect, with the same hash 2ab1f8... and name.
- The pan presets are 20/80/200 s (were 40/150/400).
- The AC11 exact-stat vectors use half the time (same dose, same stats).
- The pre-031/pre-036 ratings re-pinned: 13 s a side is Good for seitan (0.9).
- The pre-031 saute (undercooked) is now "lightly sauteed potato" with a new hash.
- MenuKitchen exact dishes use Perfect times (fries 80, soups 420, apple 20).
- The FoodLook fried cube mix is 60 s (150 s is overdone now).

**042 review follow-ups** (all nine, inside game/ and specs/):
1. `CupTrayReviewTests.HasRestPrediction_OnlyWhilePredictingARest`.
2. The seen-through rule in `PickupInteractor.InSight` is limited to `StationKind.DrinkDispenser`.
3. `DrinkRules.LiddableTap` is the one rule. The server (`KitchenState.LiddableSpotCup` with a new `SpotByteOf`) and the
   client copy both call it; tested once.
4. `AimTarget.BusyOption`.
5. `CupSpotCheck` returns NothingHeld for empty hands.
6. `IsOnSpot` uses `DrinkDesk.StationOfKey`/`TapOfKey`.
7. `EnsureCupSpots` checks its cache before `RefreshInteractables()`.
8. The pick-up check is a pure `PickupService.PlanPickup` (TryPickup runs it). Tested: the veto gives Taken and nothing is
   claimed.
9. The 042 notes say Merged.

**Feels off / for the human.**
- Onion and tomato have the placeholder window 0..1. They are Good from the first second, so "lightly boiled onion" no
  longer exists.
- Sauteed apple is overdone after about 5 s at the 2x pan.
- Grilled seitan and mushroom rate low (taste, not doneness: best 0.41 and 0.36).
- `GoodFactor` 0.9 is a default to review.
- The ring's Perfect mark stays at the fixed 0.8 share. For a mix the measured Perfect can sit a little later than the mark.

**Visual check** (`.verify/visual043`, look/tutorial; cook re-run in `.verify/visual043b`): 0 exceptions.
- The first cook run had 22 NullReferenceExceptions inside the Input System package (`InputManagerStateMonitors`, mouse
  events) in the observer window only. The re-run was clean.
- Fryer: cell 74 shows "pale fries" while Cooking (20 s); cell 82 shows "golden fries" from Good (25 s).
- Griddle: cells 602 and 603 go from warm to half-grilled; the log reads "grilled" once both sides are Good.
- Spit: cell 720 is "pale shaved seitan", cell 721 "shaved seitan" at Good (40 s).

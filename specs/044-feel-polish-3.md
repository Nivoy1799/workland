---
id: 044
title: Feel polish 3 (the open "feels off" lists of specs 027b-038)
status: approved
owner: architect
builder: opus
---

# 044: Feel polish 3

## Goal
Work through the "feels off" and review follow-up lists at the end of docs/cooking-roadmap.md that no later spec
covers. The biggest items:
- other players see food fly into pots, and hear put-downs;
- buttons sink when pressed;
- sprint reaches full speed in 0.1–0.15 s;
- the scoop and the tip are predicted;
- the third-person carry and wipe poses;
- eat-in guests leave without carrying their plate (tables come later);
- the tutorial card no longer covers the view;
- the order bubbles are not hidden behind the ticket rail;
- the rail cards are readable from below.

Each item gets a test or a visual-check shot. Items already covered by 039–043 are skipped.

## Acceptance criteria
- [x] AC1: `verify.sh`.
- [x] AC2: Every list item is fixed, or moved with a reason.
- [ ] AC3: Visual check sheets for each item. Partly: see the visual check notes; four items are covered by tests only.
- [x] AC4: `WhatsNew.json`.

## Implementation notes
Built 2026-11-11 (builder, opus), on top of 043 (d014d5e). Not committed. Tests: sim 400/400 (unchanged), EditMode 1198/1198
(baseline 1182, +16).

**Design, per list item** (all in `game/`; one idea where it fits: the client already has the state, so the look comes from it).
- **Other players see food fly in** (027b; also 035 "the bin has no byte"). `Pickup` has two new SyncVars, `ExitPoint` and
  `ExitSound`. The server sets them right before a despawn into something, through
  `PickupService.TakeAway(player, out taken, exitTo, landing)`:
  - food into a pot, pan, basket or board (`vessel.ContentPoint`, PlaceFood);
  - scraps into the trash can (its opening, the lid's thump);
  - the bag into the bin (its opening, the rustle);
  - a dirty plate into the sink (the basin, the splash).

  FishNet writes pending SyncVars before the despawn message (`ServerObjects.FinalizeDespawn`), so they arrive first. In
  `Pickup.OnStopClient`, every viewer that did not predict the move (`ExitFlightRules.ShowsFlight`) draws `ExitFlightLook`. That is a
  copy of the model's renderers only (no colliders, scripts or labels; property blocks kept). It flies on an arc from the last drawn
  pose into the point in 0.24 s, shrinks to 0.7 and plays the landing sound there. `CleaningService.ExitPoint(station)` gives the
  same points the owner's look uses.
- **Other players' put-downs are heard.** `ItemLook` remembers who put an item down. `ItemFrame.LandedBy` is set on the one frame
  the settle onto the networked spot ends. `Pickup` plays the material's place tock there for everyone but that player, who heard it
  at the click. `KitchenSounds.PlayOnce` is a static entry point for views without a reference.
- **Buttons sink.** `ButtonPressRules` has the depth curve: 12 mm, 0.04 s down, 0.05 s held, 0.12 s up. It also has
  `Pressed(kind, option, before, after)`, which tells which button a change of the station's flags byte belongs to (the stove's
  switch or mode per burner, the fryer's power or basket, the griddle's power, the spit's knob). `StationButton.Awake` adds a
  `ButtonPressView` when graphics run. The presser sinks it at the click (`ShowPress` in `ExecuteAction`); everyone else sinks it
  when the flags change. Our own confirmation within 1 s does not sink it a second time. It moves the button's own child transform
  along the station's +z, never a networked root.
- **Sprint** (feel.md 5). `MovementRules.AccelerationTo(target)` is 19 m/s² up to walk speed. Above walk speed it is
  `target / StartSeconds` (0.13 s), so a sprint from standing takes 0.13 s (was 0.26 s). Deceleration is unchanged: a stop from a
  sprint still takes 0.2 s. The owner and the server run the same function.
- **Scoop and tip predicted.** `ContentsPrediction` (pure): a vessel draws predicted looks until its synced looks change (the
  answer) or the confirm timeout runs out. `Vessel.PredictContents` and `SeenLooks` use it.
  - Tip: holding a plate or bowl at a basket or a griddle spot, the plate shows its own looks plus the source's, and the source is
    empty.
  - Scoop: the tray loses its last portion (the one the server takes), and an `ExitFlightLook` copy of that portion flies into the
    hands.

  Both work in first person and on the third-person key.
- **Third-person carry and wipe poses.** `ArmPoseRules` (pure) and `CharacterVisual.ReachLeft` / `ReachRight`: after the animation,
  the upper arm turns so the hand points at a target, faded in like the hold layer.
  - A carried box: both hands on its sides instead of the one-hand hold.
  - A held cloth near a dispenser that is being wiped is drawn at that dispenser's cloth pose, going round
    (`SpillWipeView.TryClothFor`). The arm reaches for it, and the view stops drawing its own second cloth.
- **Eat-in guests leave their plate.** `HandoffRules.LeavesPlate`: after paying, an eat-in guest with a plate dish sends the plate
  on the hand-off arc onto the dish return, where it is gone, and the arm comes down. Take-away parcels and builds stay in the hand.
- **Tutorial card.** It is now compact (380 px, smaller type and buttons) in the top right corner, above the toast column. It was a
  540 px card at the top centre. The edge arrow is 60 px (was 46).
- **Order bubbles under the rail; the rail readable from below.** The rail is raised (centre 3.0 m, was 2.5) and tilted 20° down
  towards the kitchen. The card backs are URP Unlit cream (`CreateUnlitMaterial`). `TicketRailSceneTests` checks the lines from
  the eye to the queue head's bubble (2.1–2.4 m high, 0.6 m wide). They come from first-person eyes at the counter, in the aisle and
  at the back line, and from the third-person camera, and must all pass under the rail's bounds.

**Also fixed from the lists.**
- 027b: the HUD prompt lagged the aim by one frame. `HudView` now runs after the interactor (`[DefaultExecutionOrder(100)]`).
- 027b: the outline was thick on big stations. `OutlineMinScale` went from 0.03 to 0.008, about 1.2 cm on a stove.
- 042 known limit: a refused tap press now says "Put a cup on the drip tray first".

**Moved, with the reason.**
- 027 and 027a review items (visual-check.sh, FloorPlanTests end to end, TourRules constants): tooling, for 045 or 049.
- Netcode list (TooFast disconnect, late refusals, RTT, MTU): spec 045.
- 028 and 028b review nits (content audit, the StockTake confirm): sim and catalog work, not feel. They need their own small round.
- 030: the warning lead time, rings only for the aimed spot, grill marks on plated items, the griddle-to-plates distance and
  per-dish prices. The ring and label rules overlap with 040 (readable at a glance); the prices go to 037.
- 032:
  - the bills go to 037;
  - "changed their mind", and calm guests leaving on the 014 reconcile, need a human decision (economy);
  - third person can only pay by card (register UI, not in this list);
  - the flaky look-tour exit 139 goes to 045.
- 034: the terminal rows are small and dim, counts show only in prompts, and the sauce bottle is not visible. All of this is 040
  (readable at a glance). The delivery knock replaying for late joiners and the old wallet paths go to 045 (restart and join paths).
- 035: the trash bag copy flies in as a box. The take-out copies the can's slot model; it needs a bag model in the can (040 art).
- 036:
  - An open wrap with fillings cannot be picked up to scoop onto. That is the 031 fold-on-click rule, a design call for the human.
  - The first-person knife saws in the corner: a view pose, for the next feel round.
  - The graybox cone and tray labels: 040.
- 038: the serve and pay steps often pass together with the fries guest. That is tutorial flow and needs the 046 day rules. The
  scene plates reset goes to 045.
- 041 review leftovers (WheelSteps scrollDeltaBehavior, dead ControlRules checks, the right-click ray through station bodies, the
  turned-item overlap, the shared hit sorting): input code, not in this list. They fit a 041b nit round.
- 042 known limits: the tour prompt during our own pour, and third-person E taking an empty cup back off the tray. These go to a
  drinks UX round.

**043 review follow-ups** (orchestrator, in this round).
- 3: `CookingTime.BucketId` is marked legacy in its doc comment (only tests call it). It is kept, because its tests stay.
- 6: `WhatsNewTests` has a `Shipped(id, date, ac)` helper. It checks that an entry exists, has its date and at least 3 test steps,
  and is never newer than the newest. One `ShippedFile_TheNewestEntry_IsTheLastInTheFile` test replaces the per-spec "is the
  newest" and "X is newer" checks for 042 and 043, so the next spec only appends its own `Shipped` test.
- 7: specs/043 says "Merged", AC1 is ticked, and verify.sh is recorded as passed.
- Not done, outside 044's folders (`sim/`): 1 (the AssemblyTests comments at 322 and 338–342), 2 (`DonenessZones.IsUndercooked`
  checking `ZoneBuckets`), 4 (the pot in the name sweep) and 5 (a griddle case with unequal sides). They live in `sim/Sim.Tests~`
  and `sim/Runtime`.

**Scope.**
- `Scripts/Dev/TourRunner.cs`: the observer films the first 3 remote exit flights close up ("remote-flight", 8 frames 0.04 s apart,
  through the `ExitFlightLook.Started` event). The tours already click through `DevClick`, so the other looks show in their
  existing frames.
- `Editor/GrayboxSceneBuilder.cs` changes only the rail: position, tilt and the unlit card material.
- Re-saved materials and prefabs from the scene rebuild are reverted. `TicketCard.mat` is kept (it is now unlit).

**Feels off / for the human.**
- The arm reach turns only the upper arm (no elbow bend), so on a long reach the arm is straight.
- The cloth jumps from the hand to the puddle when a wipe starts (same goal kind, so no tween).
- A refused tip or scoop goes back only after the confirm timeout. The refusal answer does not end the look.
- Unpacking a whole box (`SupplyService`) still vanishes for onlookers: it has no exit point yet.
- The serve hand-over (plate to guest) is still the 032 `GuestHandoffView` arc from the counter, not from the cook's hand.

**Visual check.** `.verify/visual044b` (look, cook, tutorial; the final build) passed with 0 exceptions, 0 timeouts and 0 aim
fallbacks. `.verify/visual044` was the same run before the observer's flight frames.
- Remote flight: in `cook-observer/sheet.png`, cells 13–20 show the potato sticks leaving the cook's hand and landing in the fryer
  basket (14 mid-air, 17 in the basket, the arm down). Cells 04–11 and 56–63 show other flights. `tutorial-observer` cells 03–10
  show the same.
- Rail and bubbles: in `cook/sheet.png`, cell 310 (orders-rail) shows the rail higher and tilted, with bright cream cards. The queue
  head's bubble "Grilled veggies · for here" reads below it; in 043's 310 no bubble showed.
- Tip: in `cook/sheet.png`, cell 111 (fries-tipped-onto-plate-01, the click's frame) shows the fries box already on the held plate and
  the basket empty, while the label still reads "golden fries" (the synced text comes a round trip later).
- Scoop: `cook/sheet.png` cells 750–752.
- Tutorial card: in `tutorial/sheet.png`, cell 02 shows the compact card in the top right, the view's centre clear, and the 60 px
  arrow.
- Not in any sheet (tests only): the button sink (12 mm, too small at the shot distances), the remote put-down tock (sound), the
  sprint start, the third-person box and wipe arms (the tours play in first person, and the observer camera does not see the stock
  run or the wipe), and the eat-in plate onto the dish return.

## Follow-ups (2026-11-11)
Branch `spec/followups-041-042-044` (builder, opus), from "Feels off / for the human" above. One verify for the 041, 042 and 044
follow-ups.
- **A refused tip or scoop goes back at once.** `PredictTipOrScoop` remembers its request id (spec 045) and its vessels. In
  `ReportCook`, the refusal of that request (`ContentsPrediction.RefusalEnds`) ends the look at once
  (`ContentsPrediction.Refused`, `Vessel.EndPredictedContents`). A yes is still left to the synced contents, and the timeout stays
  for a lost answer. The scoop's flying copy has already landed by then and is not undone.
- **The cloth eases to the spill.** `Pickup` blends the held cloth between the hand pose and `SpillWipeView`'s cloth pose over
  `CleaningArtRules.ClothBlendSeconds` (0.15 s, ease-out, `ClothShare`). It blends back the same way when the wipe ends. A wipe that
  starts or ends mid-blend starts from where the cloth is. A new holder starts in the hand. The arm reaches for the cloth during
  the blend too (the reach fades in by itself).
- **An emptied box flies into its slot for onlookers.** The server path allows it with no new RPC or SyncVar.
  `SupplyService.Unpack` takes the slot's anchor (`KitchenService.UnpackInto` passes `target.Anchor`) and calls
  `PickupService.TakeAway(player, out _, anchor, SoundKey.Unpack)` before the despawn. That is the 044 exit point, so every
  onlooker sees the box fly into the slot and hears the unpack there (before, only the unpacker heard it). The unpacker drew its
  own tip already (`UnpackLook`, the box hidden), so `SupplyBox.HiddenForUnpack` counts as its prediction in
  `Pickup.OnStopClient` (no second flight). "Back to the shelf" is the slot the stock went into. A bin flight would need a
  second point, so it was not done.
- **Skipped (needs a new RPC or SyncVar):** none of the three items. Item 042-7's late start would need one (see spec 042).
- Tests: new `FeelPolish3FollowUpTests` (3; the box test covers the flight rule only, the server path needs the network).
- **Runs (041, 042 and 044 follow-ups together).** `verify.sh` PASS: EditMode 1382/1382 (baseline 1373, +9), sim 403/403, gateway
  175/175. `RpcSurfaceTests` and `MoveDataSurfaceTests` are unchanged and green. `BuildAll` succeeded (both builds).
  `visual-check.sh --tours look,cook,tutorial,menu --out .verify/visual-fu3`: PASS, with 0 exceptions, 0 timeouts, 0 aim
  fallbacks and 0 native crashes. Look: 133 images. Cook: 975. Tutorial: 723 ("[Tutorial] finished"). Menu: 28. Cells:
  - look `61-rightclick-front-west` and `64-rightclick-beside-drinks`: the right-click ghost is still on the counter tops next
    to the dispenser, so the station block does not catch them;
  - cook `452-cup-place-09`: the cup on the tray;
  - cook `111-fries-tipped-onto-plate-01`: the tip;
  - cook `193-202 unpack`;
  - cook `292-298 wipe`.
  The observers do not film the unpack or the wipe, so the remote box flight and the cloth blend are covered by tests only.

## Follow-ups (2026-11-11 evening)
Branch `spec/followups-053-044-048` (builder), from the known limit "the arm reach turns only the upper arm (no elbow bend)". One
verify for the 053, 044 and 048 follow-ups.
- **A two-bone bend.** `ArmPoseRules.Elbow(shoulder, target, upperLength, lowerLength, bendHint)` (pure) puts the elbow where the
  shoulder, the target and the two bone lengths make a triangle, bent towards the hint; out of reach the arm is straight at the target.
  `ArmPoseRules.BendHint(yaw, left)`: down, out to the arm's side and a little back. `ArmPoseRules.TwoBone` turns that into two world
  turns (the upper arm, then the forearm) faded by the reach weight. `CharacterVisual.BendArm` applies them in `LateUpdate`, after
  the animation, with the bone lengths taken from the animated pose; a rig without a lower-arm bone keeps the old shoulder turn.
- No per-frame allocations (structs only). Tests: `FollowUpsEveningTests.Elbow_...`, `TwoBone_...` and `BendArm_OnRealBones_...`
  (three transforms: the hand lands on the target, the bones keep their lengths, 0 bytes allocated over 500 calls).
- The third-person box carry and wipe are not filmed by a tour (as before), so the bend is covered by tests only. Runs: see spec
  053, "Follow-ups (2026-11-11 evening)" (look and cook tours ok, 0 exceptions).

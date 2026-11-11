# Cooking roadmap ("Cooking 2.0")

Goal set by the human on 2026-11-08: cooking that can stand next to the best fast-food and kebab-shop
simulators. Today it covers about 5 % of what those games do. This roadmap lists the genre mechanics we
need, in our own way: an original, vegan (D27) street-food shop inside a multiplayer town. We take
**mechanics** from the genre, never names, art, levels or recipes of an existing game.

## What makes the genre fun (pillars)
1. **Orders you can read.** A customer asks for a specific build ("wrap, extra garlic sauce, no onion"),
   has visible patience, pays at the register, tips for speed and accuracy.
2. **Stations with timing.** Fryer, griddle, spit, oven: each has a doneness window you can see and hear
   (raw, cooked, perfect, burnt) and a hands-on action (shake the basket, flip, shave, toast).
3. **Assembly in order.** Bread, then filling, salad, sauce, then wrap or box. The build stacks visibly;
   wrong order or missing parts lowers the score.
4. **Prep and stock.** Chop in bulk into containers that hold portions; containers run empty; stock is
   bought, delivered in boxes and unpacked onto shelves; money goes out as well as in.
5. **A clean shop.** Dirty trays, trash, spills; a hygiene score customers notice.
6. **Running a business.** Open and close, set prices, buy equipment, unlock menu items, grow a rating.
7. **Feel.** Sizzle, steam, crisp UI, clear highlights, short onboarding for the first shift.
8. **Our own edge.** Co-op in one kitchen (roles, shared ticket rail) and the town economy around the shop
   (suppliers and other professions, docs/economy-and-professions.md).

## Menu direction (vegan street food)
Signature: a **seitan spit** (vertical rotisserie, shaved into a flatbread or wrap), **falafel** from the
fryer, **fries**, **grilled vegetables and seitan strips**, salads, two or three vegan sauces (garlic-herb,
chili, tahini), drinks (lemonade, iced tea, soda). Existing dishes (soups, salad, fried apple) stay as sides.

## Spec queue (in order)
| Spec | Title | Pillar |
|---|---|---|
| 026 | Scale and palette pass (in progress) | look |
| 027 | Visual check tool: scripted tour and walking frames without window focus | quality |
| 027a | **Shop floor plan**: counter between kitchen and guests, guests queue on the street side, all controls face the cook, work triangle (docs/feel.md 6) | feel |
| 027b | **Feel pass 1**: predicted pickup and place visuals, arcs instead of teleports, forgiving sticky aim, ghost preview, click and tock sounds, movement and camera numbers, F2 tuning panel with A/B presets (docs/feel.md 1-5, 8, 9) | feel |
| 027c | **Feel pass 2, cooking**: progress rings, sound by heat, perfect and burn cues, serve reward (docs/feel.md 7) | feel |
| 027d | **World labels and customer looks**: small station signs instead of big floating names, order bubbles that never overlap, item status as icons, more customer variety (skins and tints) | feel, look |

Findings from the first visual check (spec 027 sheets, 2026-11-09) that go into 027b: aim prefers the pan over the
free spot next to it; the remote cook keeps a stride pose while standing and turning; the third-person camera
sits too far back and high.
| 028 | Street-food ingredients and content audit (flatbread, wrap, seitan, falafel mix, lettuce, cucumber, sauces; rename `cheese`; forbidden-word test) | 1, D27 |
| 029 | Deep fryer: basket, oil, timer and browning, shake, drain, burn (fries, falafel) | 2 |
| 030 | Griddle with flipping: two-sided doneness (seitan strips, vegetables, flatbread toasting) | 2 |
| 031 | Assembly station and packaging: ordered builds, visual stack, wrap paper, box, tray | 3 |
| 032 | Orders 2.0: build orders with modifiers, patience bars, register (cash with change or card), tips, reviews | 1 |
| 033 | Drinks: cups, dispenser with fill timing, lids | 1, 3 |
| 034 | Stock and supply: portion containers, fridge and shelves, ordering terminal, delivery boxes, costs | 4 |
| 035 | Cleaning: trays and plates come back dirty, sink, trash, spills, hygiene score | 5 |
| 036 | Seitan spit: rotating, outer layer cooks, shave slices with the knife | 2, signature |
| 037 | Business: price menu, equipment shop and upgrades, shop rating unlocks items | 6 |
| 038 | First-shift onboarding (guided tutorial) - built before 037, which waits for the price-scale answer | 7 |

**Feel comes before features** (human, 2026-11-08: "most important it should feel good"). 027a–027c run
before any new station, and every later spec has docs/feel.md acceptance criteria of its own.

Each spec follows docs/art-direction.md (sizes, palette, measured animation), docs/feel.md, and the full pipeline:
builder, tester, reviewer, visual check, verify.sh. Numbers such as prices, timings and patience are
**provisional defaults** chosen by the orchestrator, listed in each spec under "Defaults to review", and
tuned later by the human.

## Cooking pace (spec 043: one table for every station)
Real (kitchen) seconds on the heat, at the inbox's "about 2x faster" default (question 3, option A). The numbers live in
one code file, `sim/Runtime/Cooking/CookingPace.cs`; the pan and pot rows follow from their dose formula at
`CookingPace.PanPotSpeed` (2x) and the item's cut, so they are measured, not set. Zones: **Good** from the window's start,
**Perfect** from its middle to its end (the best rating: doneness term 1, Good 0.9), **Overdone** after it, **Burnt** 0.1
doneness later. Names and menu buckets follow the zones: undercooked is pale / lightly / half-grilled (an order only
Close), Good and Perfect are golden / grilled / the plain cooked name, Overdone is dark / charred / soft.

| Station | Food | Good | Perfect | Overdone | Burnt | Before 043 (Good / Perfect / Burnt) |
|---|---|---|---|---|---|---|
| Pan (190 C) | whole potato | 22 | 28 | 34 | 38 | 44 / 55 / 75 |
| Pan | potato sticks | 10 | 13 | 15 | 17 | 20 / 25 / 34 |
| Pan, saute | apple slices | 1.7 (the pan's first moment) | 2.5 | 4.9 | 6.5 | 2 / 5 / 13 |
| Pot (100 C) | whole potato | 71 | 90 | 109 | 122 | 141 / 180 / 240 |
| Pot | diced potato | 27 | 35 | 42 | 47 | 54 / 69 / 94 |
| Pot | onion, tomato (window 0..1) | 5 | 60 | never | never | 5 / 122 / never |
| Fryer (180 C) | fries | 25 | 30 | 35 | 45 | unchanged |
| Fryer | falafel | 20 | 25 | 30 | 38 | unchanged |
| Griddle, per side | seitan strips | 12 | 14 | 16 | 22 | unchanged |
| Griddle, per side | paprika, eggplant, mushroom | 10 | 12 | 14 | 20 | unchanged |
| Griddle, per side | flatbread, wrap (toast) | 4 | 5 | 6 | 9 | unchanged |
| Spit (high heat) | seitan outer layer | 40 | 55 | 70 | 90 | unchanged |

Before 043 the best rating came at the end of the window (Overdone column), the names started earlier than Good on the
pan, pot and griddle (griddle "grilled" from 9 s, pan "fried" from 10 s), and the pan and pot ran at half this pace.

## Follow-ups from reviews (small, do when touching the area)
- 027: visual-check.sh trap should `exit 130`; derive the WSL path with `wslpath`; tour args parse in release builds.
- 027a: FloorPlanTests AC4 drives CustomerQueue/OrderRules directly, not CustomerService serving and Customer
  movement (make it end to end); TourRules duplicates layout constants (take them from the scene or builder).
- 027b (still feels off): other players see food vanish from the cook's hand before it drops into a pot (no flight,
  despawn has no destination); other players' put-downs are silent; pressed buttons click but do not sink; the HUD
  prompt lags the aim by one frame; sprint from standing takes 0.26 s (feel.md wants 0.1-0.15 s); the outline is
  thick on big stations.
- 027b review (2026-11-09, CHANGES REQUESTED): fix before merging 027b, as one fix round on top of 027c:
  (1) client click interval >= server interval + one tick (also the knife stroke); (2) match server answers to the
  latest request only; (3) confirm timeout scales with round-trip time; (4) never move a networked root (childless
  items); (5) clear stale settle state; (6) superseded pickup must not bounce; (7) third-person E through the
  ClickBuffer; (8) drop a queued click when the target or action changed; (9) put-into confirms only for our own
  item; (10) cache Contents/children, no Find* or allocations per frame; (11) end predictions in OnStopClient;
  (12) extract the prediction state machine into a pure tested class, remove the unused direct-hit branch, test the
  legs layer weight outside idle; nits: legs layer during the idle-to-walk fade, AvatarMask lifetime.
- 027c + 027b-fix review (2026-11-09, CHANGES REQUESTED): must-fix: server rate limit as a token bucket (burst 2,
  one token per 0.2 s) so bunched requests are not dropped and PredictionBook stays in step; test: a drop inside a
  burst, then a refusal of n+1 must not deny n. Also: queued click compares the action too; cache the heat timeline
  per (item hash, op, temperature); surface test for ServeFeed/ServeReward; reset ServeRewardView `_seen` on
  despawn; destroy the HeatRingView material; HeatRingView rig lookup and Contents via Vessel.ContentsAnchor;
  cached _BaseColor id; coin after the distance check; no closure per click. Later: RTT 0 before the first ping;
  tests that copy glue from PickupInteractor/Pickup.
- Netcode list (spec 004 load test and later): disconnect a client after many TooFast replies in a row; a late
  refusal after a server hitch over 1 s can bounce one prediction (synced state corrects it); with RTT over about
  0.24 s two requests are usually in flight, so refusals wait for the full timeout; WSL MTU 1280 path (Tugboat).
- 028 review (approved with nits): ContentRules meta-test should plant a word per source (menu, WhatsNew, grocer),
  not only test the matcher; scan name catalogs once a production one exists; adjective forms per the inbox answer;
  rename `StreetFoodItems.CutShape` to `PreferredCutShape`; grocer lists `cashew` with no kitchen item (check both
  directions or drop it); real shred and strip cut shapes need an operation-catalog change.
- 028b review (approved with nits) -> polish round after 029: a Take is confirmed by any item in the hands (snapshot
  the held id, confirm only on new food); a pending take makes Place/PutInto/Sell clickable (map to None while
  pending); KitchenService.TakeIngredient can charge without an item if the spawn fails (refund or check first);
  test the TakeIngredient sequence (TryBuy 0 times on refusal); StockTakeLook liveness when the interactor dies;
  nits (NearestSlot test point, real Finish path, measured log, crate variable names, readonly row arrays).
- 029b review (approved with nits; must-fix test and label decision moved into 030): stock take after Pay can still
  fail on pool or spawn errors (needs an Economy refund backstop, track for 034); the cook tour should press the basket
  button through DevClick (end-to-end intent); a dispatch test for tip vs pour and VesselFull on tip; TipBasket log.
- 030 (feels off) -> polish round 2 (before or with 032): warning lead time = clamp(0.4 x good window, 1 s, 3 s)
  (orchestrator decision; toast warned before Good); first person shows 6 rings and 6 labels at once (show rings and
  labels only for the aimed spot plus spots in Warning or worse); grill marks stay on the plated item; a spatula motion
  in the hand on flip; griddle to plates 4.2 m; per-dish menu prices (spec 037).
- 030 review (approved after the CombineSides fix, which went into the 031 round): tuning for the human - the "grilled"
  name and the menu's medium bucket start at 9 s per side, before the good window (12 s seitan, 10 s veg), so an
  undercooked plate can be an exact order (fixed in 043: buckets follow the zones); GriddleSides is sim-model only (the
  server keeps its own sessions).
- 031b review (approved with nits): third-person E squeeze on tap (fixed in 032 round: 0.2 s hold), free busy
  dispenser, tests and nits (in 032 round). Follow-up: no third-person tour shot with food on the heat.
- 032 (feels off): bills from market values are huge (inbox question 7, spec 037 rescale); "changed their mind"
  (spec 014 demand reconcile) is now the top miss reason and may be redundant with patience; order bubbles behind the
  ticket rail from the kitchen; rail cards dim from below; third person can only take card payments; one look-tour
  run exited 139 during FishNet shutdown after its sheet (flaky, watch).
- 032 review (CHANGES REQUESTED): double pay on hand-over failure (must-fix) and register decisions (any cook works the
  head payment; self-settled payments pay no tip; head untouched 30 s auto-settles) in the 032 fix round. Process:
  future rounds commit spec and review fixes as separate commits.
- 033 review (CHANGES REQUESTED, fixed in the 034 round): plate op added "cup" to every plated dish; cups into an
  empty bowl server-side; pour hardening; tab close order; GuestTab.Serve guard. Later: a drink line when the drink
  market has no open demand; near-empty cups profitable to NPC demand (037 pricing).
- 034 (feels off): terminal rows small and buttons dim; taps, rack and cup stack show counts only in prompts; a
  sauce dispenser's open bottle is not visible to the client; third-person carried box has no arm pose; the delivery
  knock replays for late joiners; packs are expensive next to the start money (037); old money-only wallet paths can
  be removed once every scene has a SupplyService.
- 034 review (CHANGES REQUESTED; fixed in the 035 round): delivery boxes stacked on one spot (physics sync), last
  drops, stock reset vs economy, unstocked goods free, lost boxes, pour key leftover, refund bound, tests.
- 035 (feels off): hygiene rates far too harsh (one spill 100 -> 49 % in 40 s; decision: all decay and recovery 10x
  slower, in the 036 round); eat-in guests leave holding their plate (no seats yet); the trash bag copy flies in as a
  box; third-person wipe cloth in the hand does not move; the bin has no byte (onlookers see the bag vanish).
- 035 review (CHANGES REQUESTED; fixed in the 036 round): plate invariant on a server stop, dead ForgetBox, CanDraw
  minimum share, failed refunds keep the order, GrocerPaid in the snapshot, scrub auto-stop, full bags only, tests.
  Note for the human (minor): the HUD shows an exact hygiene % (the spec asked for it); effects switch at 50/30.
- 036 (feels off): best quality comes at the end of the good window (70 s on the spit; fryer and griddle the same),
  not at the "perfect" cue - a later spec should make "perfect" the best rating everywhere (done in 043); the scoop is not
  predicted; an open wrap with fillings can't be picked up to scoop onto (the 031 fold-on-click rule); the first-
  person knife saws in the corner; graybox cone and tray labels.
- 036 review (CHANGES REQUESTED; fixed in the 038 round): cones could not be ordered (no grocer price for
  seitan_cone), cone lost on a server stop, free cones without supply, plate restart doc, empty-tap text, tests.
- 038 (feels off): serve and pay steps often pass together with the fries guest; the card covers the top of the view;
  step 4 points at the board while the knife is still held (text should say "put the knife down"); calm guests still
  leave on the 014 demand reconcile; the edge arrow is small; scene plates are not reset to their start spots on a
  server restart (036 review "better still").
- 038 review (approved with nits) -> 038b: visual-check marks the tutorial done in the dev machine's PlayerPrefs;
  calm-guests flag can go stale within one session; the engine should tie PriceGoodId to GoodId; a tap pour larger
  than what is left is refused whole; a returned cone can exceed capacity; two repeated lookups; sim test for
  PriceGoodId; `string?` nullable.
- 038b review (approved with nits): the calm-guests MonoBehaviour wiring is untested (only pure parts); two error paths
  after a cut pour; the engine trusts offer fields (items per unit, pack size); plates spawned within 1 s are not seen by
  the tutorial target; an unused using.
- 039 review (CHANGES REQUESTED, fixed in the 041 round): empty-handed aim through thin strips; coverage test blind
  spots. Later: the prediction can be one 45-degree snap off near a boundary; the fries box depth is defined twice.
- 041 review (approved; 1, 2, 5 and 9 go into the 042 round): empty-hands tests copy the hit sorting (make it a
  shared pure helper); the right-click ray sees through station bodies; WheelSteps should read scrollDeltaBehavior;
  dead checks in ControlRules routing; a turned item can overlap neighbours visually; stale spec line.
- Flaky: Cinemachine NRE in one cook run (destroyed tracked transform at teardown) and a host that ended mid-tour
  without a log line - both for spec 045.
- 042 review (approved; nits 1-9 go into the 043 round). Known limits: a cup sits 2 cm into the tray grid; other players
  see the level about one round trip late; in third person E cannot take an empty cup back off the tray; HudView still
  says "Take a cup first" on a refused press (needs a Scripts/UI scope); the tour prompt reads "pick up empty cup"
  during our own pour.
- 043 review (approved; must-fix comment and nits 3-7 in the 044 round): barely overdone food (up to Max + 0.02) still
  rates about 0.95; griddle stars use the mean of both sides; sauteed apple Perfect window 2.5-4.9 s at the 2x pan;
  onion and tomato placeholder window 0..1; seitan Perfect is only 2 s a side. Human question 16.
- 044 review (approved; 1-6 go into the 045 round where in scope): sprint turns and reversals also got twice as sharp
  (0.25 s, 25 m/s2); a vessel look change mid-prediction blinks predicted-away food back for one round trip; a button
  sinks on any flag change, also server-driven ones. Still open from 043: sim follow-ups 1, 2, 4, 5 (comment, IsUndercooked
  check, pot name sweep, unequal griddle sides) need a sim round.
- 045 review (changes required, fixed in the 047 round): SpamGuard is now a 10 s window (kick at 300); load-test verdict
  "at the limit, no headroom" (payload only, worst second 53 KB/s). Next step for bandwidth: per-packet stats, 15 Hz remotes.
- 047 review (changes required, fixed): plain-text names, the held-item return checks the place veto, SteamID64 check.
  Open: confirm before joining a friend while hosting; Alt-F4 while hosting sends no notice; friends-list text needs
  steam_display plus a localization file; no ban list after a kick.
- **Host network freeze (seen twice: visual039b, visual047c, both cook tours):** the host's FishNet tick stops (no
  more [Kitchen] lines, clients time out and cannot rejoin), the main thread keeps drawing, then a native crash. 045
  traced one case to an unhandled exception on a FishNet background thread (TimeManager.LocalTick). A clean re-run
  passed. For spec 049: log unhandled thread exceptions, find the trigger, and make the host survive or close cleanly.
- 048 review (changes requested, fixes in the 049 round): prompts follow rebinds; key capture no longer leaks into the
  game; own URP asset copy; settings schema version. Open: Esc frees the cursor; the Controls page scrolls at 720p; no
  tour shot of gamepad capture, a resolution change or the colour-blind rings while cooking. Human question 19.
- 049 review (changes required): the perf-check regression baseline mixes the best value of each metric across runs and
  shrinks the growth bounds to the noise floor (fix: one best clean row; growth only against the bounds); the watcher can
  hang if winpid is unreadable; the port check looks only for Worktown.exe; the HostStalled notice to clients is untested
  (add a bot to the stall test). C# fixes: stall close wrapped, crash-log caps, profile paths scrubbed, more scripted flags.
- 049b host freeze investigation: in both cases the host PROCESS died (a native access violation on the main thread,
  a jump through a bad code pointer from JIT code), so the tick watchdog cannot catch it. No reproduction in 4 cook
  tours. Suspects: Unity/Mono, the Steam overlay DLL in the host, or the GPU driver. Next: visual-check.sh now keeps
  native crash reports; on the next one take a full dump; try a cook run with the Steam overlay off; consider telling
  clients sooner than FishNet's 60 s timeout that the host is gone (a netcode spec).
- Follow-up round 2026-11-11 (042, 043, 048; approved with nits): ScrollOverflow should be dev-only; the menu tour's
  720p scroll check passes trivially at other sizes and has no slack; the Esc re-lock order is argued, not tested; the
  cook tour calls the station key directly, not through GameInput.
- 053/054 (merged 2026-11-11): the shop ledger shows a negative balance as $0 (display only); remote jitter p95 0.42
  stays over the 0.15 guard in every variant (likely the bots' uneven motion); the closed-station covers look rough;
  stage 1 pace depends on the potato slot for crews (question 24).

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
| 038 | First-shift onboarding (guided tutorial) | 7 |

**Feel comes before features** (human, 2026-11-08: "most important it should feel good"). 027a–027c run
before any new station, and every later spec has docs/feel.md acceptance criteria of its own.

Each spec follows docs/art-direction.md (sizes, palette, measured animation), docs/feel.md, and the full pipeline:
builder, tester, reviewer, visual check, verify.sh. Numbers such as prices, timings and patience are
**provisional defaults** chosen by the orchestrator, listed in each spec under "Defaults to review", and
tuned later by the human.

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

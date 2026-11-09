# Inbox for the human

Hop in whenever you like. Answer by writing under a question (or just tell Claude in chat). Claude never waits:
every question has a default, and work continues on other items until you answer. Newest first.

## Status (updated 2026-11-09 05:00)
- **Running:** spec 029 (deep fryer: baskets, lower, shake, lift, drain; fries and falafel).
- **Next:** fryer (029), griddle (030), assembly and packaging (031), orders 2.0 (032). See docs/cooking-roadmap.md.
- **Feel standard** (what "feels good" means, in measurable rules): docs/feel.md.
- **Merged since you left:**
  - **Spec 028b, compact stock:** a fridge (vegetables, seitan, falafel mix, lemon) and a dry shelf (sauces,
    potato, onion, flatbread, wraps) right next to the cutting board; aim at an item and click, it flies into your
    hand at once. Both are simple graybox models for now (no fridge in our Kenney furniture).
  - **Spec 028, street-food ingredients:** flatbread, wrap, seitan, falafel mix, lettuce, cucumber, red cabbage,
    lemon, garlic-herb and chili sauce (buy, hold, cut, cook); "cheese" removed (the grocer sells cashews); a test
    blocks animal-product words in every catalog, menu and news text.
  - **Specs 027b, 027c, 027d, the feel work** (three review rounds, 824 tests):
    - **instant feedback:** items fly to your hand or spot the moment you click, the server confirms in 40-100 ms,
      a refusal flies back with a "nope"; quick double clicks are never lost;
    - **no teleports:** arcs, settles, drops into pots, a ghost preview where things land, sounds for every action;
    - **forgiving aim** that sticks to the target and prefers what fits your held item; closer third-person camera;
    - **standing people** no longer stand in a stride (the library's idle clip was the cause);
    - **cooking reads at a glance:** a ring over the pan or pot fills up (cream, mustard at perfect, red when
      overdone), the food browns, sizzle follows the heat, cues at perfect and 3 s before burning, smoke when burnt,
      and serving pops stars and the tip;
    - **labels:** small signs on the station fronts, no text through walls, no mirrored or overlapping text;
      6 different customer looks.
  - **Spec 027a, shop floor plan:** back line (crates, board, two stoves, plates), a 1.5 m cook aisle (two cooks
    can pass; player body radius now 0.3 m), a slim 0.7 m front counter, guests queue on the street side facing
    the cook, back and side walls with a staff gap. **Your "buttons on the back" was real:** the stove and
    cabinet models themselves were turned towards the wall; now every knob and door faces the cook.
  - **Spec 027, visual check tool:** `bash scripts/visual-check.sh --tours look,walk,cook` makes contact sheets
    (frames every 0.1 s, walking seen from the side, the kitchen, the cook demo) without window focus. Its first run
    found 9 problems; each one is now in a roadmap spec.
  - **Spec 026, sizes and palette:**
    - every model is at real size (person 1.75 m, worktops 0.90 m, counter 1.05 m, food 1.5x for readability);
    - houses are moved so they don't overlap;
    - worktops are built from 0.6 m cabinet modules;
    - the warm palette is used everywhere, with tests that block stray sizes and colours;
    - the review found and fixed an awning stuck inside the shop and a lamp in a house.
    - Play-test (AC7) is open for you.
  - Clean-up: old Mini Characters and unused Kenney clips removed.

## Open questions
5. **Vegan word check: plant products.** The new content check (D27) blocks words like milk, butter, cream and cheese
   anywhere in item, menu and news text. That also blocks honest plant products ("oat milk", "coconut milk",
   "peanut butter", "cashew cream"), and it does not catch adjectives ("creamy", "cheesy", "buttery").
   Option A: allow explicit plant compounds (oat/soy/coconut/almond milk, peanut/cashew butter, cashew cream) through
   an allow-list, and also block the adjective forms. Option B: block every such word, plant or not (the world simply
   has its own names for these things). **Default: B** (strictest, fits "native vegan" and the own-history rule;
   easy to relax later). Blocks nothing.
3. **Cooking pace.** Measured with the new rings: a potato in the **pan** is good after 44 s, perfect at 55 s and
   burnt at 75 s. In the **pot** it is good after 2 min 21 s, perfect at 3 min, burnt at 4 min. Shop games usually
   aim at 20–60 s per item. Option A: speed everything up about 2x (pan good at ~22 s, pot at ~70 s). Option B:
   keep it. Option C: tell Claude your target, for example "pan 20 s, pot 45 s". **Default: A**, done in the fryer
   or griddle spec (028+), when timings are tuned anyway. Blocks nothing.
4. **FYI, no answer needed:** in development builds (the ones you play) **F2** now opens the feel panel
   (Preset A / B), and the What's-new banner moved to **Shift+F2**.
2. **D21 extension for cooking feedback (spec 027c).** D21 says clients only see food looks, names and stars.
   The cooking feedback needs a little more, all computed by the server:
   - per pot or pan: how far along it is (0-255) and its zone (raw, cooking, good, perfect, overdone, burnt),
     plus a warning flag that turns on 3 s before overdone;
   - after a serve: stars, amount paid and the **tip** (the tip reveals how big the pay share is).
   No recipe stats are revealed. Option A: allow all of it. Option B: allow the rings but hide the tip.
   Option C: keep D21 strict (rings would have to guess on the client). **Default: A.** Blocks nothing; easy to revert.
1. **WSL test server: network MTU is 1280 again** (Tailscale). Clients connect to the WSL dedicated server but
   nothing spawns (the spec 003 issue: FishNet's Tugboat sends packets up to 1350 bytes that must not be split).
   Option A: you run `sudo ip link set dev eth0 mtu 1500` in WSL (or turn Tailscale off) when you want WSL tests.
   Option B: nothing. **Default: B**, Claude tests with a local Windows host instead. Blocks nothing.
   Real risk for players on VPN paths with the Tugboat transport; it goes on the netcode list (spec 004 load test).

## Things to try when you have 5 minutes
- After 027b lands: open the game, press **F2**, switch between **Preset A** and **Preset B** while walking and
  picking things up, and write here which one feels better (or just "A" / "B"). No explanation needed.

## Answered
- 2026-11-08: 24 h autonomy granted; CC0 downloads from Kenney/Quaternius OK; old Mini Characters may be deleted;
  feel comes first ("counter on the wrong side, buttons on the back, everything feels off").

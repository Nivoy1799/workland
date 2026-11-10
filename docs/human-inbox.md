# Inbox for the human

Hop in whenever you like. Answer by writing under a question (or just tell Claude in chat). Claude never waits:
every question has a default, and work continues on other items until you answer. Newest first.

## Status (updated 2026-11-10 20:45)
- **Autonomous run 2 is running:** specs 039, 041, 042, 043, 044, 037, 045, 040 in that order, each merged when green.
- **Next (only when you say so):** 037 pricing, then the follow-up lists at the end of docs/cooking-roadmap.md.
- **Feel standard** (what "feels good" means, in measurable rules): docs/feel.md.
- **Merged since you left:**
  - **Specs 030-032: griddle, assembly, orders 2.0** (and two polish rounds; several review rounds caught a
    raw-plus-burnt "perfect" exploit, a sauce button that could block the dispenser forever, and a hand-over path
    that could pay a guest twice; all fixed):
    - **griddle:** two sides cook separately, flip with the spatula, split ring, grill marks; grilled veggies, toast;
    - **assembly:** build wraps layer by layer, hold to squeeze sauce (a portion costs 10 % of the bottle), fold,
      paper, box or bag; recipe scoring by layers and order;
    - **orders 2.0:** guests order with changes ("no onion", "+ extra garlic"), patience rings, a register where you
      count out change coin by coin or take a card, tips and star reviews in the shift summary (F3);
    - calm view (only the aimed ring and warnings), held food visible in first person, any cook can work the till.
  - **Specs 029 + 029b, deep fryer and polish:** two baskets you lower, shake, raise and let drain; fries and
    falafel; bubbles, rings, a bubbling loop, rattle and drips; tip fries from the basket straight onto a plate you
    hold. Labels speak your words ("golden fries", "well done"). The review caught an exploit (re-frying stacked the
    shake bonus up to a perfect rating), now capped. The stoves stand side by side; the fryer is 3.3 m from the plates.
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
11. **Spec 046 v2 (town time, shifts, progression).** Rewritten after your feedback: a town clock that never skips,
    personal clock in and out with a shift summary, three gratification loops (every order with streaks, your shift,
    long term: player levels plus shop reputation), owner cash and helper wages. Decisions, with defaults in brackets:
    game mode for EA [co-op sessions now, persistent town later with the same rules], progress tied to [player and
    shop], day length [48 real minutes], helper wage [fixed per game hour, owner can change it]. 046 is built only
    after you approve it.
10. **Early Access plan (your new goal).** See docs/early-access.md. After the current queue Claude builds 006 (AI
    gateway, already approved). Approved 2026-11-10: **047** and **048**. Still need your OK: **046** (now v2, question 11) days and progression (save, rating, unlocks),
    **047** main menu and joining (Steam invite, lobby, rejoin), **048** settings and comfort, **049** release
    stability. Default: Claude waits for your approval before building them. Also open for you: O4 backend, O5 hoster,
    O6 moderation, O7 player cap (after the load test in 045).
9. **Blender (you offered it).** Yes, please. With Blender installed, Claude can generate our own low-poly models
   from code (headless `blender -b -P script.py`, exported as FBX into `game/Assets/_Project/Art/Generated`). One
   style, our palette and the same bevels everywhere, which is exactly what spec 040 needs. Install it yourself
   (Claude does not install software): `winget install BlenderFoundation.Blender` (or blender.org, version 4.x LTS),
   then write "Blender installed" here. **Until then:** 040 runs last and uses code-built stand-ins; no downloads.
8. **Crash on quit (Direct3D 12).** After the visual-check tours finish, the game sometimes crashes while shutting
   down (4 of 6 runs): an access violation inside Windows' `D3D12Core.dll`, after networking has stopped; no game code
   on the stack. An older dump from 11-07 shows a different shutdown crash. Players would see "Worktown has stopped"
   when quitting. Claude switched the visual-check tours to Direct3D 11 so checks stay reliable. Option A: Claude
   investigates in a small spec (driver update check, PSO cache, Unity graphics settings, try D3D11 as default).
   Option B: ignore until real players report it. **Default: A**, after the cooking roadmap. Do you also see it when
   you quit the game?
7. **Price scale.** Bills now come from the economy's market values: a wrap costs about **$1,525**, ingredients
   $45–110 each, the player starts with $500, and a cash guest hands over a $1,900 note. That reads like a luxury
   restaurant, not street food. Option A: rescale the whole economy to street-food money (wrap about $9, fries $4,
   ingredients $0.30–2, start with $200), with prices in cents internally so change stays exact. Option B: keep the
   numbers and just call the currency something else. Option C: you set the targets. **Default: A**, done in the
   pricing spec (037), where you also get a menu board to set your own prices. Blocks nothing until then.
6. **FYI, scope note (no answer needed unless you object):** the griddle spec (030) needed grill vegetables that spec
   028 had not added. The builder added paprika, eggplant and mushroom as ingredients (shelf slots, grocer prices
   60 / 70 / 80). Claude approved it under the 24 h grant; say so if you'd rather review such additions yourself.
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
- 2026-11-10 play-test: the register / payment terminal works great ("insane"). Problems found and drafted as specs:
  039 (every counter accepts items, items are not stairs, tools handle towards you), 040 (readable at a glance:
  better models, labels from every angle), 041 (mouse-first: left does, right puts down, no Q).
- 2026-11-08: 24 h autonomy granted; CC0 downloads from Kenney/Quaternius OK; old Mini Characters may be deleted;
  feel comes first ("counter on the wrong side, buttons on the back, everything feels off").

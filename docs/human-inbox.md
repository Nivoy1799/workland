# Inbox for the human

Hop in whenever you like. Answer by writing under a question (or just tell Claude in chat). Claude never waits:
every question has a default, and work continues on other items until you answer. Newest first.

## Status (updated 2026-11-11 13:45)
- **Merged in run 2:** 039 (every counter accepts items, items are not stairs, tools handle towards you) and 041
  (left click does, right click puts down, the wheel turns), 042 (put the cup on the drip tray, then hold to fill;
  play-test: can you fill a drink while doing something else?), 043 (perfect means best, names like "golden fries"
  match the ring, one pace table; pan and pot now twice as fast), 044 (others see food fly into pots and bins and hear
  put-downs, buttons sink, faster sprint start, tip and scoop at the click, both hands on a box, smaller tutorial card,
  tilted ticket rail), 045 (spam guard, late answers fixed, restart paths, D3D11 default, 16-player load test passed),
  047 (title screen: open your shop, join a friend through Steam or by address, lobby with kick on Tab, auto-rejoin).
  006 (AI gateway skeleton: item names via an OpenAI-compatible API, strict JSON, vegan check, cache; stays off, see 20).
  048 (settings: graphics, audio, rebinding, FOV, colour-blind rings), 049 (crash log, freeze watchdog,
  `scripts/perf-check.sh`; 2-hour soak with 16 players passed). Digest: docs/digest-2026-11-11.md.
  Follow-ups 2026-11-11: E takes the cup back from the tray; the cursor relocks after settings; controls fit 720p;
  joining a friend while hosting asks first; Alt-F4 tells your guests the shop closed; a kicked player stays out for
  the session. For the real Steam app id, upload game/Steam/rich_presence_localization.vdf in Steamworks.
  **Please try:** join through Steam from a second PC (spec 003 AC2 and 047 AC4).
- **Run 3 (from 2026-11-11 afternoon):** merged: 052 climb simulation (findings in question 22) and the 041/042/044
  follow-ups. Building: 054 network headroom, then 053 food corner with the save file. 037 still waits for question 17; 040 is with your second session.
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
22. **What the climb simulation found (spec 052, merged).** With 4 and 16 players the climb works: stage 5 after about
    19-20 h, inside the targets. Stage 1 to 2 takes about 1.2 h for every crew size. Four design problems remain:
    1. **A solo player gets stuck in stage 2.** One person cannot serve the guests of a full menu: about 156 actions a
       service against 25-47 guests, and more dishes cost more actions per guest. Default: fewer, more patient guests
       for crews of 1-3, and the crew chooses which dishes are on the menu, so it can drop one.
    2. **Charging +$1 looks like a shortcut.** It isn't really about price. Higher prices turn guests away, and fewer
       guests pass the stage 4 check sooner. That is fixed by point 4.
    3. **Late in the game, money has no use.** Rich crews buy everything by about 25 h. Default: costs that come
       back and grow with you, such as a bigger place with higher rent, staff wages, event stakes that grow, and
       cars and decor (D34).
    4. **The stage 4 gate punishes growth.** Stage 4 asks whether the crew runs the place with few players, and every
       extra service bought in stage 3 makes that harder. Default: stage 4 is unlocked by an "automation" purchase
       (staff and machines) plus stars, not by the few-players check.
    If you agree, I write spec 052b (climb tuning v2) and rerun the simulation. Meanwhile 053 (stage 1, which already
    works) goes ahead.
20. **AI gateway (spec 006) and player-hosted games (D33).** Spec 006 was planned for official servers only: they
    alone held the gateway's password. Under D33 the host is a player's PC, so that password ships inside the game and
    is public. Anyone could then run up our API bill, or claim an item's name first and show a bad name to everyone.
    Default: merge the skeleton (it is not live and costs nothing) and keep the gateway **off** until a follow-up
    spec adds:
    - a login per host (for example Steam tickets checked by the gateway; O5);
    - item names built on our side;
    - item checks through /sim;
    - protection for the shared cache.
    Until then, games use the built-in fallback names. Also FYI: the gateway now rejects animal-product words (D27) with
    a copy of the game's own word list, and a test keeps both lists equal.
19. **Colour-blind ring colours (spec 048) and D32.** D32 says one palette of 16 named colours. The new colour-blind
    option for the cooking rings uses 4 extra colours from the Okabe-Ito set, a standard palette that is safe for colour
    blindness. Default: allow them as a separate "accessibility" set next to the 16, used only when that option is on.
    Say no if you'd rather pick 4 of the 16.
18. **Spec 045 results (in review).**
    - **16-player load test passed:** tick time p95 1 ms (target under 20), 49.6 KB/s per player (target 50, just met),
      host upload about 0.8 MB/s, all 17 processes alive.
    - **VPN / Tailscale (MTU 1280):** the direct-IP transport (Tugboat) cannot run there without editing FishNet.
      Proposed decision: **players connect through Steam; direct IP stays for LAN and dev.** Default: yes, and that
      becomes D37 once you agree.
    - **Crash on quit:** it is inside Direct3D 12 itself (no game code on the stack). Windows builds now start with
      D3D11 and fall back to D3D12. Default: keep it until a Unity update.
    - **FYI:** a partly shaved spit cone now stays on the spit after a restart (it used to be lost).
17. **Spec 037 (street-food prices) conflicts with D23 "whole dollars only, no cents".** 037 wants cents (a wrap $9.50,
    an ingredient $0.30, coins 0.10-2). I did not build it, because D23 is your decision. Options:
    - **A (default once you answer, recommended): cents.** D23 becomes "dollars with cents; integer cents inside".
    - **B: whole dollars.** A wrap is $9, an ingredient $1 or $2, coins $1 and $2, notes $5, $10 and $20. Small ingredients
      are bundled. Simpler change, coarser prices.
    Meanwhile the queue goes on with 045, then 047, 048 and 049. 037 waits for your answer.
16. **Cooking tuning after spec 043 (defaults are in; change any number you like).**
    - **Good versus Perfect:** Good rates 0.9 of Perfect. Default: keep 0.9.
    - **Just burnt still rates well:** for about 2 s past the window, food still rates about 0.95 and still counts as an exact
      order. Default: keep it as a grace margin.
    - **Sauteed apple** with the faster pan: Perfect from 2.5 to 4.9 s, burnt at 6.5 s. It is playable but twitchy. Default:
      leave it until you have played it; say "wider" and it becomes about 2x wider.
    - **Griddle:** the stars use the mean of both sides, so one side Good and the other Perfect can give the best stars.
      Default: keep the mean (simpler); say "per side" if you want the stricter rule.
    - **Onion and tomato** still have a placeholder window, so they are Good from the first second. Default: give them real
      windows when 040 brings the new models.
15. **Gamepad turn (spec 041).** The bumpers already open the emote wheel (LB) and ping (RB), so turning stays on Y.
    Default: keep it on Y. Say if you'd rather move the emote wheel or the ping.
14. **Setting (2026-11-10 brainstorm):** the year 2222 as imagined in 1950–80 (retro-futurism), with a little
    steampunk in the kitchen machines; robots and AI exist, but people work by passion; native vegan. Money question,
    default **C**:
    - basics are free;
    - the economy is a voluntary passion game: credits are the scoreboard and the way to big dreams;
    - cash is a retro ritual (that's why guests pay in coins);
    - real hand-grown ingredients are rare and cost credits;
    - robots can do everything, but hand-made is the luxury.
    When you say yes: Claude writes docs/setting.md and proposes D37 (setting) plus a palette update for D32 and
    spec 040.
12. **046 direction after the brainstorm (2026-11-10).** You liked crew competition ("like Schedule I, making the most
    in a group"); weapons and PvP harm are out for EA. Proposal: rewrite 046 as **"Crew and empire, slice 1"**:
    - crew and shop (roles and shares);
    - signature dishes (a recipe lab: the sim values a dish, AI names it, the crew prices it);
    - the first NPC employee (dishwasher);
    - a food truck for events ("big jobs", the heist feeling);
    - a crew ranking per server (global later).
    Districts, more employees and more locations come as later specs. **Default: Claude drafts this as 046 v3 and asks
    you before building.**
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
- **2026-11-11, questions 13 and 21:** "13 and 21 go burn the tokens. AI live we skip, you are right." Design v1 is
  approved (D37). Specs 052, 053 and 054 are approved with their defaults. The live AI gateway is skipped for Early Access:
  names are generated offline (D38), and 055 is deferred.
- 2026-11-10 play-test: the register / payment terminal works great ("insane"). Problems found and drafted as specs:
  039 (every counter accepts items, items are not stairs, tools handle towards you), 040 (readable at a glance:
  better models, labels from every angle), 041 (mouse-first: left does, right puts down, no Q).
- 2026-11-08: 24 h autonomy granted; CC0 downloads from Kenney/Quaternius OK; old Mini Characters may be deleted;
  feel comes first ("counter on the wrong side, buttons on the back, everything feels off").

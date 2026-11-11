# Haven Nine: the town plan, v1 (DRAFT for approval, 2026-11-11)

Status: **v1 draft, waiting for the human's approval.** It pulls together spec 051 (the town layout and its answers),
the style lab rounds 6–8 (spec 046) and the story (`docs/town-story.md`, draft 3) into one plan that implementation
can start from. When approved, it gets decision lines (section 9) and becomes binding the way `docs/game-design.md`
is (D37). Pictures: style lab round 8, https://claude.ai/artifact/VCszDibQnpUFGyD4oKEzNm (rounds 6 and 7 for the
districts and the seasons).

How it looks (shapes, palette, wear, the four revival looks, light moods) is in `docs/art-direction-v2.md`
(draft), which merges into `docs/art-direction.md` once approved. W6 and W7 build from it.

It does not change the game design (D33–D40): a co-op restaurant climb for up to 16 players, player-hosted. This plan
is the world that climb happens in.

## 1. The world in one paragraph
**Haven Nine** is a town under a glass Dome, one of twelve shelters built when the old Weather Engines split the world
into calm valleys walled in by a never-ending storm, the **Storm Ring**. Fifty years ago it was a working utopia:
robots did every job, the food printers made good food, nobody had to work. Then the last engineers retired, robots
broke and stayed broken, the printer food turned grey and the seeds got worse. Many people drifted into betting
halls and dream booths. **The game starts in that tired town with a cart and a crate of poor potatoes. Every good meal
the players serve brings the town back, and they see it happen around them.**

## 2. Fixed inputs
- D8 one hand-built town; D29 1 unit = 1 m; D31 walk 2.5 m/s, sprint 5 m/s.
- D33 co-op, player-hosted, saved on the host's PC; D36 16 players; D40 tuned for 4 players.
- D34 and D37 (`docs/game-design.md` v1): the climb in five stages; time runs only while the game runs; nothing
  decays while nobody plays.
- D35 no mechanic profession. Robots come back through the restaurant (stage 4 automation), never as a repair job.
- D27 and D28 native vegan; the dark past only as the game's own named era.
- D38 no live AI at Early Access: all generated text is made offline and shipped.

## 3. The map and its borders

### 3.1 Shape and size (unchanged from spec 051)
- A bowl that opens to a lake in the east and rises about 45 m to Greenhouse Hill in the north. Every path stays
  below 5 %, and every stair has a ramp or a lift next to it.
- The playable area is about **640 × 480 m**, edge to edge in 4 minutes walking. The core is about 230 × 160 m, and
  every core point is within 90 s of Fountain Cross.
- The districts, pitches and rentable places are those of spec 051 sections 3 and 4 (11 districts; cart pitches
  C1–C8; small places S1–S6; restaurants R1–R4; the stage 5 places).

### 3.2 The Dome
- An organic glass shell, not a flat roof: about **770 × 640 m** in plan over the bowl. Its crown is about **250 m**
  up, higher than every tower, and leans slightly west. It is steep at the foot and round at the top.
- Its ribs branch like the veins of a leaf. They are lit at night.
- Officially it is called "the Sail"; in the Low Ring people call it "the Lid".
- **Weather inside is planned** (section 6): a daily Rain Hour, roof panels that open on fine days, storms and hail
  that hit the glass from outside.

### 3.3 The borders (hard, each with a reason)

| Edge | What you see | What stops you |
|---|---|---|
| **West: the Wreck** | the Albatross sealed in the wall with a riveted collar ("SEALED 2161"): the tail and the night market inside, the nose outside in the storm | a bulkhead welded shut at the wall; its porthole shows the storm |
| **South: the Wildbelt** | a wild strip along the inside of the wall where the freed animals live | the sanctuary fence, then the glass |
| **East: the lake** | the wall running into the water | water gate 3, for cleaning boats only |
| **North: the tunnel** | the round pressure door to Haven Eight, the seasonal cargo train | "Transit closed. Next service: Autumn". Goods only |
| **All round** | service airlocks; robots go out to sweep the glass | "Keepers and robots only" |
| **Up** | the ribs, 250 m up | the glass. Players never fly |

The safety net from spec 051 still applies:
- an invisible collider 15 m behind every border;
- a server check that puts any player outside the play area, or below the fall height, back at the nearest safe
  spot. The server owns positions (AGENTS rule 6).

## 4. The revival (new core of the world)

### 4.1 What the player sees
Each district has a **revival level**, and its look and life follow it:

| Level | Name | Look and life |
|---|---|---|
| 0 | **Grey** | dead or flickering signs, broken robots slumped in corners, shutters down, litter, grey printer bars, few people out, most of them listless |
| 1 | **Patched** | lamps back on, a few shops open, a queue somewhere, the first potted plants, people stop to talk |
| 2 | **Lively** | gardens replanted, clean windows, music, people out in the evening, robots working again |
| 3 | **Thriving** | bright signs, full terraces, the Dome's ribs lit over the district, festivals |

The town starts at level 0 everywhere, except a little warmth around the station. The Low Ring (section 5) is the
last to come back.

### 4.2 How it works in /sim (deterministic)
- **Input:** every meal served or delivered adds revival points to the district where it is eaten, weighted by its
  quality. A good dish counts much more than a poor one; printed paste counts nothing. A small bonus comes at each
  climb stage.
- **Thresholds** are stored as a fraction of the goal and grow with the crew (D40), so a solo player and a crew of 4
  both see the town change at a fair pace.
- **It never goes back.** A district never loses a level. That matches "nothing decays while nobody plays" (D37),
  and players do not lose what they built.
- **Effects:**
  - footfall in the district rises with its level (proposed +0 %, +10 %, +20 %, +30 %), under the area demand
    ceiling;
  - NPC mood and lines change;
  - the look switches (section 4.3);
  - some suppliers and places only open at a level (section 4.4).
- **Seeded and pure:** no randomness without the save's seeded RNG and no wall clock (AGENTS rule 7). Every public
  function has a unit test.

### 4.3 How it works in Unity (cheap to build)
- The four looks are **swaps, not rebuilds**: lights on or off, sign materials (dead, flickering, lit), shutters,
  litter and plant sets, robots (slumped or working), NPC density and animation mood.
- The building shells stay the same.
- One component per district reads the level from the server and switches its sets.
- The first districts to get all four looks are Station Square and Main Street (where stage 1 happens); the rest
  follow.

### 4.4 Food quality and the seeds
- At stage 1 the Market Hall sells only **worn seed stock**: poor potatoes, tired tomatoes. The quality ceiling of
  the ingredients on sale rises with Greenhouse Hill's revival level and the climb stage.
- At stage 5 the lab (D34 "create new ingredients") breeds better crops. In game terms these are crop properties the
  sim computes; there is never real-world chemistry or biology in the game.
- This sits on top of the existing ingredient quality (specs 028 and 034) and does not replace it.

### 4.5 Robots
- Broken robots are part of the grey look.
- At stage 4 (automation) the players buy **refurbished robots** from an NPC workshop in Workshop Lane, which
  reopens at its district's level 2.
- There is no repair gameplay in v1 (D35). The robot by the station that loops "Welcome to Haven Nine. Welc—" says
  "Welcome back" once its district reaches level 2.
- **After Early Access: repair comes back as a mechanic role** (the human, 2026-11-11: "feel free to add in mechanics
  again ... in later stage, just to make it doable, early access release"). Players relearn the lost trade: they fix
  their own kitchen robots and then the town's, and each repaired robot adds revival points to its district. This
  revises D35 for a later release; the restaurant stays the heart of the game.

## 5. The Low Ring (the dark side)
- **Where:** the shaded ground floors of Tower Rise and a band along the inside of the Dome's foot. It is dim even at
  noon.
- **Look:** soot black and rust, orange sodium lamps, signs with dead letters, plain concrete service ducts and vent haze (industrial wear, never steampunk brass or gears; docs/setting.md), broken robots, the
  scrap pile, overflowing bins.
- **What is there:** betting halls (drone races, credit slots), dream booths, cheap printer bars, and a few warm
  corners where someone cooks by hand. It is a place you can rent cheaply and deliver to.
- **Gambling and addiction stay in the world**: places, NPCs and their stories. They are never a loop the player
  plays (proposed, open question 2). All vices are made up. No crime and no harm (game-design pillar 5).
- **Its NPCs have arcs** that move with the revival: the regular who leaves the dream booth for a job at your place,
  the betting-hall owner who turns the hall into a food court. Recovery is shown with respect.

## 6. Time, night and weather
- **Day:** 40 real minutes per game day: dawn 3, day 24, dusk 3, night 10 (spec 051 section 6).
- **Night is never skipped.** It is played like the day, with its own demand: the night market, late deliveries,
  bars. The bed in your flat saves, ends your shift and gives a "rested" bonus.
- **Night look:** street lamps first, then signs and windows in a wave, the Dome's ribs lit. Night is never pitch
  dark, so older eyes can still read the street.
- **Seasons:** 4 × 7 game days, deterministic from the seed. They change the look and the demand, never the paths.
  The festivals are Blossom Fair, Lake Nights, Landing Day and Winter Lights (spec 051 section 7b).
- **Planned weather under the Dome:**
  - a daily Rain Hour that empties outdoor tables for a while;
  - open-panel days that fill them;
  - storm days, when lightning hits the glass and people stay under roofs.
  - Weather is deterministic from the seed and the day.
  - Comfort settings (spec 048): lightning flash strength, rain loudness.
- **Parked:** the compost loop (the human, 2026-11-11: "skip for now").

## 7. Player homes
The **Crew House** in the Homes district has 16 free flats, one per player slot, a shared kitchen table and a roof
garden (spec 051 section 4b). You decorate your flat with money, and it has a wardrobe, a mailbox and the bed.
Your door is Private by default. The flats are part of the host's save.

## 8. v1 and later

| | In v1 (towards Early Access) | Later releases |
|---|---|---|
| Map | the bowl under the Dome, the 11 districts in graybox, the border set pieces, the safety net | dressed districts beyond the core |
| Revival | the /sim model; four looks for Station Square and Main Street; NPC lines | four looks for every district; Low Ring NPC arcs in full |
| Time | 40-minute day, night play, planned weather (Rain Hour, open panels, storm days) | seasons with festivals, snow |
| Homes | — | the Crew House |
| Robots | broken robots in the grey look; refurbished robots bought at stage 4 | the mechanic role: repair your robots, then the town's (revises D35) |
| Story | told in the world: posters, the museum, the Wreck, signs, NPC lines (offline text, D38) | the seed vault, going outside in a suit, the tunnel to Haven Eight, the storm rumour |

## 9. Decision lines to add when approved
Numbers are given at merge, after main's D40.
- **Setting:** Haven Nine, a declined utopia under a Dome; the players' climb brings it back (revival). This replaces
  the clean-utopia framing of `docs/setting.md` and answers its open question 2 (the too-clean look).
- **Borders:** the Dome wall with the Storm Ring behind it; no outside in v1.
- **Revival:** per-district levels 0–3, computed in /sim from the quality of meals served, never going back.
- **Low Ring:** gambling and addiction only as world content, never a player loop (if the human agrees, open
  question 2). Pillar 5 reads "peaceful, not perfect".
- **Night:** never skipped; the bed ends your shift.
- **Time:** a 40-minute day; seasons of 7 game days (seasons after v1).
- **The Wreck:** the airship Albatross, Ark Flight 9.
- **Mechanic, later (revises D35):** robot repair returns as a role after Early Access, not in v1.

## 10. Work packages for the orchestrator
These are proposed follow-up specs, without numbers (main owns the numbering). Each names its folders.

| # | Package | Folders | Needs | Notes |
|---|---|---|---|---|
| W1 | **Decisions and docs:** the D-lines in section 9; update `docs/setting.md` and the game-design pillar 5 wording; merge this plan and the story | `docs/` | the human's approval | first |
| W2 | **Town and location model:** districts, pitches and places as data, footfall per district and hour, walking distances | `sim/` | W1 | check overlap with specs 050 (street life) and 052 (climb simulation) |
| W3 | **Revival model:** levels, points from served meals by quality, thresholds by crew (D40), never going back, effects on footfall and on ingredient quality ceilings | `sim/` | W2 | pure functions, unit tests for every public function; tune with the 052 climb sim |
| W4 | **Clock and planned weather:** the 40-minute day with night demand, the Rain Hour, open-panel and storm days, all seeded; the season calendar (data only in v1) | `sim/` | W1 | can run in parallel with W2 |
| W5 | **Graybox of Haven Nine:** terrain bowl, district blocks, paths, navmesh, the Dome as a shell, the border set pieces as blocks, the colliders and the server safe-spot check | `game/` | W1 | netcode part (safe spot) stays with Opus |
| W6 | **Revival looks:** a district component that reads the server's level and swaps light, sign, prop, robot and NPC sets; the four looks for Station Square and Main Street | `game/` | W3, W5 | swaps only, no rebuilt shells |
| W7 | **Day, night and Dome light:** light moods, the lighting wave at dusk, the Dome's lit ribs, Rain Hour and storm visuals with the comfort settings | `game/` | W4, W5 | follows the art pass |
| W8 | **The story in the world:** museum plaques, posters, signs, robot loops, NPC lines per revival level, generated offline (D38) through the schema and moderation rules | `ai-gateway/` tools, `game/` data | W1 | AI guardrails in AGENTS.md apply |

Order: W1 → (W2, W4, W5 in parallel) → W3 → (W6, W7) → W8 alongside.

## 11. Open questions for the human
1. **Founders:** name the Albatross's captain and cook (placeholders Ilse Marrow and Rosa Vell), or leave them
   unnamed?
2. **Gambling and addiction:** only in the world (proposed), or also a player loop?
3. **The storm rumour:** a rumour (proposed), a later plot, or dropped?
4. **The Smoke Years:** museum and jokes only (proposed), or nothing?
5. **Revival footfall bonus:** +0/10/20/30 % per level (proposed), to be tuned in the climb sim?

---
id: 070
title: The story in the world (museum plaques, posters, signs, robot loops, NPC lines per revival level; written offline, checked with the gateway's schema and vegan rules, shipped as data)
status: approved
owner: architect
builder: sonnet       # 070a (/ai-gateway tools and the text), then 070b (/game)
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W8 of docs/town-plan.md section 10.

# 070: The story in the world

## Goal
Haven Nine tells its story without dialogue trees: museum plaques, old and new posters, signs, broken robots looping a
line, and short NPC lines that change with the district's revival level. All text is written offline (D38), checked by
the gateway's schema, vegan rule and moderation seam (the 056 tools), reviewed by the human, and shipped as one data
file the game looks up by id. The LLM never decides anything here, and no text is generated at runtime.

## This is an integration spec
It touches `/ai-gateway` and `/game`, built in order as **070a** (tools, schema and the text) and **070b** (the game),
each with its own branch, commit and review. Each builder touches only its part's files.

## Depends on
D27, D28, D38, D41, D42, D44, O6 (OPEN, not decided here); docs/town-story.md ("the human's framing" is binding);
docs/town-plan.md sections 4-5; spec 056 (NameBake, review sheet, publish rules); spec 064 (district and place ids).
070a needs only 064. **070b needs 067 and 068 built** (places to hang text on, levels, `SignState`).

## Scope
**070a (/ai-gateway)**
- `ai-gateway/schemas/world_text.v1.schema.json` (new); `src/Worktown.Gateway/Worktown.Gateway.csproj` (change: embed
  it), `src/Worktown.Gateway/Validation/SchemaValidator.cs` (change: `ForWorldTextV1()`).
- `ai-gateway/tools/NameBake/` (change): `WorldTextCommand.cs`, `WorldTextRules.cs`, `WorldTextSheet.cs` (new),
  `Program.cs` (subcommands `world-check`, `world-publish`).
- `ai-gateway/offline/world-text/` (new data, committed): `source.v1.json` (the text), `review.tsv` (decisions),
  `lore-denylist.txt` (tool only, never shipped).
- `ai-gateway/tests/Worktown.Gateway.Tests/WorldText*Tests.cs` (new); `scripts/world-text.sh` (new);
  `ai-gateway/README.md` (change: section "World text").
**070b (/game)**, paths under `game/Assets/_Project/`
- `Scripts/Story/` (new, namespace `Worktown.Game.Story`): `WorldTextBook.cs` (pure), `WorldTextLoader.cs`,
  `WorldTextHolder.cs`, `StoryLineRules.cs` (pure), `RobotLoop.cs`, `StoryPlacements.cs` (pure table).
- `Resources/WorldText.json` (new: a valid book with `"entries": []`), `Editor/StoryPlacer.cs` (new, called by
  `TownGrayboxBuilder`), `Scripts/Revival/AmbientPeople.cs` and `RobotLook.cs` (change: speak lines, show the screen),
  the regenerated scene.
- `Scripts/Dev/StoryTourRunner.cs` (new), `scripts/visual-check.sh` (change: `story` tour); tests in `Tests/EditMode/`;
  the D27 word test (change: covers the book); `Resources/WhatsNew.json`; this spec.

## Out of scope
A live gateway or runtime generation (D38, 055 deferred); choosing the moderation approach (O6); NPC dialogue,
quests and the Low Ring arcs beyond one arc; other languages; voice; the Wreck's interior, the seed vault and outside
(later releases); dish names (056); deciding the town plan's open questions 1, 3 and 4 (see Writing rules).

## Design

### Writing rules (checked by tool where a machine can, by the human's review for the rest)
1. **The human's framing** (D41, D42): climate change killed the Earth outside; the Domes were built for safety;
   robots made life inside a utopia; then it broke apart: nobody knew how to repair things, made-up vices and
   addiction spread, people drifted; everyone is vegan out of need, because nothing else was left (never preaching).
2. **D27:** no animal products as goods or food, even in the past tense of a menu; the vegan rule runs on every line.
3. **D28:** the dark past only as the game's own era; no names of or comparisons to real atrocities, no real people,
   places, brands or dates before 2090.
4. **D44:** gambling and addiction only as world content: no instructions, no glamour, no prompt to the player to bet;
   recovery shown with respect, never a joke about an addict; all vices made up (the betting halls, the dream booths);
   **no real drug names, no substances, effects or doses** (the denylist covers real drug names).
5. **AGENTS guardrails:** no real chemistry, medicine or biology; better seeds are "better seeds", never a method.
6. **Peaceful, not perfect** (pillar 5): no harm words (058's list: steal, sabotage, poison, burn, break, fight, hurt,
   kill, destroy, attack) in NPC and robot lines.
7. **Open plan questions stay open:** the founders stay unnamed ("the captain", "the ship's cook"; question 1); the
   storm rumour is not used (question 3); the name "Smoke Years" appears only in entries with `"needs":
   ["town-plan-q4"]`, which publish holds back until the human confirms (question 4); meanwhile plaques say "the old
   world".
8. Plain English, short sentences, readable for older players; ASCII plus the em dash (U+2014).

### The text (`source.v1.json`, written by the 070a builder; counts are minimums)
| Kind | Where | Count | Example |
|---|---|---|---|
| `plaque` (title + body) | the museum, Main Street west | 8 | "The Albatross": Ark Flight 9 came through a gap in the storm... |
| `poster` | Main Street, Station Square | 12 | levels 0-3: Golden Calm posters ("No one in a Haven will ever go without"); levels 1-3: "Real food, made by hands" |
| `sign` | districts, places, borders | 16 | "SEALED 2161", "Transit closed. Next service: Autumn", "Keepers and robots only" |
| `robot` | the station greeter, a printer robot, a sweeper | 3 x 2 | levels 0-1 "Welcome to Haven Nine. Welcome to Haven Nine. Welc—"; levels 2-3 "Welcome back to Haven Nine." |
| `npc` | `station_square`, `main_street` | 6 per district and level (48) | level 0 listless, 1 hopeful, 2 lively, 3 proud ("Up there they call it the Sail. Down here it's the Lid.") |
| `arc` | `main_street` | 4 steps x 2 | the dream-booth regular who comes back, one step per level of the shop's district |
The eight plaques cover: the old world outside, the Weather Engines and the Storm Ring, the Charter and the twelve
Havens, the Albatross and the animals, the Dome and the Hail Summer of 2161, the Golden Calm, the Long Fade, and "Why we
eat what we eat" (out of need, D42).

### Schema (`world_text.v1.schema.json`, `additionalProperties: false`)
`{ "format": "worktown-world-text-source", "formatVersion": 1, "approvedNeeds": [], "entries": [ { "id": "npc.main_street.l2.03",
"kind": "plaque|poster|sign|robot|npc|arc", "district": "<064 id>|any", "minLevel": 0, "maxLevel": 3, "title": "...",
"text": "...", "needs": ["town-plan-q4"] } ] }`. Ids `^[a-z0-9_]+(\.[a-z0-9_]+){1,4}$` (at most 64); `title` only on
plaques (at most 40 chars); text lengths: sign 32, poster 90, robot 70, npc 80, arc 120, plaque 420; levels 0..3 with
`minLevel <= maxLevel`.

### The tool (070a; no upstream call, no key, no `.env` needed)
- `bash scripts/world-text.sh check`: runs `NameBake world-check`: the schema (`SchemaValidator.ForWorldTextV1()`),
  `VeganContentRule`, the registered `IModerator` (today the placeholder; O6), the lore denylist (whole words,
  case-insensitive), the harm list on `npc`, `arc` and `robot`, unique ids, districts known (a test compares the
  tool's copied id list with `TownCatalog.CreateDefault()`; the test project already references `/sim`), the `needs`
  rule (only `town-plan-q4`, only on plaques). It merges `review.tsv` (`id, kind, district, levels, text, flags,
  decision, note`): a row keeps its decision while its text is unchanged; a changed text clears it. Output: counts and
  one line per problem (`id rule`), never more; exit 0 clean, 2 problems.
- `bash scripts/world-text.sh publish`: entries with decision `ok` whose `needs` are all in `approvedNeeds`, re-checked
  as above (any failure: exit 2, nothing written), written to `game/Assets/_Project/Resources/WorldText.json` (`.tmp`
  then replace; sorted by id, LF, no timestamps; byte-identical for the same input):
  `{ "format": "worktown-world-text", "formatVersion": 1, "moderator": "<class name>", "entries": [ {id, kind,
  district, minLevel, maxLevel, title, text} ] }`. Prints published, pending, rejected and held counts.
- **Moderation (O6 OPEN):** the placeholder moderator, the rules and the human's review are gates, not an O6 choice.
  Whether reviewed text may ship to players before O6 is decided is the human's call (as in 056).

### The game (070b)
- `WorldTextBook.Parse(json)` (pure, as 056's `ShippedNames`): wrong format, newer version or over 1 MB gives `Empty`
  and one warning; per entry the copied id pattern and lengths (a test checks them against the schema file) and
  `ContentRules.FindForbidden == null`; bad entries dropped, duplicates keep the first. `TryGet(id)`,
  `Lines(kind, district, level)`. `WorldTextLoader.Current` loads `Resources/WorldText` once.
- `WorldTextHolder` (component: id, kind): shows the entry's text in world-label style on a plain field (Cream or Ink,
  art direction 7); a missing id shows nothing. Minimum text heights: sign 0.12 m, poster 0.04 m, plaque 0.025 m
  (readable at 2 m in first person). Signs ask 068's `SignState` for their state and dark letters.
- `StoryLineRules` (pure, no RNG): `Pick(lines, npcId, day)` = `lines[(npcId * 31 + day) % count]`; a stand-in figure
  (068 `AmbientPeople`) speaks only when a player is within 4 m, at most once per 8 s, and at most 2 bubbles show
  within 10 m; the line comes from its district and current level; `arc` steps show on one named figure on Main Street.
- `RobotLoop`: the robot's screen shows its `robot` entry for the district's level; a looping line scrolls and cuts off
  at the dash.
- `StoryPlacements` (pure table): every holder's id, the 064 place or node it hangs on, an offset and a facing. The
  museum gets an open front room (a wall run of 8 plaque panels in its 067 block); border signs at 064's border places.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes after each part.
- [ ] AC2 (070a): the schema accepts a full fixture and rejects each field rule (bad id, unknown kind, title on a
      sign, text too long, levels reversed, an extra property).
- [ ] AC3 (070a): `world-check` on fixtures reports each rule once (D27 word, a denylist word from a test-only list,
      a harm word in an npc line, unknown district, duplicate id, `needs` on a poster) with exit 2; the committed
      `source.v1.json` passes with exit 0.
- [ ] AC4 (070a): publish writes only `ok` rows, holds back `needs` rows unless approved, gives byte-identical files
      twice, and a hand-edited bad row stops it with exit 2 and no file change; a changed text clears its decision.
- [ ] AC5 (070a): `source.v1.json` meets every count in the table; no entry names a founder; `needs` appears only on
      plaques; every text is ASCII plus the em dash.
- [ ] AC6 (070b): `WorldTextBook` rejection fixtures (format, version, bad id, forbidden word, too long, duplicate)
      and a valid book; its copied patterns equal the schema file's.
- [ ] AC7 (070b): `StoryLineRules` picks only the district's lines for its level, the same for the same `(npc, day)`;
      the 4 m, 8 s and 2-bubble rules hold on literals.
- [ ] AC8 (070b): Built scene: every `StoryPlacements` id has a holder at its place; text heights meet the minimums;
      every placed id exists in `source.v1.json` (the test reads that file).
- [ ] AC9 (070b): `--tours story` with a test book: the museum plaques; a poster at level 0 and 2; the station robot at
      level 0 (the cut-off loop) and level 2 ("Welcome back"); an NPC line on Main Street at level 1; a dead sign with
      dark letters at level 0. 0 Exception lines; the existing tours pass.
- [ ] AC10 (070b): the D27 word test covers the shipped book; `WhatsNew.json` entry.

## Test plan
- 070a: `dotnet test ai-gateway/Gateway.sln` (`WorldTextSchemaTests`, `WorldTextRulesTests`, `WorldTextSheetTests`,
  `WorldTextPublishTests`); fixtures in temp folders; a test-only denylist with made-up words.
- 070b: EditMode `WorldTextBookTests`, `StoryLineRulesTests`, `StoryPlacementsTests`, `BuiltStoryTests`, the D27 test.
- Commands: `bash scripts/verify.sh`; `bash scripts/world-text.sh check`; `bash scripts/visual-check.sh --tours story
  --out .verify/visual070`.

## Human steps
- Review every row of `review.tsv` (about 120 short rows): `ok` or `no`; then `world-text.sh publish` and commit.
- Answer town-plan questions 1 (founders), 3 (the rumour) and 4 (the Smoke Years) when ready; add `town-plan-q4` to
  `approvedNeeds` if the name is confirmed.
- Decide when reviewed text may reach players (O6).

## Open questions (recommended default in brackets)
1. Who writes the text. [the 070a builder writes it by hand, offline, following the rules; no LLM call through the
   gateway; the human reviews every row]
2. Moderation (O6). [OPEN, not decided: the placeholder moderator runs; shipping beyond dev builds waits for the human]
3. Founders, rumour, Smoke Years (town-plan questions 1, 3, 4). [unnamed; not used; held back behind `needs`]
4. Made-up substances. [none named in v1: the vices are the dream booths and the betting halls]
5. Language. [English only in v1]
6. NPC line rate. [one bubble per figure per 8 s, within 4 m, at most 2 in view]
7. The 050 overlap. [050's signs, posters, museum, exclamations and robot lines are this spec's; 050 keeps the
   printer, the coin ritual, the rankings board dressing and the start cart]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

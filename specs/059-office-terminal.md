---
id: 059
title: The office terminal (menu, services, results in plain words; a pocket clipboard; co-op change log)
status: approved
owner: architect
builder: sonnet
---

> Approved by the human on 2026-11-11 night ("feel free to do everything") with the recommended defaults of its Open questions.

# 059: The office terminal

## Goal
One place in the back office where the crew runs the business without spreadsheets (game-design "Not in the game"):
switch dishes on and off, set prices and a special of the day, see which services are open, and read how the last
service went in plain words and simple bars. A pocket clipboard shows the same facts read-only, anywhere. Anyone can
change things; a short log says who changed what. Later specs add apps here (furniture 062c, brand and marketing 060).

## Depends on
062a (pieces, ids, `Backfill`), 062b (`ShopDoor`, the build-mode entry), 037 (menu board prices, `BoardQuote` at
join), 057 (stage 2, recipe ownership and on/off, `BoardConfirm`), 034 (the in-world screen pattern of `TerminalView`),
D34, D39. 058 is optional: see "Rival share" below.
**Order:** build after 062b and before 062c (062c sells furniture through this terminal). If it must start right
after 062a, the Services app ships without the door and build rows, and those rows join 062b's build as one item.

## Scope
Only `/game` (plus the tour hook). Paths under `game/Assets/_Project/`.
- `Scripts/Office/` (new, `Worktown.Game.Office`): `OfficeTerminal.cs` (NetworkBehaviour, server writes),
  `OfficeTerminalView.cs` (client screen), `TerminalApp.cs` (enum), `TerminalOptions.cs` (pure: option packing),
  `MenuAppRules.cs`, `ServicesAppRules.cs`, `ResultsRules.cs` (pure: words and bars), `ServiceRecord.cs` (struct),
  `ServiceHistory.cs` (pure), `SpecialRules.cs` (pure), `ChangeLog.cs` (pure: entry packing and text),
  `ClipboardRules.cs` (pure), `ClipboardView.cs` (client overlay).
- `Scripts/Cooking/StationKind.cs` (`OfficeTerminal` appended last), `KitchenService.cs` (route its presses).
- `Scripts/Shop/MenuBoardDesk.cs` (change: one server method `ServerStepPrice(line, delta, playerSlot)` used by the
  board and the terminal), `MenuBoardView.cs` (a "Special" tag on that line).
- `Scripts/Climb/ShopStage.cs` (change: the 057 recipe toggle callable from the terminal with the player slot).
- `Scripts/Npc/CustomerService.cs` (change: the special at the order mapping; per-dish sales and waits to the record).
- `Scripts/Player/PlayerInputReader.cs` (Tab), `Scripts/UI/ControlsHelp.cs` (F1 line).
- `Resources/Layouts/default.json` (an `office_terminal` piece in the back-office corner, flagged for `Backfill`),
  `Editor/PiecePrefabBuilder.cs` (its prefab: stand-in wooden case, dark screen).
- `Scripts/Save/` (`ShopSave.cs`, `ShopSaveCodec.cs`, `SaveMigrations.cs` next version, `SaveService.cs`).
- `Scripts/Net/ProtocolSurface.cs` (+1), `Scripts/Dev/OfficeTourRunner.cs` (new), `scripts/visual-check.sh` (`office`).
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec. No change in `/sim`.

## Out of scope
Furniture, brand and marketing apps (062c, 060). Stock ordering (the 034 terminal stays at the back wall). Delivery,
tables or any stage 3 service. Staff. Graphs over many days. Any AI text (D38). Art beyond a stand-in (art pass).

## Design

### The terminal
- A `office_terminal` piece on the back-office wall (by the back door; the exact spot is a layout pose, so build mode
  can move it). Stand-in: a wooden box with a dark screen and Mustard text (D32 palette names only).
- Screen: a tab row (Menu · Services · Results, later more), 6 rows, a footer with the 2 newest log lines. The page and
  tab are each client's own (as `TerminalView`). Left click on a tab or a row button (041).
- Presses use the existing station intent: `option = app << 12 | row << 4 | action` (`TerminalOptions.Pack/TryUnpack`;
  action 0 press, 1 minus, 2 plus, 3 toggle, 4 special). Unknown values are refused. No new RPC.

### Menu app (`MenuAppRules`)
- Rows: the dishes this stage may sell (stage 1: fries; stage 2: fries plus owned recipes; stage 0: every `MenuPrices`
  line), plus drinks. A row reads "Falafel wrap   $9.00   on   special" with [-] [+], on/off and special buttons.
- Price: ±$0.50 through `MenuBoardDesk.ServerStepPrice` (037's clamps); the board shows the same price. Any time; a
  guest keeps the quote from when it joined (037 fix 1).
- On/off: the 057 toggle (same rules: fries always on, `MaxDishes`, applies at the next service start).
- **Special of the day** (`SpecialRules`): none or one dish on the menu. It applies to guests who join after the change.
  Without a special every order is exactly as today (same seed, same orders). With one, a joining guest whose drawn dish
  is not on the menu takes the special instead of 057's `drawnDishId % count`, and every 4th joining guest of the
  service (join ordinal, no RNG) switches to the special. Fries guests at stage 1 never change (one dish).

### Services app (`ServicesAppRules`)
- One row per 052 channel: "Counter: open" (stage 1 and up), "Delivery", "Custom orders", "Tables", "Buffet": "comes
  with stage 3" (refused with that text). The counter cannot be switched off: it is the only service for now.
- "Shop: open / closes after this service / closed" row: the 062b door toggle (same rules).
- "Build mode" row: only while Closed; it sends the presser into build mode (the server checks as 062b's key does).
- Services and layout change only while the shop is Closed.

### Results app (`ResultsRules`, `ServiceRecord`, `ServiceHistory`)
- `ServiceRecord`: day, chosen, served, missed, earned and tips (cents), stars tenths (`GateRules.ServiceStarsTenths`),
  mean wait of served guests (s), sold per menu line (6 byte counts by `MenuPrices` line order), rival share permille
  (-1 when unknown). Recorded at each service end; the last 8 are kept, synced as `SyncList<ServiceRecord>` and saved.
- Page 1, the last service, one fact per line, a sentence or a bar, compared with the mean of up to 4 earlier services
  (within ±10 %: "about the same", else "more/fewer than usual", "quicker/slower than usual", "better/worse"):
  - "15 guests came, 12 were served. More than usual."
  - "Fries        ##########  14" (a bar of at most 10 characters, scaled to the page's largest count)
  - "Guests waited about 40 s. Quicker than usual."
  - "3.8 stars. About the same."
  - "You won 9 of 36 guests in town." (only when the rival share is known)
- Page 2: the last 5 services as one bar each (guests served), newest at the bottom. Page 3: the change log.
- Never a table: no line has more than one column of numbers, no "|" or tab characters, at most 40 characters.

### Pocket clipboard (`ClipboardRules`, `ClipboardView`)
Tab toggles a paper overlay, read-only, anywhere, from synced state: today's menu with prices and the special, the door
state, the goal line from the HUD, and page 1 of Results. Movement stays on; Tab or Esc closes it. Client only.

### Co-op
- Anyone may press anything. Spending (none in this spec; 062c and 060 use it) needs 057's two presses within 4 s.
- `ChangeLog`: the last 8 changes, synced as `SyncList<uint>` (player slot 5 bits, action 6 bits, argument 16 bits)
  and drawn as "Mira: fries $4.50", "Tobi: falafel wrap is the special", "Lu: the shop closes after this service".
  Names come from the lobby roster; an unknown slot reads "A cook". Not saved.

### Rival share
If 058 is built, the record reads its town line (yours, the rival's, outside). If 059 is built first, the field stays
-1 and the line is hidden; adding it after 058 is a short follow-up (one method, named in Implementation notes).

### Synced state and save
`OfficeTerminal`: `SyncVar<byte> Special`, `SyncList<ServiceRecord> History`, `SyncList<uint> Log`. Save: `office`
part with `special` (line id) and `history` (at most 8 records); migration fills empty defaults; clamps on load.
`ProtocolSurface.Revision` + 1; surface tests change only by these lines.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `TerminalOptions` round-trips every app, row and action; unknown values are refused.
- [ ] AC3: Menu: a terminal price step and a board step give the same price; a guest who joined before a change pays its
      quote; on/off follows 057's rules; the board shows the special tag.
- [ ] AC4: `SpecialRules`: with no special the orders of a seeded service equal today's; with a special every 4th joining
      guest switches; an off-menu draw takes the special; stage 1 is unchanged.
- [ ] AC5: Services: locked channels refuse with their text; the door row follows `ShopDoorRules`; the build row refuses
      while Open.
- [ ] AC6: `ResultsRules` literals for each comparison band; bars scale to the largest count; every line passes the
      no-table check (no "|", no tab, at most 40 characters, at most one number column).
- [ ] AC7: `ServiceHistory` keeps 8 and compares with up to 4; save round-trip; migration defaults; version 99 newer.
- [ ] AC8: `ChangeLog` packing round-trips; texts for each action; unknown slot reads "A cook".
- [ ] AC9: Surface tests change only by the three synced fields; `Revision` + 1; no new RPC.
- [ ] AC10: `--tours office`: walk to the terminal, raise fries by $0.50, make the wrap the special (stage 2 fixture), a
      service runs, Results page 1 shows the service, Tab shows the clipboard. 0 Exception lines.
- [ ] AC11: earlier tours still pass; `WhatsNew.json` entry; every string passes the D27 word test.
- [ ] AC12 (manual, human): run three services from the terminal; it reads like a note, not a spreadsheet.

## Test plan
`Tests/EditMode/`: `TerminalOptionsTests`, `MenuAppRulesTests`, `SpecialRulesTests`, `ServicesAppRulesTests`,
`ResultsRulesTests`, `ServiceHistoryTests`, `ChangeLogTests`, `ClipboardRulesTests`, updated `ShopSaveCodecTests` and
surface tests. Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours office --out .verify/visual059`.

## Human steps
AC12 play-test; answer or accept the open questions.

## Open questions (recommended default in brackets)
1. Does the 034 stock terminal move into this one? [no; cooks order stock near the kitchen. A later spec may merge them]
2. Does the menu board keep its own price buttons? [yes; both call the same server method]
3. What the special does. [off-menu draws and every 4th joining guest take it; no price change unless the crew sets one]
4. Clipboard key. [Tab, toggle; rebindable later with 048]
5. Results history length. [8 kept, 5 shown, 4 used for "usual"]
6. Who may change prices and the menu. [anyone; the log names them]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

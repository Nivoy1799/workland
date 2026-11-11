# Early Access: what is missing (goal set 2026-11-10, updated 2026-11-11)

The human's goal: "the game has early access state". This lists what an Early Access release on Steam needs, what
exists, and what is blocked by a human decision. Claude builds only approved specs; missing specs are drafted and go to
docs/human-inbox.md. Decisions marked OPEN in docs/decisions.md stay with the human.

## 1. A shop that is fun for an hour (the core loop)
- Done: specs 025–038b (stations, orders, register, stock, cleaning, tutorial), 039 (place anywhere), 041 (mouse
  controls), 042 (cup on the tray), 043 (cooking consistency), 044 (feel polish 3).
- Waiting for the human: 037 street-food prices (question 17: cents or whole dollars, D23).
- Owned by the second session: 040 (Blender models, a coherent look).
- **Missing, blocked by design v1 (question 13):** the climb (D34). Stage 1 "food corner" (1 dish, cash only), rent and
  stages 2–3, NPC rivals, a save file for the host's game. The plan in docs/game-design.md starts with a climb
  simulation in /sim. 046 (days and progression) is superseded by this.

## 2. Playing together
- Done: FishNet host and client, 045 (spam guard, late answers, restart paths, 16-player load test), 047 (title screen,
  host, join through Steam or by address, lobby, kick, rejoin, build check).
- Human: the Steam test on two PCs (003 AC2, 047 AC4).
- OPEN O10: players connect through Steam; direct IP only for LAN and dev (question 18, default yes).
- Next technical step: bandwidth headroom (049 notes: per-packet stats, 15 Hz remote players). 16 players are at 49.6 KB/s
  payload per player against a 50 KB/s target.

## 3. A shippable client
- Done: 048 settings and comfort (graphics, audio, rebinding, FOV, sensitivity, colour-blind rings), 049 release
  stability (local crash log, host tick watchdog, `scripts/perf-check.sh` with a budget and a soak). Both merge after
  the 2-hour soak on 2026-11-11.
- Open: the host network freeze trigger (the watchdog now closes cleanly), remaining GC per frame, an opt-in crash upload
  (human decision).

## 4. Online services
- Done: 006 AI gateway skeleton (strict JSON, vegan check, cache, rate limit). **Off** until the D33 go-live blockers are
  built (question 20): a login per host (O5), names built server-side, hash checks through /sim, cache protection.
- OPEN: O4 backend (mostly moot), O5 hoster for the gateway, O6 moderation approach.

## 5. Steam release (the human)
- A Steamworks app ID (the game uses 480 / Spacewar today), the store page, capsule art, trailer, price, age rating, the
  EA questionnaire, the title (O1). Claude can draft texts and checklists; the human submits.

## The critical path
1. The human approves design v1 (question 13).
2. Climb simulation in /sim, then the stage 1 food corner spec and its save file.
3. "One great service": stage 1 plus the kitchen polish, played by outside groups.
4. Stages 2–3 with rent, services and NPC rivals; the gateway go-live spec; Steam page.

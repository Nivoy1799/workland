# Steam Early Access page: DRAFT (2026-11-11)

Title: [O1, placeholder 'Worktown']

Status: draft for the human to edit. Nothing here is submitted. Parts that are not built yet are marked "planned for
Early Access updates". Source of truth for the plan: docs/game-design.md (v1, D33-D36) and docs/early-access.md. Check
every claim against the build on the day you submit.

## 1. Short description (max 300 characters)

Cook, serve and grow a restaurant together, with up to 16 friends. One player hosts through Steam. Work a hands-on
kitchen, run the register and keep the place clean. A food corner with one dish and cash only is the planned start.

(Character count to verify before submitting: about 235.)

## 2. About the game (about 250 words)

Run a restaurant with your friends. One player hosts the game, up to 15 others join through Steam, and everyone shares
one kitchen, one register and one growing business.

The work is hands-on. You prep ingredients, cook on stations, fill drinks, take orders, handle cash and keep the floor
clean. A built-in tutorial walks you through your first shift. Roles are free: nobody assigns them, the work itself
sorts out who does what.

The world is a peaceful, plant-based town. Every ingredient, recipe and dish in it is vegan, and people live together
in a relaxed, friendly place.

The long-term goal is a climb. Planned for Early Access updates: you begin at a food corner with a single dish and
cash only, then save up for rent, open your first restaurant, and add services such as delivery, custom orders and
sit-down tables. Later you hire staff, buy machines, take on rival teams in cooking competitions and serve at big
event locations. Every step costs money and asks for skill, and the place changes as you grow.

Today the kitchen, orders, register, stock and cleaning loop is playable, with a title screen, a lobby, settings
(graphics, audio, key rebinding, colour-blind aids) and a crash log. Time runs only while the game runs, so nothing
falls apart while you are away.

### Feature bullets
- Co-op for 1 to 16 players: one host, friends join through Steam. (Joining through Steam is built; the two-PC
  test is still to be done.)
- A hands-on kitchen: stations, pots, pans, drinks, a ticket rail, a register and stock ordering. Available now.
- Short tutorial and accessibility options: rebindable keys, field of view, sensitivity, colour-blind rings. Available now.
- Vegan by design: the whole food world is plant-based. Available now.
- Grow from a food corner to a restaurant with your own mix of services. Planned for Early Access updates.
- Hire staff, buy machines, compete against rival teams and serve at events. Planned for Early Access updates.
- Save your shop on the host's PC and continue later with the same friends. Planned for Early Access updates.

## 3. Early Access questionnaire (draft answers)

**Why Early Access?**
The kitchen and the co-op core are playable and stable in our tests, but the larger climb from food corner to
restaurant is still being built. We want real groups of friends to play it and tell us what is fun and what is not
before we lock the design. Their feedback decides which services and events we build first.

**Approximately how long will this game be in Early Access?**
[human decides] Suggested wording: "about 12 months, depending on feedback." Only commit to a number the team can keep.

**How is the full version planned to differ from the Early Access version?**
The full version is planned to have the complete climb: food corner, rented restaurants, a mix of services (fast food,
delivery, custom orders, sit-down tables), staff and machines, rival teams, cooking competitions and event locations,
plus more polish, models and content. Early Access will grow toward that in steps. The exact list may change with
feedback.

**What is the current state of the Early Access version?**
Playable: a shared kitchen with stations and drinks, orders, a register, stock, cleaning and a tutorial; hosting and
joining through Steam or by address; a lobby with kick and rejoin; settings; a crash log. In our tests, 16 players in
one shop ran for two hours without losing anyone. Not yet in: the food-corner start, rent, services beyond the
basic restaurant, rival teams, competitions, events and saved games. Expect rough edges and placeholder art in places.

**Will the game be priced differently during and after Early Access?**
[human decides] Price during Early Access: [human decides]. Price at 1.0: [human decides]. If the price changes, say
so plainly and in advance.

**How are you planning on involving the Community in your development process?**
Through Steam discussions and update notes. We will read feedback, publish what we plan to build next, and note which
changes came from players. [human decides] whether to add a Discord or a public roadmap. The opt-in crash report
upload is a separate decision (see checklist) and will only exist if players can switch it on themselves.

## 4. Release checklist

Owner: **Human** = needs the human's account, money, legal name or decision. **Claude** = Claude can draft or build it.

| # | Item | Owner | Notes |
|---|---|---|---|
| 1 | Steamworks partner account, fee paid, tax and bank details | Human | Needed before any app ID |
| 2 | Real Steamworks app ID | Human | The game uses test app 480 today. Claude swaps it in config once given |
| 3 | Company name in Player Settings | Human decides, Claude edits | Still "DefaultCompany", which is also part of the save path. Change it before release, with a migration or a note for existing test saves |
| 4 | Title (O1) | Human | Blocks capsule art, the page and the logo. "Worktown" is a placeholder |
| 5 | Capsule art in all Steam sizes (header, small, main, vertical, library hero and logo) | Human or artist; Claude can write the brief | Check Steamworks' current size list on the day. Needs the final title |
| 6 | Screenshots (at least 5, real in-game 1920x1080) | Claude captures with the visual tours, human picks | Take them after the models spec (040) is merged so they match the shipping look |
| 7 | Trailer (30 to 60 s, gameplay first) | Human records and edits; Claude can write the shot list | Needs the final title and a stable build |
| 8 | Age rating and content survey (Steam content form, regional ratings if asked) | Human | Claude drafts honest answers: no violence, no crime, vegan food world, user names are shown in the lobby |
| 9 | Store page text and tags | Human edits this draft; Claude updates it | Section 1 to 3 above |
| 10 | Store-page review lead time | Human | Steam reviews the page and the build before release. Plan at least one to two weeks for the review and buffer for a rejection or fix. Check Steamworks for the current time and the required "coming soon" period |
| 11 | Steam join test on two PCs (003 AC2, 047 AC4) | Human | Not yet done. Claude cannot test it |
| 12 | O1 title | Human | See row 4 |
| 13 | O10 direct IP vs Steam (default: players connect through Steam, direct IP for LAN and dev only) | Human | Question 18 |
| 14 | O5 gateway hoster, O6 moderation approach | Human | Only if the AI gateway ships. If it stays off, release without it and say nothing about AI on the page |
| 15 | AI gateway go-live blockers (login per host, server-side names, hash checks, cache protection) | Claude, after the human's go | Question 20. Disclose AI-generated text on the page if it ships (Steam asks) |
| 16 | Crash-log upload decision | Human | Today the log stays local. An opt-in upload needs a host, a privacy text and a switch in settings |
| 17 | Privacy policy and support contact | Human | Needed once any upload or account exists |
| 18 | Final build: release config, perf-check and soak, `scripts/verify.sh` green | Claude | `bash scripts/perf-check.sh`. Watch the bandwidth headroom (about 48 of 50 KB/s per player) and the rare host freeze |
| 19 | Design v1 approved (question 13) and the climb built | Human approves, Claude builds | Without the food-corner start and saved games the page must say "planned" |
| 20 | Price and release date | Human | Do not put a date on the page until the review is through |

## 5. Open points to settle while editing

- Do not claim features marked planned as existing; re-check section 2 against the build before submitting.
- The setting (docs/setting.md, a retro-futurist look) is a DRAFT and not decided. If the human adopts it (it would
  become D37), the page can describe the look; until then the text stays neutral.
- Keep the vegan wording positive ("plant-based town", "vegan food world") and never describe what is absent.
- No brand names and no comparisons with other games anywhere on the page, in tags or in the trailer text.

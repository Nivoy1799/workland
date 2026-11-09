---
id: 038b
title: Tutorial and supply polish (review follow-ups of 038)
status: approved
owner: architect
builder: opus
---

# 038b: Tutorial and supply polish

## Goal
Close the follow-ups of the spec 038 review before the human's play-test, most of all the one that would hide the
tutorial from the human. Approved by the orchestrator under the human's 24 h grant.

## Scope (files as needed; see the 038 review notes in docs/cooking-roadmap.md)
1. **Never mark the tutorial done in scripted runs.** `TutorialRunner.SaveDone`: no save when `-tour` or `-observe`
   is present (dev scripted runs). Test.
2. **No stale calm-guests flag.** CustomerService reads the local runner's state when the server starts, and the
   runner re-tells the host when it finds its player. Tests for both scenarios: a tutorial finished as a remote
   client and then hosting; Replay pressed before hosting.
3. **The engine ties PriceGoodId to GoodId.** A price good is allowed only when it is null, equals GoodId, or GoodId
   has no grocer price of its own. Sim test: `("lettuce", seitan price, …, "seitan")` is refused. Also add sim tests
   for `PriceGoodId` and `TryBuySupply` in StockAndSupplyTests, and make it `string?`.
4. **A tap pour larger than what is left** draws `min(poured, available)` and fills only that much, with the bucket
   from the drawn amount. A server-desk test.
5. **A returned cone never exceeds the line's capacity:** the overflow goes into a waiting delivery box (the 034
   mechanism). Test.
6. **Cache** the tutorial's plate lookup, and rate-limit the PlayerInputReader lookup.
7. **Tutorial step 4 text:** when the knife is in the hands, the text says to put it down first.
8. Record the scope note in spec 038.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Tests for items 1–5.
- [ ] AC3: `visual-check.sh --tours tutorial` passes, and afterwards the machine's PlayerPrefs still has no
      `worktown.tutorial.done` (or its earlier value).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

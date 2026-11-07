---
id: 014
title: Visible NPC customers at the counter
status: draft
owner: architect
builder: opus
---

# 014: Visible NPC customers

## Goal
Make the town's demand visible. Today money arrives from an invisible market (spec 008/009). With
this spec, NPC customers walk in from the street, queue at the counter with what they want above
their heads ("Meal?", "Snack?"), take a dish when a player sells one, say thanks and leave. When the
hour ends and nobody served them, they leave for an NPC restaurant instead. The economy rules do not
change: the queue only shows the sim's open demand.

## Scope
Only `game/` (branch stacked on `spec/013-plating-and-mixing`).
- `game/Assets/_Project/Scripts/Npc/` (new):
  - `CustomerRules.cs` (pure): how many customers to show per market from `MarketState.OpenDemand`
    (capped), queue slot positions, walk speed, what to do on sale / on hour change.
  - `CustomerQueue.cs` (pure): ordered queue of customer ids with their wish (market id); join,
    serve-first-matching, leave-unserved.
  - `Customer.cs` (NetworkBehaviour): server-moved NPC (capsule in a different color) with a wish
    bubble; walks along fixed waypoints; NetworkTransform server-authoritative.
  - `CustomerService.cs` (server, next to the NetworkManager): spawns/despawns customers to match
    the queue, reacts to sales from `KitchenService` and to economy ticks from `EconomyService`.
- `game/Assets/_Project/Scripts/Cooking/KitchenService.cs` (change: report a successful sale with
  its market id to `CustomerService`).
- `game/Assets/_Project/Scripts/Economy/EconomyService.cs` (change: event after each hourly tick).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: customer prefab, street entry and
  queue waypoints, service).
- `game/Assets/_Project/Resources/WhatsNew.json` (change: entry).
- `game/Assets/_Project/Tests/EditMode/CustomerTests.cs` (new).

## Out of scope
- Any economy or `/sim` change; customers never decide prices or payouts.
- Pathfinding (NavMesh), animations, models, NPC dialogue, NPC jobs or NPC businesses as places.
- Customers at anything other than the counter.

## Design
- **Who shows up:** for the item markets (meal, snack, drink), the number of visible customers is
  `min(OpenDemand, cap per market)`, total cap 6 (open question 1). Wishes: meal first, then snack,
  then drink, so the queue shows the most valuable orders.
- **Walking:** customers spawn at a street entry east of the kitchen, walk on a straight line south
  of the station row to a queue in front of the counter, and stand in queue slots 1 m apart. They
  walk away the same way. Movement is server-side at about 2 m/s; clients see it through a
  server-authoritative NetworkTransform.
- **Serving:** after `KitchenService` sells a dish to NPC demand, the first customer in the queue whose
  wish matches the sold market (else the first one) shows "Thanks! +$478" and leaves. Wholesaler
  sales serve nobody.
- **Hour change:** after each economy tick the queue is rebuilt from the new open demand: customers
  whose demand was filled by NPC businesses leave with "Too slow...", new ones walk in.
- **Network cost:** at most 6 extra network objects, within the 40 m interest radius only.

## Acceptance criteria
- [ ] AC1: `CustomerRules`/`CustomerQueue` (EditMode tests): visible count follows open demand with
      caps; wish order meal, snack, drink; serving removes the first matching customer, else the
      first; hour change keeps matching customers and removes the rest; queue slots are 1 m apart.
- [ ] AC2: In the game, customers walk in and queue at the counter; selling a dish makes one leave
      with a thanks bubble; at the next hour unserved ones leave (manual / screenshots, server log
      `[Customers] ...`).
- [ ] AC3: Spec 007-013 tests still pass; `bash scripts/verify.sh` passes with `UNITY_PATH` set;
      What's-new entry added.

## Test plan
- EditMode tests for the pure queue and rules.
- Screenshots via `-screenshot` from a client near the counter; manual selling test.

## Open questions (human decides; recommended default in brackets)
1. How many customers at most? [6 in the queue, so the counter area stays readable.]
2. Look of customers. [Same capsule as players, warm colors per wish (meal orange, snack green,
   drink blue), until an asset pack is chosen (decision O8).]
3. Street entry. [East end of the kitchen row; customers leave the same way.]

## Human steps
- Approve, answer the open questions, sell a few dishes and watch the queue.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

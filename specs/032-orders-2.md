---
id: 032
title: Orders 2.0 (order at the counter, modifiers, patience, register, tips, reviews)
status: approved
owner: architect
builder: opus
---

# 032: Orders 2.0

## Goal
Roadmap pillar 1, "orders you can read". A guest walks up and says what they want, and a ticket prints with the
exact build, for example "Falafel wrap, extra garlic, no onion". The guest waits with visible patience. You hand the
food over, take payment at the register (cash with change, or card), and get a tip for speed and accuracy. Guests
leave a short review. This turns cooking into a shop. Approved by the orchestrator under the human's 24 h grant.

## Depends on
022 (shift, orders, ticket rail), 027c (serve reward), 027d (order bubbles), 031 (recipes as layers, packaging).

## Scope
- `sim/Runtime/Economy` or `Cooking` (pure):
  - an order model: dish, modifiers (add, remove, extra), packaging (eat in or take away);
  - order matching per layer with modifiers;
  - patience model (a per-guest base plus a dish complexity term, deterministic from the seed);
  - tip formula (speed × accuracy × quality);
  - review score and text keys (the text itself comes later from the AI gateway; here it is template text).
- `game/Assets/_Project/Scripts/Npc/`:
  - the guest says the order (a speech bubble with an icon list);
  - patience shown as a small ring or bar above the guest that turns Terracotta near the end;
  - a guest leaves when patience runs out (missed);
  - the ticket rail shows modifiers in clear words ("no onion" struck through, "+ extra garlic").
- `game/Assets/_Project/Scripts/Cooking/` or a new `Shop/` folder: a register station on the counter.
  - **Cash:** the guest hands a note. You click coins and notes in the tray to give change, and the server validates.
  - **Card:** you click the terminal.
  - Wrong change costs the difference and some satisfaction.
  - Use existing RPCs only (`RequestStation` with the option as tray slot or terminal). No new RPCs.
- `Scripts/UI/`: a shift summary that adds tips, reviews (1–5 stars with a short line), and missed orders by reason.
- Tests, visual check (the tour takes a wrap order with one modifier, builds it, serves it, takes cash with change),
  `WhatsNew.json`, this spec.

## Out of scope
- AI-generated review text (the AI gateway spec later; here, template lines).
- Delivery, tables, buffets (docs/ideas.md).
- Price setting by the player (037).

## Design notes
- Modifiers come only from a recipe's allowed list:
  - "no X" for a removable salad or sauce layer;
  - "extra X" for sauces and fillings;
  - at most 2 per order at the start of the game.
- Patience (default): 60 s plus 15 s per layer, shown as the ring. The guest's mood changes the review, not the tip
  formula.
- Tip: up to 30 % of the price. It falls with time past half the patience, with modifier mistakes, and with low
  quality.
- Feel:
  - the register is one click per coin with a satisfying clink;
  - card payment is a single click with a beep;
  - the guest takes the food with a small hand animation and a thank-you bubble.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: order generation from seed, modifier matching, patience, tip, review score, determinism.
- [ ] AC3: Server tests: the register validates change, card payment, wrong change penalty, a guest leaves on
      patience timeout.
- [ ] AC4: Visual check: the ticket with modifiers is readable; the patience ring drains; cash with change; the
      review shows in the summary.
- [ ] AC5: `WhatsNew.json` entry `spec-032`.

## Defaults to review
Patience 60 s + 15 s per layer; 2 modifiers maximum; a 30 % tip cap; a 50/50 cash/card split; coin and note values
(5, 10, 20, 50 notes; 1, 2 coins).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

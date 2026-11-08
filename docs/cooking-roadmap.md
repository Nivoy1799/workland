# Cooking roadmap ("Cooking 2.0")

Goal set by the human on 2026-11-08: cooking that can stand next to the best fast-food and kebab-shop
simulators. Today it covers about 5 % of what those games do. This roadmap lists the genre mechanics we
need, in our own way: an original, vegan (D27) street-food shop inside a multiplayer town. We take
**mechanics** from the genre, never names, art, levels or recipes of an existing game.

## What makes the genre fun (pillars)
1. **Orders you can read.** A customer asks for a specific build ("wrap, extra garlic sauce, no onion"),
   has visible patience, pays at the register, tips for speed and accuracy.
2. **Stations with timing.** Fryer, griddle, spit, oven: each has a doneness window you can see and hear
   (raw, cooked, perfect, burnt) and a hands-on action (shake the basket, flip, shave, toast).
3. **Assembly in order.** Bread, then filling, salad, sauce, then wrap or box. The build stacks visibly;
   wrong order or missing parts lowers the score.
4. **Prep and stock.** Chop in bulk into containers that hold portions; containers run empty; stock is
   bought, delivered in boxes and unpacked onto shelves; money goes out as well as in.
5. **A clean shop.** Dirty trays, trash, spills; a hygiene score customers notice.
6. **Running a business.** Open and close, set prices, buy equipment, unlock menu items, grow a rating.
7. **Feel.** Sizzle, steam, crisp UI, clear highlights, short onboarding for the first shift.
8. **Our own edge.** Co-op in one kitchen (roles, shared ticket rail) and the town economy around the shop
   (suppliers and other professions, docs/economy-and-professions.md).

## Menu direction (vegan street food)
Signature: a **seitan spit** (vertical rotisserie, shaved into a flatbread or wrap), **falafel** from the
fryer, **fries**, **grilled vegetables and seitan strips**, salads, two or three vegan sauces (garlic-herb,
chili, tahini), drinks (lemonade, iced tea, soda). Existing dishes (soups, salad, fried apple) stay as sides.

## Spec queue (in order)
| Spec | Title | Pillar |
|---|---|---|
| 026 | Scale and palette pass (in progress) | look |
| 027 | Visual check tool: scripted tour and walking frames without window focus | quality |
| 028 | Street-food ingredients and content audit (flatbread, wrap, seitan, falafel mix, lettuce, cucumber, sauces; rename `cheese`; forbidden-word test) | 1, D27 |
| 029 | Deep fryer: basket, oil, timer and browning, shake, drain, burn (fries, falafel) | 2 |
| 030 | Griddle with flipping: two-sided doneness (seitan strips, vegetables, flatbread toasting) | 2 |
| 031 | Assembly station and packaging: ordered builds, visual stack, wrap paper, box, tray | 3 |
| 032 | Orders 2.0: build orders with modifiers, patience bars, register (cash with change or card), tips, reviews | 1 |
| 033 | Drinks: cups, dispenser with fill timing, lids | 1, 3 |
| 034 | Stock and supply: portion containers, fridge and shelves, ordering terminal, delivery boxes, costs | 4 |
| 035 | Cleaning: trays and plates come back dirty, sink, trash, spills, hygiene score | 5 |
| 036 | Seitan spit: rotating, outer layer cooks, shave slices with the knife | 2, signature |
| 037 | Business: price menu, equipment shop and upgrades, shop rating unlocks items | 6 |
| 038 | First-shift onboarding (guided tutorial) | 7 |

Each spec follows docs/art-direction.md (sizes, palette, measured animation) and the full pipeline:
builder, tester, reviewer, visual check, verify.sh. Numbers such as prices, timings and patience are
**provisional defaults** chosen by the orchestrator, listed in each spec under "Defaults to review", and
tuned later by the human.

# Professions and economy plan

Status: **plan, not a spec.** Each part below becomes its own spec (with a number, approval and
acceptance criteria) when the human picks it. Written 2026-11-07 from the human's wishes.

## World rule: native vegan (decided, D27)
- Worktown is a **native vegan world**. No animal products exist as goods, ingredients, recipes,
  prices, items or generated text. Nothing else.
- The world is **post non-vegan**: society remembers a dark past era when people farmed and killed
  animals. Today people and animals live together; animals are free neighbours in the town, not
  goods. The past is part of the culture: NPCs talk about it, schools teach it, people joke about the
  absurd things "the old ones" did (dry, self-aware humour about the past, never jokes at the
  animals' expense).
- The past is told only through the game's own fictional history (D28): no names of or comparisons
  to real atrocities.
- Consequences for content and AI:
  - The AI gateway's schemas and moderation reject animal-product names and descriptions; the
    prompt rules say the world is vegan (AGENTS.md AI guardrails, `ai-gateway/schemas`).
  - Content audit (first spec of this plan): the sim's grocer list still has `cheese` (rename to
    plant-based, e.g. "cashew cheese"), and the egg model is the potato stand-in (a stand-in shape is
    fine, but no item may be called or look like an egg).

## Design principles for the economy
1. **Every good has a chain**: who makes it, from what, who buys it. Nothing appears from nowhere
   except the town's raw sources (fields, water, sunlight, imports via a port).
2. **Every business can be run by an NPC or a player.** NPC businesses keep the town working with
   few players; a player can rent or buy one (see docs/ideas.md, restaurants) and does better
   through skill, speed and good prices.
3. **Money is conserved in the sim.** Sources: NPC households' wages from NPC jobs, the town's export
   sales. Sinks: rent, utilities, supplier imports, taxes, repairs, wear. The sim checks that money
   only moves (unit tests on a full simulated day).
4. **Byproducts feed other jobs** (AGENTS.md): food scraps → compost → farms; okara from tofu →
   bakery and compost; worn parts → recycling → parts supplier.
5. **Server authority:** prices, stock, quality and pay are computed by /sim on the server; players
   set prices only within server-checked bounds.
6. **Speed and skill pay, but not unfairly:** faster work means more jobs per hour, not a higher
   price per job; quality (stars) adds tips and reputation.

## Professions (first set)
| Profession | Makes / does | Needs (inputs) | Sells to | Byproduct |
|---|---|---|---|---|
| Farmer (NPC first) | vegetables, fruit, soy, grains, herbs | seeds, compost, water, tools | wholesaler, grocer, tofu maker | crop waste → compost |
| Wholesaler / supplier (NPC) | bulk goods, imports (rice, spices, noodles, soy sauce) | farm goods, port imports | shops, restaurants | packaging → recycling |
| **Grocer (shop)** | sells groceries to households and cooks | stock ordered from suppliers | NPC households, players, restaurants | spoiled goods → compost |
| **Tofu maker** | tofu, smoked tofu, soy milk, tempeh | soybeans, water, coagulant | grocer, Asia store, restaurants | okara → bakery, compost |
| **Asia store** | specialised goods (noodles, miso, kimchi, spices) | imports + own production (kimchi, dumplings) | households, restaurants | scraps → compost |
| Baker | bread, buns, pastries | flour (miller), okara, plant milk | households, cafés, restaurants | crumbs → compost |
| **Cook / restaurant** (today's kitchen) | dishes | groceries, specialised goods | customers (counter, table, delivery, buffet) | food scraps → compost |
| **Mechanic (cars / bikes)** | repairs, maintenance | parts from the parts supplier, tools | vehicle owners: delivery riders, shops, households | worn parts → recycling |
| Delivery rider | brings orders and supplies around town | a bike or cargo bike (wears out → mechanic) | restaurants, shops, suppliers | — |
| Composter / recycler | compost, recycled material | scraps, packaging, worn parts | farmers, parts supplier | — |
| Hairdresser (later) | haircuts | products from a supplier | townspeople | hair → compost |
| Animal sanctuary carer (later, flavour) | cares for the town's free animals, visitors and donations | plant feed from farmers | visitors (donations) | — |

## How the money and goods move (one day)
```
port imports ─┐
farms ────────┼─► wholesaler ─► grocer ──────► households (NPC wages)
              │            └──► Asia store ─► households / restaurants
              └─► tofu maker ─► grocer, Asia store, restaurants
restaurants ◄── groceries + specialised goods;  restaurants ─► customers (pay)
delivery riders carry orders and stock;  their bikes ─► mechanic ◄── parts supplier
all scraps ─► composter ─► farms
```

## Core mechanics per business type (shared building blocks)
- **Stock and reorder (shops, restaurants):** each item has stock, a reorder point and a supplier
  with a lead time (in game hours) and a minimum order. The owner orders refills (or sets automatic
  reorder). Running out loses sales; overstock spoils (fresh goods have a shelf life).
- **Pricing:** the owner sets a price per item within bounds (for example 50 % to 200 % of the
  reference price). NPC demand reacts: demand = base × (reference price / price)^elasticity, plus
  reputation. The sim exposes coarse feedback ("customers find this expensive"), not exact curves (D21).
- **Production (tofu maker, Asia store, baker):** hands-on mini-processes like the kitchen
  (soak → grind → cook → press for tofu; ferment for kimchi over in-game days), quality from
  the deterministic sim, never from client data.
- **Repairs (mechanic):** a vehicle arrives with a hidden fault; diagnose (inspect parts), order or
  take the part, replace it hands-on. Fixed price per job type; faster work = more jobs per shift.
  A botched repair comes back (reputation loss). Parts come from the parts supplier.
- **Rent, upkeep, wages:** businesses pay rent and utilities per day; a player owner can hire NPC
  staff (wage per hour) or work themselves.
- **Reputation:** per business, from stars and service; affects demand.

## Suggested spec order (after the overnight specs 021 to 024)
1. **World rule and content audit** (rename `cheese`, AI gateway vegan rule, NPC lines about the past).
2. **Grocery shop** (shelves, stock, NPC shoppers, reorder from a supplier, price setting).
3. **Tofu maker** (first production chain: soybeans → tofu, okara byproduct) feeding the grocer and kitchen.
4. **Restaurant ownership and service types** (docs/ideas.md).
5. **Mechanic workshop** (bikes first: flat tyre, chain, brakes; then cars).
6. **Delivery rider** (orders and stock moved by players on bikes; bikes wear → mechanic).
7. **Asia store** (imports + kimchi/dumplings production), baker, composter.

## Open
- Prices, shelf lives and lead times: numbers come with each spec and are balanced with load and play
  tests.

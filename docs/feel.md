# Feel standard (how Worktown should feel to play)

The counterpart to docs/art-direction.md, for movement, interaction and cooking. Written on 2026-11-08
after the human said: "everything feels off: how you move, how you interact, how you cook". Smoothness is
the holy grail. Each rule is concrete enough to measure or to see in the visual check (spec 027). The final
judgement stays with the human, through A/B choices in the tuning panel (no vocabulary needed: "A or B?").

## 1. Every input answers at once
- Something visible or audible happens within **100 ms** of every click or key press. Normally the same
  frame (aim highlight, hand reach, click sound).
- **Never wait for the server to show it.** The client predicts the visual (the pan flies to the hand right
  away); the server confirms. If the server says no, the item flies back with a short "nope" sound. Rules and
  money stay server-side (AGENTS rule 6); only the look is predicted.

## 2. Nothing teleports
- Pick up: a 0.12–0.18 s ease-out arc into the hand. Place: a 0.12 s ease to the spot, a small settle and a
  "tock" sound that fits the material (wood, metal, ceramic).
- Food into a pot or pan drops in with a short splash or sizzle. A button press visibly sinks and clicks.

## 3. Aiming is forgiving
- The target is the best interactable within a small cone around the crosshair (about 6°), not only what the
  exact ray hits. Ties prefer the object that matches what you hold (a potato in hand prefers the pot).
- **Sticky target:** the current target stays until another one is clearly better. No flicker between two
  objects.
- Clear highlight (outline in the Interact colour) plus a short prompt: "Left click: put into pot".
- Reach about 2 m from the body. Out of reach shows the target greyed with "Too far", never nothing.

## 4. Placing shows where things land
- A ghost preview at the landing spot while you aim at a surface with something in hand.
- Spots fit the object (a pan takes a burner, a plate takes a plate spot), not a coarse grid that looks random.

## 5. Moving
- Full speed in 0.1–0.15 s, stop in 0.08–0.12 s. No slide after release, no sluggish start.
- First person: camera locked to the eyes (no lag), horizontal FOV about 90° (vertical 70°), no mouse
  smoothing, at most a very soft head bob (off by default).
- Third person: small follow damping (at most 0.1 s), camera never clips into walls or counters.
- Animation matches speed (spec 025): feet planted, cadence from the speed.

## 6. The shop layout works like a real one
- **The counter separates the kitchen (cook side) from the guests (street side).** Guests queue on the street
  side and face the cook across the counter. The cook never walks among the guests.
- **Every control faces the cook:** stove knobs and buttons on the front (cook side), crates open towards
  the cook.
- One dish never needs more than 3–4 steps between its stations (work triangle: stock, prep, heat, assembly,
  counter). Aisles at least 1.2 m wide. Worktops at 0.90 m (D29).

## 7. Cooking reads at a glance
- Every item that changes over time shows progress: colour change on the food, plus a small ring or bar over
  it while it is on heat or being cut.
- Sound follows state: the sizzle grows with heat; a clear cue at "perfect"; a warning 3 s before burning;
  a distinct sound and a puff of smoke when it burns.
- Serving gives a reward moment: stars pop up, a coin sound, the tip shown.

## 8. Controls are consistent
- Left click does the obvious action on the target (pick up, put, press, cut). Right click is the secondary
  action (put back, rotate, drop). Hold left for continuous actions (chopping, filling a cup).
- The same action uses the same input everywhere; the prompt always names it.

## 9. How it is checked
- Numbers above are constants in code with tests (`FeelRules` style), and visible in the spec 027 contact
  sheets (frames every 0.1 s show the arcs, highlights and progress rings).
- **Tuning panel (F2, dev builds):** live sliders for the feel numbers, plus "Preset A / B" buttons so the
  human can compare and pick. The picked values are saved to a file and committed as the new defaults.

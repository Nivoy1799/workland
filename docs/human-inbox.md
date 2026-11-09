# Inbox for the human

Hop in whenever you like. Answer by writing under a question (or just tell Claude in chat). Claude never waits:
every question has a default, and work continues on other items until you answer. Newest first.

## Status (updated 2026-11-09 02:40)
- **Running:** spec 027a (shop floor plan), builder working.
- **Next:** 027b feel pass 1 (movement and interaction, F2 panel with presets A/B), 027c feel pass 2 (cooking
  feedback), 027d world labels and customer looks. Then the cooking stations (028+). See docs/cooking-roadmap.md.
- **Feel standard** (what "feels good" means, in measurable rules): docs/feel.md.
- **Merged since you left:**
  - **Spec 027, visual check tool:** `bash scripts/visual-check.sh --tours look,walk,cook` makes contact sheets
    (frames every 0.1 s, walking seen from the side, the kitchen, the cook demo) without window focus. Its first run
    found 9 problems; each one is now in a roadmap spec.
  - **Spec 026, sizes and palette:**
    - every model is at real size (person 1.75 m, worktops 0.90 m, counter 1.05 m, food 1.5x for readability);
    - houses are moved so they don't overlap;
    - worktops are built from 0.6 m cabinet modules;
    - the warm palette is used everywhere, with tests that block stray sizes and colours;
    - the review found and fixed an awning stuck inside the shop and a lamp in a house.
    - Play-test (AC7) is open for you.
  - Clean-up: old Mini Characters and unused Kenney clips removed.

## Open questions
2. **D21 extension for cooking feedback (spec 027c).** D21 says clients only see food looks, names and stars.
   Progress rings need two small extra values per pot or pan: how far along it is, and which zone (raw, cooking,
   good, perfect, overdone, burnt). They reveal no recipe stats. Option A: allow it. Option B: keep D21 strict, so
   rings would have to guess on the client. **Default: A.** Blocks nothing; easy to revert.
1. **WSL test server: network MTU is 1280 again** (Tailscale). Clients connect to the WSL dedicated server but
   nothing spawns (the spec 003 issue: FishNet's Tugboat sends packets up to 1350 bytes that must not be split).
   Option A: you run `sudo ip link set dev eth0 mtu 1500` in WSL (or turn Tailscale off) when you want WSL tests.
   Option B: nothing. **Default: B**, Claude tests with a local Windows host instead. Blocks nothing.
   Real risk for players on VPN paths with the Tugboat transport; it goes on the netcode list (spec 004 load test).

## Things to try when you have 5 minutes
- After 027b lands: open the game, press **F2**, switch between **Preset A** and **Preset B** while walking and
  picking things up, and write here which one feels better (or just "A" / "B"). No explanation needed.

## Answered
- 2026-11-08: 24 h autonomy granted; CC0 downloads from Kenney/Quaternius OK; old Mini Characters may be deleted;
  feel comes first ("counter on the wrong side, buttons on the back, everything feels off").

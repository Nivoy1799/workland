# Inbox for the human

Hop in whenever you like. Answer by writing under a question (or just tell Claude in chat). Claude never waits:
every question has a default, and work continues on other items until you answer. Newest first.

## Status (updated 2026-11-09 00:40)
- **Running:** spec 027 (visual check tool), builder working.
- **Next:** 027a shop floor plan (spec written: specs/027a-shop-floor-plan.md), 027b feel pass 1 (movement
  and interaction), 027c feel pass 2 (cooking feedback). Then the cooking stations (028+). See docs/cooking-roadmap.md.
- **Feel standard** (what "feels good" means, in measurable rules): docs/feel.md.
- **Merged since you left:**
  - **Spec 026, sizes and palette:**
    - every model is at real size (person 1.75 m, worktops 0.90 m, counter 1.05 m, food 1.5x for readability);
    - houses are moved so they don't overlap;
    - worktops are built from 0.6 m cabinet modules;
    - the warm palette is used everywhere, with tests that block stray sizes and colours;
    - the review found and fixed an awning stuck inside the shop and a lamp in a house.
    - Play-test (AC7) is open for you.
  - Clean-up: old Mini Characters and unused Kenney clips removed.

## Open questions
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

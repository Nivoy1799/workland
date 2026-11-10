---
id: 045
title: Stability and netcode hardening (crash on quit, MTU, spam, restarts)
status: approved
owner: architect
builder: opus
---

# 045: Stability and netcode hardening

## Goal
Collect the stability items from the inbox and the netcode list:
- **D3D12 crash on quit:** investigate the driver and the PSO cache, try a D3D11 default or a clean shutdown order,
  and report the result.
- **Tugboat on paths with MTU 1280** (VPN, Tailscale): make it work (FishNet MTU setting or fragmentation), or document
  Steam transport as the fix, tested against WSL at MTU 1280.
- **Spam:** disconnect a client after many TooFast replies in a row.
- **Late answers:** a late refusal after a server stall over 1 s must not bounce a newer prediction.
- **Restart paths:** a scene-plate reset to its start spots; a partly used cone handled.

Then run spec 004 (bot load test: 32 bots, 500 objects) on today's game and write the result.

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: Tests for spam disconnect, late answers, restart paths.
- [ ] AC3: The WSL server at MTU 1280 spawns players (or a documented decision).
- [ ] AC4: A crash-on-quit report with a fix or a workaround.
- [ ] AC5: The spec 004 load-test numbers in docs/loadtest-results.md.

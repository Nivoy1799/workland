---
id: 004
title: Bot load test, 32 bots and 500 networked objects
status: draft
owner: architect
builder: claude
---

# 004: Bot load test

## Goal
Measure whether FishNet and our server setup carry a "larger server" before we build
gameplay on top. Produces numbers that decide the Early Access player cap (decision O7).

## Scope
- `game/Assets/_Project/Scripts/Bots/` (new): bot mode for the client build.
- `tools/bots/run-loadtest.sh` (new): starts the server and N headless bot clients locally or on a test box.
- `tools/bots/collect-metrics` (new): writes `.verify/loadtest.json`.
- `game/Assets/_Project/Scenes/LoadTest.unity` (new): graybox with 500 pickup objects.

## Out of scope
- Steam transport for bots (bots use Tugboat, Steam would need N accounts).
- Optimizing anything. This spec only measures. Optimizations get their own spec.

## Design
- Client build started with `-batchmode -nographics -bot -connect <ip:port> -seed <n>`.
- Bot behavior (seeded): walk random waypoints, every 5 to 15 s try to pick up the nearest object,
  carry it 10 to 30 s, drop it.
- Server records every second: tick duration (avg, p95, max), outbound bytes per client,
  inbound bytes total, connected clients, CPU and RAM of the server process.
- Test run: ramp 1 to 32 bots over 2 minutes, then hold 32 bots for 10 minutes.

## Acceptance criteria
- [ ] AC1: `tools/bots/run-loadtest.sh --bots 32 --minutes 10` runs unattended and writes `.verify/loadtest.json`.
- [ ] AC2: The JSON contains per-second samples and a summary (p95 tick time, avg and max bytes/s per client).
- [ ] AC3: Report in `docs/loadtest-results.md` with the numbers, server hardware, and a chart.
- [ ] AC4 (target, not a hard fail): server tick p95 under 20 ms at 30 Hz, server outbound
      under 50 KB/s per client. If missed, the report lists the top 3 suspected causes.

## Test plan
- Run once at 8, 16 and 32 bots. Compare.

## Human steps
- Provide a Linux test box (a VM on Proxmox is fine) and note its specs in the report.
- Decide O7 (Early Access player cap) based on the report.

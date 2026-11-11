# /tools/bots

**Purpose:** headless test clients and scripts for load tests. Bots are the development client build started with
`-batchmode -nographics -connect <ip:port> -bot -seed <n> [-metrics <file>]`, connecting via Tugboat (spec 004, built in
spec 045: `Scripts/Dev/LoadTestRunner.cs`, `BotBrain.cs`). A host or server started with `-metrics <file>` writes one JSON
line per second (clients, objects, tick and frame times, bytes received, CPU, memory).

**Typically built by:** Claude (networking).

**How to run:** `bash tools/bots/run-loadtest.sh --bots 16 --ramp 120 --hold 300` (Git Bash on Windows, after
`WorktownBuild.BuildAll`). It starts one windowed host and the bots, cuts out the hold phase and writes
`<out>/summary.json` and `.verify/loadtest.json`. Results go to `docs/loadtest-results.md`.

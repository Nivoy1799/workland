# /tools/bots

**Purpose:** headless test clients and scripts for load tests. Bots are the client build
started with `-batchmode -nographics -bot -connect <ip:port> -seed <n>`, connecting via Tugboat.
Results go to `.verify/loadtest.json` and `docs/loadtest-results.md` (spec 004).

**Typically built by:** Claude (networking).

**How to test:** `tools/bots/run-loadtest.sh --bots 8 --minutes 1` once spec 004 is built.

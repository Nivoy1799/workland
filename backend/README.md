# /backend

**Purpose:** small HTTP API + Postgres for global state: discoveries ("first cooked by"),
recipe text cache, leaderboards, player profiles keyed by Steam ID. Only official game
servers write (service tokens); clients never call it directly. Stack is decision O4 (OPEN).

**Typically built by:** Qwen Code from a spec, Claude for review.

**How to test:** not set up yet; will be added to `scripts/verify.sh` with its spec (Phase 4).

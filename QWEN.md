# QWEN.md

@AGENTS.md

If file imports are not supported in your environment, read AGENTS.md in the repo root
before doing anything else. It contains all project rules.

## Qwen Code specifics

- Your role in this repo is **Builder**: implement exactly one approved spec at a time.
- Work in a branch named `spec/<id>-<short-name>`.
- Do not change specs, AGENTS.md, CLAUDE.md or anything in /docs.
- Run `scripts/verify.sh` before you say you are done. Paste only the summary line.
- After 3 failed attempts on the same problem, stop and write a `## Blockers` note
  in the spec (see AGENTS.md, Escalation). A Claude session will take over.
- Out of scope for you: anything in /game that touches networking (FishNet),
  and architecture changes. Those stay with Claude.

# Workflow

## The loop for every feature

```
Human idea (5 to 10 lines)
      │
      ▼
Architect (Claude, opus) ──► specs/NNN-name.md  status: draft
      │
      ▼
HUMAN approves (sets status: approved)   ◄── the most important human step
      │
      ▼
Builder (subagent, model from the spec) ──► branch spec/NNN-name, verify.sh green
      │
      ▼
Tester (Claude, sonnet) ──► every acceptance criterion PASS with evidence
      │
      ▼
Reviewer (Claude, opus, fresh context) ──► APPROVE / CHANGES REQUESTED
      │
      ▼
HUMAN plays it 5 minutes ──► orchestrator merges to main the same day
```

## Sessions and branches (decided by the human, 2026-11-07)

- **One orchestrator.** A single Claude Code session on the Windows PC (Unity, WSL server, builds)
  is the main agent. It is the only one that numbers specs, writes to `main`, reviews and merges.
  The human reaches it from the phone via Remote Control. All other sessions (the Mac, cloud
  sessions, older side sessions) are frozen unless the orchestrator hands them a task.
- **One spec at a time, merged right away.** Each spec gets a short branch `spec/NNN-name` from the
  current `main`. When verify.sh is green, the reviewer approved and the human played it, the
  orchestrator merges it with `git merge --no-ff` (one merge commit per spec) and pushes. No stacked
  branches; `main` is always green.
- **Parallel work only for independent folders.** For example `/ai-gateway` or `/backend` while the
  orchestrator works in `/game`. The orchestrator starts it as a side session in its own worktree and
  branch, with one spec. It never touches files another session touches. The orchestrator reviews
  and merges it. At most one or two at a time.
- **The Mac (optional).** If used again, it gets one self-contained spec from the orchestrator and
  works on its own branch, never directly on `main`.
- **The human:** approves specs and OPEN decisions, play-tests, and does what only a human can
  (Steam on two PCs, art downloads if wanted, SSH push approvals).

## Who builds what

Claude models through the Claude subscription, plus other models as builders (the human lifted the
Anthropic-only rule on 2026-11-13): the free local builder (see "Local builder" below) and an API builder
(a stronger hosted model, for cards too big for the local one). Opus reviews every result; netcode stays
with Opus. Pick the cheapest option that can do the job; the spec's `builder:`
field says which one (`local` is allowed for small, clearly specified parts).

| Work | Agent | Model |
|---|---|---|
| Data files (ingredients, items), READMEs, renames, trivial edits | builder | haiku |
| Running `verify.sh` and reporting the summary | tester | haiku |
| /sim logic and unit tests from a clear spec | builder | sonnet |
| /infra manifests, Ansible | builder | sonnet |
| UI wiring, simple MonoBehaviours | builder | sonnet |
| Writing tests, checking acceptance criteria | tester | sonnet |
| Anything with FishNet / networking | main session or builder | opus |
| Debugging across many files | main session | opus |
| Specs, architecture | architect | opus |
| Reviews | reviewer | opus |
| Task failed 3 times on a cheaper model | main session | opus (escalation) |

### Local builder (Windows dev PC only, since 2026-11-12)

Small, clearly specified implementation tasks can run on a local model instead of a
subagent: Qwen3.8-27B (Q5) in llama-server, driven by the Claude Code CLI in headless mode.
It reads `CLAUDE.md` and `AGENTS.md` like any agent here. It costs no subscription usage
but is slow: about 6 tokens/s writing, about 4 minutes for a small two-file rename.

**Setup.** Once per boot: `C:\llama\start-qwen.ps1` (model on port 9931, 262k context).
`local-build.ps1` starts the router (port 4100) itself and fails fast if the model is down.

**Run.** From Bash, always with `run_in_background`:
`powershell -File C:\Users\jovin\tools\claude-router\local-build.ps1 -Task "<task card>"`
Options: `-ReadOnly` (questions only), `-Dir <worktree>` (default: the main checkout),
`-MaxTurns 40`. It prints the model's report and `git status` before and after; the full
JSON goes to `local-build-last.json` next to the script.

**What it can and cannot do.** Read, Grep, Glob, Edit, Write. No shell, no tests, no git,
no Unity MCP, no web. So it never commits; the orchestrator does after review.

**How the orchestrator spends its budget (Opus plans, Qwen types):**
1. Write a task card instead of code. One card is one to three files and under ~200 changed
   lines. It names: the goal in one sentence, the exact files, the type and method
   signatures, the rules with their numbers, the tests to add (names and cases), and what
   must not be touched. The more exact the card, the better the local result.
2. Start it in the background and end the turn. The finish notice wakes you; waiting costs
   no tokens. Never poll, never re-read the output file while it runs.
3. Review cheaply: `git diff` of the named files only, then `scripts/verify.sh`.
4. Red? Send one fix card with only the failing lines (no full logs). At most two local fix
   rounds, then hand the part to the Sonnet builder (or Opus for netcode).
5. One local run at a time: the server has one slot (`-np 1`) and one GPU. Never start two
   in the same checkout. Parallel local runs would need separate worktrees and more slots,
   which is not set up or measured yet (OPEN: try `-np 2 --kv-unified`).
6. While it runs, the orchestrator can let Sonnet subagents work on other folders, but
   nothing may touch the files on the local card until it is reviewed.

**Good fits:** renames, data files (ingredients, items), small `/sim` functions with the
tests spelled out, test files from a list of cases, doc comments. **Not for:** FishNet and
netcode, Unity scenes or prefabs, debugging across many files, anything vague.

**Note:** desktop-app sessions cannot route their own `haiku` agents to the local model
(the app forces its own API address), so this script is the only route. Never put
`ANTHROPIC_*` variables into user settings: a running session keeps them and its haiku
agents and web search break until it is restarted.

### API builder (since 2026-11-13, not tried yet)

The same idea with a stronger hosted model: DeepSeek through its Anthropic-format endpoint
(`https://api.deepseek.com/anthropic`). It costs API money, not subscription usage, and is far
faster than the local model, so it can take Sonnet-sized cards (several files in `/sim` or the UI).

**Setup (the human, once).** Create `C:\Users\jovin\tools\claude-router\api-builder.env` with
`API_BUILDER_KEY=<key>` and optionally `API_BUILDER_MODEL=<model name>` (default `deepseek-v4-pro`;
an unknown name silently falls back to DeepSeek's small model, so check DeepSeek's Claude Code page).
The file stays outside the repo; never print or commit it.

**Run.** Like the local builder, in the background:
`powershell -File C:\Users\jovin\tools\claude-router\api-build.ps1 -Dir <worktree> -Task "<task card>"`
(`-MaxTurns 60` by default). Live view: `watch-local.ps1 -Api`. Another provider with an
Anthropic-format endpoint gets its own file, e.g. `minimax.env` with
`API_BUILDER_BASE_URL=https://api.minimax.io/anthropic` and `API_BUILDER_MODEL=MiniMax-M3`, run with `-Config minimax.env`.

**What it can do.** Read, Grep, Glob, Edit, Write, and `dotnet build` / `dotnet test` only (no git,
no other commands, no web, no Unity). Same card rules, same review, same "netcode stays with Opus".
Start with one card to calibrate, as with the local builder.

## Running the builder

1. `git checkout -b spec/NNN-name`
2. In Claude Code: `Use the builder subagent with model <builder from the spec> to implement specs/NNN-name.md.`
   The agent file defaults to sonnet; the Agent tool's `model` override picks haiku or opus.
3. Then: `Use the tester subagent on specs/NNN-name.md`, then
   `Use the reviewer subagent to review branch spec/NNN-name against specs/NNN-name.md.`

## Visual check (spec 027)
After a development build (`WorktownBuild.BuildAll`), `bash scripts/visual-check.sh [--tours look,walk,cook]
[--out .verify/visual] [--server local|wsl]` (Git Bash) runs each tour with two 1280x720 windows: the tour client
(`-connect ... -tour <name>`) and, started second so it has the focus, the observer (`-host -observe <name>` by
default, or a client of the WSL dedicated server with `--server wsl`). The tour moves through the player's normal
intents, so it needs no window focus. Results: `<out>/<tour>/sheet.png` (the tour player's own view) and
`<out>/<tour>-observer/sheet.png` (the fixed observer camera: walking frames every 0.1 s 4 m to the side, guests' view
while cooking); `sheet.txt` names each numbered cell, the logs sit next to them. Look at the sheets (agents: Read the
PNGs) before calling a visual spec done. The tours live in `Scripts/Dev/TourRules.cs` (scene coordinates); the Dev
assembly only exists in the editor and development builds.

## Perf check (spec 049)
`bash scripts/perf-check.sh` (Git Bash, after `WorktownBuild.BuildAll`) is the release-stability check: the crash log
(forced exceptions, a forced host tick stall), the profiling tour (the host's player visits every station of the scene
with 15 bots in the restaurant, 1920x1080) and a soak (memory and object counts; 15 minutes by default,
`--soak-minutes 120` for the spec's 2 hours, before a release). It appends a dated row to
`docs/performance.md`, replaces the per-station table there, and exits 1 when the budget (60 fps: frame avg and p95 at most
16.7 ms), a memory bound or the baseline (one row: the clean passing run of the same scenario with the lowest p95; more than
10 % worse) is broken. Spec 054 adds the bandwidth budget: the FishNet payload each client receives over the soak, at most
**35.0 KB/s on average and 40.0 KB/s in the worst second** (KB = 1000 bytes; 16 players); the row's "KB/s/client (worst s)"
column also records the wire bytes (no budget yet). `--net-stats` adds the per-packet table (`soak/summary.json`, `pid_*`) and
marks the row as partial (never a baseline). To find what costs the most: `bash tools/bots/run-loadtest.sh --bots 15
--net-stats` (docs/loadtest-results.md has the latest table).
- **When:** at least once per merged milestone, and for big specs the orchestrator adds it to the pipeline after the
  visual check (never at the same time as a visual check or a load test: they share port 7770; the script refuses then).
  Commit the new row with the merge (`docs: perf check [spec NNN]`).
- **Length:** the default run takes about 20 minutes; `--quick` (10-minute soak) after a change that may cost frames;
  rows are only compared with rows of the same scenario (bots, minutes, resolution). Do not poll a running check with a
  loop of greps: on Git Bash that, plus 16 game processes, once used up the process table (fork failures) mid-soak.
  `--no-record` measures without touching the doc; `--skip-soak` runs only the crash checks.
- **A FAIL:** read `.verify/perf/<run>/report.md` (why, the station table) and the logs next to it; a frame regression
  shows in the station table (worst p95 first); a growth in memory in `soak/host.jsonl` (`managed_heap_mb`,
  `unity_objects`, one line a second). Fix it or write a plan into docs/performance.md, never raise the bounds quietly.
- Crash logs stay local (`crash.log` in the player's data folder, `-crash-log path` to move it); an upload is OPEN.

## What's new entries
Every spec that changes something playable adds one entry to
`game/Assets/_Project/Resources/WhatsNew.json` as part of "done": an id ending in `spec-NNN`,
the date, a title, what is new, and what to test. The game shows the newest entry once per build
(F2 reopens it). Decided by the human on 2026-11-07 (spec 011).

## Human checkpoints

- Approve every spec. Reading 1 page saves hours of wrong code.
- Review all netcode diffs yourself. Agents make subtle authority and sync mistakes here.
- Play every playable change for 5 minutes. Tests cannot tell you if it feels good.
- Art, level layout and game feel are yours.

## Claude Max budget tips

- Specs and AGENTS.md replace re-explaining. Keep them current.
- One main Claude session at a time: the orchestrator (see above). Long histories are summarised
  automatically; specs, this file and AGENTS.md carry the context.
- Opus for architect and reviewer, sonnet for builder and tester.
- Routine building goes to Sonnet, trivial tasks to Haiku. Opus only for specs, netcode, reviews and escalations.
- When the local builder runs, small clear tasks go there first (free, slow). Opus writes the
  task card and reviews the diff; see "Local builder".
- Reviewing a diff costs far fewer tokens than writing it.

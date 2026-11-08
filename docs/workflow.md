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

Only Anthropic models, all through the Claude subscription. Pick the cheapest model that
can do the job; the spec's `builder:` field says which one.

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
- Reviewing a diff costs far fewer tokens than writing it.

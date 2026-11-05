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
HUMAN plays it 5 minutes, merges
```

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

## Human checkpoints

- Approve every spec. Reading 1 page saves hours of wrong code.
- Review all netcode diffs yourself. Agents make subtle authority and sync mistakes here.
- Play every playable change for 5 minutes. Tests cannot tell you if it feels good.
- Art, level layout and game feel are yours.

## Claude Max budget tips

- Specs and AGENTS.md replace re-explaining. Keep them current.
- One main Claude session at a time. Start a fresh session per spec.
- Opus for architect and reviewer, sonnet for builder and tester.
- Routine building goes to Sonnet, trivial tasks to Haiku. Opus only for specs, netcode, reviews and escalations.
- Reviewing a diff costs far fewer tokens than writing it.

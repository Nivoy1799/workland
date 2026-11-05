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
Builder (Qwen Code or Claude builder) ──► branch spec/NNN-name, verify.sh green
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

| Work | Builder |
|---|---|
| /sim logic with a clear spec | Qwen Code |
| Unit tests from a spec | Qwen Code |
| Data files (ingredients, items) | Qwen Code |
| /infra manifests, Ansible | Qwen Code |
| UI wiring, simple MonoBehaviours | Qwen Code |
| Anything with FishNet / networking | Claude |
| Debugging across many files | Claude |
| Architecture, specs, reviews | Claude |
| Task failed 3 times with Qwen | Claude (escalation) |

## Running Qwen Code as builder

1. `cp .env.example .env` and fill in `OPENAI_*`.
2. `git checkout -b spec/NNN-name`
3. Start Qwen Code in the repo root and give it:
   `Implement specs/NNN-name.md. Follow QWEN.md and AGENTS.md. Run scripts/verify.sh before finishing.`
4. Back in Claude Code: `Use the reviewer subagent to review branch spec/NNN-name against specs/NNN-name.md.`

## Human checkpoints

- Approve every spec. Reading 1 page saves hours of wrong code.
- Review all netcode diffs yourself. Agents make subtle authority and sync mistakes here.
- Play every playable change for 5 minutes. Tests cannot tell you if it feels good.
- Art, level layout and game feel are yours.

## Claude Max budget tips

- Specs and AGENTS.md replace re-explaining. Keep them current.
- One main Claude session at a time. Start a fresh session per spec.
- Opus for architect and reviewer, sonnet for builder and tester.
- Routine building goes to Qwen Code (paid per token, cheap).
- Reviewing a diff costs far fewer tokens than writing it.

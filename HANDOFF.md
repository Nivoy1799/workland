# Handoff to Claude Code

Paste the prompt below into Claude Code, started in the root of this repo.

---

```
You are joining a new game project as the lead coding agent.

Before doing anything else, read these files in this order:
1. AGENTS.md            (project rules, shared with all agents)
2. docs/architecture.md (system overview)
3. docs/decisions.md    (what is decided and what is still open)
4. docs/workflow.md     (how we work: spec, build, test, review)
5. tasks/phase-0.md     (your current task list)

Then:
- Summarize in max 10 bullets what you understood, and list anything that is
  unclear or contradictory. Wait for my answer before writing code.
- After I confirm, work through tasks/phase-0.md top to bottom.
- Steps marked [HUMAN] are mine. When you reach one, stop, tell me exactly
  what to do, and wait.
- Use the subagents in .claude/agents/ as described in docs/workflow.md.
- A task is only done when scripts/verify.sh passes.
- Tick off tasks in tasks/phase-0.md as you finish them and commit after
  each finished task with a clear message.
- Never invent decisions marked OPEN in docs/decisions.md. Ask me.
```

---

## What is in this kit

| Path | Purpose |
|---|---|
| `AGENTS.md` | Rules for every agent (Claude Code and Qwen Code) |
| `CLAUDE.md` | Claude Code entry point, imports AGENTS.md |
| `QWEN.md` | Qwen Code entry point, imports AGENTS.md |
| `.claude/agents/` | Architect, Builder, Tester, Reviewer subagents |
| `.claude/settings.json` | Hooks (verify before finishing) and blocked commands |
| `.mcp.json` | MCP for Unity (CoplayDev, free) |
| `docs/` | Architecture, decision log, workflow |
| `specs/` | Spec template plus the first four specs |
| `tasks/phase-0.md` | Ordered checklist with human and agent steps |
| `scripts/verify.sh` | The single "is it green?" command |
| `.env.example` | Endpoint config for Qwen Code and the AI gateway |

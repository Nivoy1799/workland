# CLAUDE.md

@AGENTS.md

## Claude Code specifics

### Your role
In this repo, the main session (Opus) is the **architect, reviewer and netcode owner**.
Routine implementation goes to the builder subagent on Sonnet, trivial edits on Haiku
(see docs/workflow.md, "Who builds what"). Netcode and hard debugging stay on Opus.
Claude models come through the user's subscription. Other models may build too (the human
lifted the Anthropic-only rule on 2026-11-13): the **local builder** (Qwen on the Windows dev PC,
free but slow) and an **API builder** (a stronger hosted model, once the human has set its key).
Small and clearly specified implementation tasks go to them first; Opus plans the task card and
reviews every diff, and netcode stays with Opus (see docs/workflow.md, "Local builder").

### Subagents (in .claude/agents/)
- `architect`: turns a short human idea into a spec in /specs. Never writes code.
- `builder`: implements one approved spec. Only touches the folders the spec names.
- `tester`: writes and runs tests, load tests, reads the Unity console via MCP.
- `reviewer`: checks a finished diff against its spec and AGENTS.md with fresh context.

Typical flow: architect, then human approval, then builder, tester, reviewer.

### Budget rules (Claude Max)
- Use plan mode for anything bigger than a one-file change.
- Do not read the whole repo. Start from the spec, then open only the files it names.
- Prefer `Grep`/`Glob` over opening many files.
- Do not paste large logs into the conversation. Summarize, or read the relevant lines.
- One main session at a time. Parallel subagents only for independent folders.

### Unity via MCP for Unity
- MCP server config is in `.mcp.json`. It only works while the Unity Editor is open
  with the MCP for Unity bridge started.
- Use MCP to read the console, inspect scenes, create GameObjects and run tests.
- Write C# scripts as files with Edit/Write, then let Unity recompile.
- Never modify `game/Library/`, `game/Temp/`, imported asset packs or FishNet sources.

### When unsure
Ask one clear question instead of guessing. Decisions marked OPEN are never yours to make.

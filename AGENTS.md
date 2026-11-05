# AGENTS.md

Shared rules for every coding agent in this repo (Claude Code main session and its subagents, on any model).
Read this fully before every task. If anything here conflicts with a spec, stop and ask.

## Project in one paragraph

Working title: **Worktown** (rename later). A multiplayer life and profession
simulator on Steam. Players live in a small hand-built town and work jobs
(cooking first, later mechanic, hairdresser, more). The town has a circular
economy: NPCs create demand, players or NPC businesses supply it, and byproducts
of one profession feed another. AI (an LLM behind our own gateway) generates
flavor content such as recipe names and descriptions. Game mechanics are always
deterministic and computed by our simulation code, never by the LLM.

## Stack (decided)

- Unity 6 (LTS), C#
- FishNet for networking, Steam via FishySteamworks / Steamworks.NET
- Dedicated, server-authoritative, headless Linux server builds
- Self-hosted: k3s + Agones on rented servers
- AI: our own gateway exposing an OpenAI-compatible API (cloud Qwen now, own GPU later)
- Backend: small API + Postgres for global state
- Official servers only (no community servers yet)
- Low-poly art from asset packs
- Editor automation: MCP for Unity (CoplayDev), not Unity AI

## Repo layout

```
/specs        one .md per feature, written and approved before code
/sim          pure C# simulation (economy, jobs, recipes). NO UnityEngine references.
/game         Unity project (client and headless server from one codebase)
/ai-gateway   OpenAI-compatible proxy: cache, schema validation, moderation
/backend      API + Postgres for global state
/infra        k3s, Agones, Ansible
/tools/bots   headless test clients for load tests
/docs         architecture, decisions, workflow
/tasks        phase checklists
/scripts      verify.sh and helpers
```

## Hard rules

1. **Work only from a spec.** No spec in `/specs` with status `approved` means no code. Ask.
2. **Stay in scope.** Only touch the folders the spec names. If you need more, stop and ask.
3. **Small steps.** One spec, a handful of files, then verify and commit.
4. **Done means green.** A task is done only when `scripts/verify.sh` passes.
5. **Never invent decisions.** Anything marked OPEN in `docs/decisions.md` needs a human answer.
6. **Server is the authority.** Clients send intents, the server validates and decides. Never trust client data for money, items, quality or position-critical logic.
7. **Determinism in /sim.** No randomness without an injected seeded RNG. No `DateTime.Now`, inject a clock. No static mutable state.
8. **No secrets in git.** Use `.env` (gitignored). Never print secret values in logs or chat.
9. **Do not edit generated or vendor code** (Unity `Library/`, imported asset packs, FishNet sources).
10. **Ask before destructive actions**: deleting files outside your scope, force pushes, rewriting history, dropping databases.

## AI content guardrails

- The LLM never decides game outcomes. The sim computes stats, the LLM only names and describes.
- Prompts contain only in-game IDs and computed properties, never free real-world chemistry, medicine or similar instructions.
- Every LLM response must validate against a JSON schema in `/ai-gateway/schemas`. Invalid output is rejected, never "fixed up" by guessing.
- Every generated text passes the moderation step before it is stored or shown to other players.
- Generated content is cached by a stable hash of its inputs. Same inputs, same result.

## C# conventions

- .NET naming: `PascalCase` types and methods, `_camelCase` private fields, `camelCase` locals.
- One public type per file, file name equals type name.
- `/sim` code is plain C#, compatible with .NET Standard 2.1, `nullable enable`.
- Prefer small pure functions and immutable records in `/sim`.
- Unity MonoBehaviours stay thin: they call into `/sim` and networking, they hold no game rules.
- Every public `/sim` function has at least one unit test.

## Escalation

If you fail to get `scripts/verify.sh` green after **3 attempts** on the same problem,
stop. Write a short note in the spec under `## Blockers` (what you tried, what failed,
your best guess) and hand back to the human or to the Claude reviewer.

## Commits

- Conventional style: `feat(sim): add ingredient stats`, `fix(net): ...`, `chore: ...`.
- One logical change per commit. Reference the spec id, e.g. `[spec 002]`.

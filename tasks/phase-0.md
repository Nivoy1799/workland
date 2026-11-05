# Phase 0 and Phase 1: Foundation and netcode

Work top to bottom. [HUMAN] steps: the agent stops, explains what to do, and waits.
Tick boxes as you go and commit after each finished task.

## Phase 0: Setup (target: 1 week)

- [x] 0.1 [AGENT] Read AGENTS.md, docs/*, this file. Summarize understanding, list questions. Wait.
- [x] 0.2 [AGENT] Implement spec 001 (repo skeleton). Verify, commit.
- [ ] 0.3 [HUMAN] Install the .NET 10 SDK on the Mac: `brew install --cask dotnet-sdk`
      (LTS; .NET 8 support ends 2026-11-10).
- [ ] 0.4 [AGENT] Implement spec 002 (/sim package) with the builder subagent (sonnet)
      in branch `spec/002-sim-package`.
- [ ] 0.5 [AGENT] Run the tester subagent, then the reviewer subagent on branch `spec/002-sim-package`.
      Fix or hand back. Merge to `main` and push.
- [ ] 0.6 [HUMAN] On the Windows PC: install Unity Hub, Unity 6 LTS with Windows, Linux and
      Linux Dedicated Server Build Support, and Claude Code. Clone the repo. Create the project
      in `/game` (see `game/README.md`), import FishNet, FishySteamworks, Steamworks.NET, MCP for Unity.
- [ ] 0.7 [AGENT] On Windows: verify the MCP connection, read the Unity console through MCP and report.

## Phase 1: Netcode (target: 2 to 3 weeks)

- [ ] 1.1 [HUMAN] Read and approve spec 003 (set `status: approved`), or request changes.
- [ ] 1.2 [AGENT] Implement spec 003 with the builder subagent (opus: netcode).
- [ ] 1.3 [AGENT] Tester and reviewer subagents on spec 003.
- [ ] 1.4 [HUMAN] Review the netcode diff. Two-PC Steam test (AC2). Merge.
- [ ] 1.5 [HUMAN] Approve spec 004. Provide a Linux test VM.
- [ ] 1.6 [AGENT] Implement spec 004, run 8 / 16 / 32 bot tests, write docs/loadtest-results.md.
- [ ] 1.7 [HUMAN] Decide O7 (player cap) in docs/decisions.md.
- [ ] 1.8 [AGENT] Draft the next specs as `draft` for the human to approve:
      005 AI gateway skeleton, 006 cooking station in game, 007 economy core in /sim.
      Do not start building them.

## Exit criteria for Phase 1
- Dedicated Linux server runs, players join via Steam, pick up objects, server-authoritative.
- Load test numbers documented, player cap decided.
- /sim test loop works: `dotnet test` on the Mac and the package loads in Unity on Windows.

# /sim

**Purpose:** pure C# simulation, the heart of the game: ingredient stats, cooking quality,
jobs, economy ticks, NPC demand. No `UnityEngine` references. .NET Standard 2.1, `nullable enable`.
Deterministic: inject a seeded RNG and a clock, no static mutable state.

**Consumers:** Unity loads it as the local UPM package `com.worktown.sim`
(`game/Packages/manifest.json`: `"file:../../sim"`), and `dotnet test` builds the same
files via `Sim.csproj`. Folders ending in `~` (tests, build output) are ignored by Unity.

**Typically built by:** builder subagent on Sonnet (routine logic from a clear spec), Opus for design-heavy parts.

**How to test:** `dotnet test sim/Sim.sln`, or `bash scripts/verify.sh` from the repo root.

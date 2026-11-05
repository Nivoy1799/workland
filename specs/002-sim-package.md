---
id: 002
title: /sim package with first ingredient model and test harness
status: approved
owner: architect
builder: qwen
---

# 002: /sim package with first ingredient model and test harness

## Goal
Set up the pure C# simulation library that both Unity and `dotnet test` use,
with a tiny first slice of the cooking domain. This gives every agent a fast,
Unity-free test loop from day one.

## Scope
- `sim/package.json` (new): UPM local package `com.worktown.sim`.
- `sim/Runtime/Worktown.Sim.asmdef` (new): `noEngineReferences: true`, `autoReferenced: true`.
- `sim/Runtime/Cooking/IngredientStats.cs` (new)
- `sim/Runtime/Cooking/Ingredient.cs` (new)
- `sim/Runtime/Cooking/DishScorer.cs` (new)
- `sim/Runtime/Core/IRandom.cs`, `sim/Runtime/Core/SeededRandom.cs` (new)
- `sim/Sim.csproj` (new): targets `netstandard2.1`, compiles `Runtime/**/*.cs`.
- `sim/Sim.Tests/Sim.Tests.csproj` (new): xUnit, targets `net8.0`, references `../Sim.csproj`.
- `sim/Sim.sln` (new): contains both projects.
- `sim/Sim.Tests/Cooking/DishScorerTests.cs` (new)

## Out of scope
- Unity project integration (done in spec 003, which adds the package to the manifest).
- Economy, jobs, AI. Only the minimal cooking model below.

## Design

```csharp
// Immutable flavor and texture profile, all values 0..1
public sealed record IngredientStats(
    float Sweet, float Salty, float Sour, float Bitter, float Umami,
    float Fat, float Crunch, float HeatTolerance);

public sealed record Ingredient(string Id, string DisplayName, IngredientStats Stats);

public sealed record DishResult(float Quality, IngredientStats Profile, string ComboHash);

public static class DishScorer
{
    // Profile = average of ingredient stats.
    // Quality 0..1 = balance score: rewards umami and fat in moderation,
    // penalizes extreme dominance of a single taste. Exact formula is the
    // builder's choice but must be documented in a comment and be monotonic
    // in the obvious cases tested below.
    // ComboHash = stable hash of the SORTED ingredient ids (order independent),
    // hex string, same on every platform (use a fixed algorithm like SHA-256, not GetHashCode).
    public static DishResult Score(IReadOnlyList<Ingredient> ingredients);
}
```

## Acceptance criteria
- [ ] AC1: `dotnet test sim` passes.
- [ ] AC2: `ComboHash` is identical for the same ingredients in any order (test).
- [ ] AC3: `ComboHash` differs for different ingredient sets (test).
- [ ] AC4: A single extreme ingredient (e.g. salt only) scores lower than a balanced
      mix of 3 complementary ingredients (test with fixed data).
- [ ] AC5: `Score` with an empty list throws `ArgumentException` (test).
- [ ] AC6: No `UnityEngine` references anywhere under `/sim` (verify with grep in a test or in verify.sh).
- [ ] AC7: `bash scripts/verify.sh` shows `PASS sim: dotnet test`.

## Test plan
- xUnit tests in `sim/Sim.Tests/Cooking/DishScorerTests.cs` with 5 to 8 hard-coded test ingredients.
- Command: `bash scripts/verify.sh`

## Human steps
- Install the .NET 8 SDK if `dotnet --version` fails.

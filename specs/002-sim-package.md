---
id: 002
title: /sim package with first ingredient model and test harness
status: done
owner: architect
builder: sonnet
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
- `sim/Runtime/Cooking/DishResult.cs` (new)
- `sim/Runtime/Cooking/DishScorer.cs` (new)
- `sim/Runtime/Core/IRandom.cs`, `sim/Runtime/Core/SeededRandom.cs` (new)
- `sim/Runtime/Core/IsExternalInit.cs` (new): polyfill, see Design.
- `sim/Directory.Build.props` (new): redirects build output, see Design.
- `sim/Sim.csproj` (new): targets `netstandard2.1`, compiles only `Runtime/**/*.cs`.
- `sim/Sim.Tests~/Sim.Tests.csproj` (new): xUnit, targets `net10.0`, references `../Sim.csproj`.
- `sim/Sim.Tests~/Cooking/DishScorerTests.cs` (new)
- `sim/Sim.sln` (new): contains both projects.

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

### Layout rules (Unity and dotnet share this folder)
Unity imports **everything** inside a `file:` package except folders ending in `~` or
starting with `.`. So:
- Tests live in `sim/Sim.Tests~/`, never in a folder Unity can see. Otherwise Unity tries
  to compile them without xUnit.
- Build output must not land in `sim/bin` or `sim/obj`, or Unity imports the built
  `Sim.dll` as a plugin next to the source and you get duplicate type errors.
  `sim/Directory.Build.props` sets, for every project under `/sim`:
  `BaseIntermediateOutputPath = $(MSBuildThisFileDirectory)Build~/obj/$(MSBuildProjectName)/`
  and `BaseOutputPath = $(MSBuildThisFileDirectory)Build~/bin/$(MSBuildProjectName)/`.
- `Sim.csproj` sets `EnableDefaultCompileItems=false` and includes only `Runtime/**/*.cs`,
  so it never picks up test files.
- `Sim.csproj`: `LangVersion` 9.0 (records; Unity 6 supports C# 9), `Nullable` enable.
- Positional records need `System.Runtime.CompilerServices.IsExternalInit`, which
  netstandard2.1 and Unity do not ship. Add it as an `internal static class` in
  `Runtime/Core/IsExternalInit.cs`.
- Unity writes `.meta` files into `/sim` once the package is linked (spec 003). Those are
  committed. Folders ending in `~` get none.

## Acceptance criteria
- [x] AC1: `dotnet test sim/Sim.sln` passes.
- [x] AC2: `ComboHash` is identical for the same ingredients in any order (test).
- [x] AC3: `ComboHash` differs for different ingredient sets (test).
- [x] AC4: A single extreme ingredient (e.g. salt only) scores lower than a balanced
      mix of 3 complementary ingredients (test with fixed data).
- [x] AC5: `Score` with an empty list throws `ArgumentException` (test).
- [x] AC6: No `UnityEngine` references anywhere under `/sim` (checked by `scripts/verify.sh` as `PASS sim: no UnityEngine references`).
- [x] AC7: `bash scripts/verify.sh` shows `PASS sim: dotnet test`.
- [x] AC8: After `dotnet test`, no `bin/` or `obj/` folder exists under `sim/` outside `sim/Build~/` and `sim/Sim.Tests~/`.

## Test plan
- xUnit tests in `sim/Sim.Tests~/Cooking/DishScorerTests.cs` with 5 to 8 hard-coded test ingredients.
- Command: `bash scripts/verify.sh`

## Human steps
- Install the .NET 10 SDK (LTS) if `dotnet --version` fails. `global.json` pins the 10.0 feature band.

## Implementation notes
- Quality = 0.5 * balance + 0.25 * umamiScore + 0.25 * fatScore. Balance rewards an even spread across the five tastes (dominance = max/sum), umami and fat score peak at 0.4. Profile is the component-wise average.
- ComboHash: SHA-256 over ordinal-sorted ingredient ids joined by '\n', lowercase hex, duplicates kept. Ingredients are sorted by Id first, so float sums and the hash are order independent.
- Validation (ArgumentException): null ingredient, null Stats, Id null/empty/containing '\n', any stat NaN, infinite or outside 0..1. Empty list throws ArgumentException, null list ArgumentNullException.
- SeededRandom uses SplitMix64; modulo bias in NextInt is accepted.
- `sim/Sim.Tests~/Core/SeededRandomTests.cs` was added beyond the listed scope because AGENTS.md requires a test for every public /sim function.
- Known limit: ingredients sharing an Id but with different Stats are summed in unstable sort order.
  Ids are unique per ingredient definition in the game, so this is accepted. Revisit if that changes.
- Review: tester PASS on AC1 to AC8; reviewer findings (order-dependent float sums, id separator,
  NaN/range validation, invariant hex) fixed; 27 tests.

---
id: 045
title: Stability and netcode hardening (crash on quit, MTU, spam, restarts)
status: approved
owner: architect
builder: opus
---

# 045: Stability and netcode hardening

## Goal
Collect the stability items from the inbox and the netcode list:
- **D3D12 crash on quit:** investigate the driver and the PSO cache, try a D3D11 default or a clean shutdown order,
  and report the result.
- **Tugboat on paths with MTU 1280** (VPN, Tailscale): make it work (FishNet MTU setting or fragmentation), or document
  Steam transport as the fix, tested against WSL at MTU 1280.
- **Spam:** disconnect a client after many TooFast replies in a row.
- **Late answers:** a late refusal after a server stall over 1 s must not bounce a newer prediction.
- **Restart paths:** a scene-plate reset to its start spots; a partly used cone handled.

Then run the spec 004 bot load test with 16 players (D36) in one restaurant on a host, and write the result.

## Acceptance criteria
- [x] AC1: `verify.sh`.
- [x] AC2: Tests for spam disconnect, late answers, restart paths.
- [x] AC3: The WSL server at MTU 1280 spawns players (or a documented decision).
- [x] AC4: A crash-on-quit report with a fix or a workaround.
- [x] AC5: The spec 004 load-test numbers in docs/loadtest-results.md.

## Implementation notes

The spec names no folders; the work stayed in `game/Assets/_Project` (Scripts, Tests), `game/ProjectSettings/ProjectSettings.asset`
(one line: the Windows graphics APIs), `scripts/visual-check.sh`, `tools/bots` and `docs/loadtest-results.md`.
Spec 004's tooling was not built (only `-bot/-seed/-metrics` flags on the parked spec/004 branch), so a minimal version is
built here: `LaunchArgs` `-bot`, `-seed`, `-metrics`; `Scripts/Dev/LoadTestRunner.cs` (bot + per-second metrics, dev builds),
`BotBrain` and `LoadStats` (pure, tested); `tools/bots/run-loadtest.sh` (host + N bots, ramp, hold, perl summary).

**Spam (AC2).** (Replaced in the 045 review, built with spec 047: a sliding window, 300 TooFast replies within 10 s; see the 047 notes.) `Net/Rules/SpamGuard` (pure): 40 TooFast replies in a row disconnect the client (`ServerManager.Kick`,
ExcessiveData, one warning line); any request that passes ends the streak; the host's own player is never kicked; reset on a
new player life. 40 because the bucket lets one request through every 0.2 s: a flood (about 200/s) trips it within a
fraction of a second, an honest client needs a ~10 s stall; a client a little too fast is only throttled. Tests: `SpamGuardTests`
(with the real `RequestBucket`: a 5 s stall burst never kicks, 1000/s does, 10/s is only throttled).

**Late answers (AC2).** Root cause: `PredictionBook` counted answers; after a quiet second it wrote older requests off, so a
late refusal after a server stall landed on the newer request. Fix: every request RPC carries its number as a last `int
request` and the three answer TargetRpcs echo it; the book matches by number (no counting, no quiet-spell reset, no "alone"
rule). Side effect (better): a refusal with two requests in flight (RTT over ~0.24 s) now applies at once instead of waiting
for the timeout. The server only echoes the number. Updated tests (same scenarios, echo API): FeelPredictionTests (one renamed:
`AnUnansweredRequest_NeverShiftsTheNextOnesAnswer`, was `DroppedRequests_AreForgottenAfterAQuietSecond`; one renamed
`SlowRoundTrip_ALateAnswerOfTheLastRequest_IsNotTheNewOnes`), KitchenPolishTwoTests; new: the 1.5 s stall test, the
two-in-flight refusal. RPC surface pins updated (+1 int named `request`, still no positions): MoveDataSurfaceTests,
HandsOnSurfaceTests (`StationRequest_TakesOnlyInts`).

**Restart paths (AC2).** Scene plates: `Cooking/SceneStartSpots<T>` (pure) records the scene plates' spots at the first server
start (sealed after); on a server stop KitchenService puts them back, so the next session starts with them on their spots.
`Pickup.OnStopServer` forgets the hold point (a scene item held at the stop never follows an old hold point). Partly used cone:
`SpitDesk.ClearReturningUntouched(tick, rate)` now keeps a partly shaved cone on its spit (portions left, its browning folded to
the stop, knob off, no hold, tray emptied); untouched cones still go back to stock (038b). This changes the 036/038 rule "a
partly shaved cone is lost": `SpitReviewTests` stop test updated to the new rule and renamed; new
`APartlyUsedCone_KeepsItsBrowningOverTheStop_...`, `SceneStartSpotsTests`. Docs in DishRoom/CleaningRoom updated.

**MTU 1280 (AC3): documented decision path.** Reproduced: the WSL Linux server (eth0 MTU 1280, set by Tailscale) spawned the
bot's player (`[Net] player 0 spawned`), but the Windows bot only received ~490 bytes/s (pings) and never saw its player.
Cause (read in FishNet, not changed): Tugboat passes a fixed `MAXIMUM_UDP_MTU = 1350` as LiteNetLib's `MtuOverride`, which turns
off MTU discovery; sockets set DontFragment and LiteNetLib's fragmentation is removed, so 1350-byte datagrams (and merged
packets up to that size) are dropped on a 1280 path. A fix in our code would need a Tugboat subclass (`GetMTU`) plus reflection
into internal `NetManager` fields: not done. Decision proposed: players use the Steam transport (FishySteamworks over Steam's
networking sockets, which segment messages themselves); Tugboat stays the LAN/dev/bot transport; on a VPN path use MTU 1500
(`sudo ip link set dev eth0 mtu 1500` in WSL) or Tailscale off. WSL and Windows network settings were not changed.

**Crash on quit (AC4).** Evidence: 5 crash dumps (`%LOCALAPPDATA%\CrashDumps`), WER: access violation in `D3D12Core.dll`
1.618.1.0 (the Agility SDK Unity ships in `Builds/Windows/D3D12`), 4 on 11-09 at offset 0x264831 (after tours), 1 on 11-10
20:59 at 0xa1f5 (not a tour run); no game code on the stack; NVIDIA driver 610.88 (unchanged). Not reproduced today with D3D12
in 8 quits (4 short host sessions closed with WM_CLOSE, 2 look tours with both windows; `.verify/quit045`). Hypothesis (not
proven): the Steam overlay (`gameoverlayrenderer64.dll` hooks D3D12) is shut down in `SteamBootstrap.OnApplicationQuit` before
Unity releases the D3D12 device. Workaround applied: Windows builds default to Direct3D 11 with D3D12 as fallback
(`ProjectSettings.asset` `WindowsStandaloneSupport` APIs `[D3D11, D3D12]`, automatic off); D3D11 is also what every visual check
has used since 11-09. `visual-check.sh --gfx d3d11|d3d12|default` (default d3d11) re-checks D3D12 when wanted. Follow-up: retry
D3D12 with a Unity or driver update, and the Steam shutdown order.

**Flaky items.**
- Host ending mid-tour without a line (`.verify/visual039b`): the host's log ends with an unhandled exception on a non-main
  thread inside FishNet (`TimeManager.get_LocalTick` NullReferenceException via the UnhandledExceptionHandler, no caller frames),
  then Steam's socket shutdown messages: the runtime ended the process. Not a D3D crash and not a kill. FishNet code; not
  changed; seen once.
- Cinemachine NRE (`.verify/visual041` cook tour): inside `UpdateTracker.UpdateTargets` (a null status in Cinemachine's static
  registry), mid-run, not at teardown; package code, no game code on the stack; seen once; not changed.
- 22 Input System NREs (`.verify/visual043` cook observer only): inside `InputManagerStateMonitors.ProcessStateChange` from
  `FastMouse.OnNextUpdate` (the action state monitors on the mouse of an unfocused window). Mitigation: scripted roles (tour,
  observer, bots) call `PlayerInputReader.DisableForScript()` (reader off, Player action map disabled), so they keep no action
  monitors on devices. Not reproducible on demand, so unproven.

**Load test (AC5).** 16 bots + the host's player in one restaurant, 2 min ramp + 5 min hold: tick p95 1.0 ms, outbound 49.6
KB/s per client (target 50: just met; grows ~3 KB/s per other player), host CPU mostly rendering at FishNet's 500 fps cap,
17 of 17 processes alive. Details, chart and suspected causes: docs/loadtest-results.md. Bots set `ClientManager.SetFrameRate(30)`
(FishNet overrides `Application.targetFrameRate` on every connection change).

**044 review follow-ups.** 1: `[DefaultExecutionOrder(100)]` on CharacterVisual (after Pickup.LateUpdate sets the box and cloth
targets; test). 2: `FoodVisual.LastPart` above the `ShowParts` summary. 3: the unused two-argument `DiscardHeld` dropped from
ICleaningKitchen and KitchenService. 4: `ExitFlightLook` and `Pickup.ExitPoint` docs name the real exits (cookware, trash, bin,
sink; a box unpack has none). 5: `ButtonPressRules.IsOwnConfirm(now, ownPressAt)` (tested), used by ButtonPressView. 6: tests for
`CleaningService.ExitPoint` and `SpillWipeView.TryClothFor` (`Review044Tests`).

**Hygiene.** DefaultPrefabObjects re-saves reverted after each build; `PredictionBook.cs` had CRLF in the working copy, now LF;
`UnityConnectSettings.asset` untouched.

**Checks.** `verify.sh` PASS (EditMode 1221, was 1198; sim 400). Visual check `--tours look,cook,tutorial --out
.verify/visual045`: PASS, 0 Exception lines, no timeouts. The new Windows build starts on Direct3D 11 without a flag.

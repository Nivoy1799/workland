---
id: 047
title: Main menu and joining (title screen, host, join via Steam or IP, lobby, rejoin)
status: approved
owner: architect
builder: opus
---

# 047: Main menu and joining

## Goal
Today the game starts from command-line flags (`-host`, `-connect`). Early Access needs a title screen.

## Design
- **Title screen:**
  - Continue / New shop (host, with the 046 save);
  - Join a friend (Steam invite, friends list, rich presence; FishySteamworks);
  - Join by address (testing);
  - Settings (048);
  - Quit.
  The town is visible behind the menu.
- **Lobby:** the host's shop with up to 16 players (D36). Players appear as
  they join. The host can kick.
- **Connection feel:**
  - a loading screen with progress;
  - clear errors ("version mismatch", "server full", "connection lost");
  - automatic rejoin for 30 s after a drop;
  - the held item goes back to its spot when a player leaves.
- **Version check:** client and server compare a build hash, and a mismatch refuses cleanly.
- There are no dedicated game servers (D33): the host is the server.

## Acceptance criteria
- [x] AC1: `verify.sh`; RPC surface tests (only connection messages may change, documented).
- [x] AC2: Tests for the version check, kick, rejoin and errors.
- [x] AC3: Visual check: menu, host, join by address, lobby, a disconnect message.
- [ ] AC4 (manual, human): join through Steam on two PCs (finishes spec 003 AC2).

## Implementation notes

The spec names no folders; the work stayed in `game/Assets/_Project` (Scripts, Tests, Editor/GrayboxSceneBuilder, UI/Worktown.uss,
the regenerated scene), `scripts/visual-check.sh` (the menu tour) and one line in `tools/bots/run-loadtest.sh` (`-max-players`).

**Design.**
- `Net/Rules/JoinFlow` (pure) is the client's connection as the player sees it: Menu, Loading (stages Connecting, CheckingVersion,
  LoadingShop with a progress value), InGame, Rejoining, Message (a `DisconnectReason`). NetBootstrap feeds it the transport events,
  FishNet's OnAuthenticated, the own player's spawn and the server's notices, and makes the rejoin attempts it asks for.
- `Net/JoinAuthenticator` (FishNet `Authenticator` on the NetworkManager object; every connection, the host's own client too): the
  client sends `JoinRequest {Build, Name}` when its transport is up; the server admits it (`JoinRules.Decide`: the same build hash,
  then a free place, 16 with the host, D36) or sends `ConnectionNotice {Reason, Detail}` and fails the authentication (FishNet
  disconnects after the notice is out). No request within 10 s: NoAnswer. The server sends `LobbyRoster` (ids, names, host id, places)
  to everyone on each join and leave. `Kick(id)` (never the host) and `NotifyClosing()` (the host closes the shop) send a notice first.
- **Build hash:** `BuildHash.Compute(Application.version, ProtocolSurface.Lines(game assembly))`, FNV-1a 64 over sorted lines; the
  surface lists every RPC (server, observers, target, replicate, reconcile) with parameter types, every synced field and every
  broadcast with its fields, type names without assembly versions. Two builds of the same code match on any PC (AC4 needs no copied
  build); a changed message (or a dev build's dev-only RPCs against a release build) refuses cleanly. Shown in the menu corner.
- **Rejoin:** a drop in a joined shop (not the host's own) without a notice: Rejoining, first attempt after 1 s, then 3 s after each
  failed attempt, for 30 s from the drop (an attempt still hanging 10 s past the window is stopped), then "Connection lost". A notice
  (refused, kicked, the shop closed) never rejoins; leaving on purpose goes to the menu.
- **Held item on leave:** `PickupService` remembers each held item's origin at pick-up (`PickupOrigin`: its spot via the new
  `PlaceSpots.TryGetSpotOf`, else the pose it lay in). When the holder leaves, the item goes back there if the spot is free and no
  other item lies there (`PickupOrigin.GoesBack`), with `ItemMoved` for the kitchen (a pan back on its burner); otherwise (handed over
  fresh by a crate, or the place is used) it drops where it is, as before.
- **UI:** `UI/MainMenuView` (title: New shop, Join a friend, Join by address, Settings, Quit; the loading card with a bar; the rejoin
  countdown; error cards with Try again / Back to the menu). The town shows behind it: the CinemachineBrain is paused and the main
  camera sways slowly south-east of the shop (`MenuRules.CameraPose`). `UI/LobbyView`: Tab in the shop: "Players n/16", Kick for the
  host, Invite friends (Steam), Leave / Close the shop. The F1 debug menu no longer opens by itself before connecting (it keeps its raw
  connect buttons). Controls help gained the Tab line.
- **Continue:** there is no save yet (046 is superseded), so the title shows only "New shop"; `MenuRules.HostChoices(hasSave)` adds
  "Continue" once a save exists. **Settings** shows the controls and "More settings come in a later update" (048).
- **Command line unchanged:** `-host`, `-connect`, `-steam`, `-server` skip the menu as before (the flow just shows the loading card
  until the player spawns). New: `-max-players n` (1..64, server or host; the load test passes bots + 1, since 16 bots and the host are
  17 players) and `-tour menu` (no mode flag).

**Steam status.** Steamworks.NET (package) and FishySteamworks (in Assembly-CSharp, used by type name) were already in the project;
nothing was downloaded. `Net/SteamFriendsLink` (plain class owned by NetBootstrap): while hosting, rich presence `connect` =
`-steam <host SteamID64>` (`RichPresenceRules`; the same flag the command line takes, so Steam starting a friend's game for Join Game
goes straight in) and `status` = "Running a shop (n/16)"; `GameRichPresenceJoinRequested_t` joins from a running game (leaving the
current session first); Invite friends opens `ActivateGameOverlayInviteDialogConnectString`; Join a friend lists friends playing this
app id whose rich presence has a connect string. The player's Steam name is sent as the display name (cleaned). App id stays 480
(steam_appid.txt, unchanged). Not testable here with one account: AC4 is for the human on two PCs (host: New shop, Tab, Invite
friends; or the friend uses Join Game / Join a friend). With 480 (Spacewar) the friends list shows "Spacewar"; rich presence works.

**RPC surface (AC1).** No RPC or SyncVar changed. Three broadcasts were added, all connection messages: `JoinRequest`,
`ConnectionNotice`, `LobbyRoster`; `ConnectionSurfaceTests` pins them as the only broadcasts with exactly their fields.

**Tests (AC2).** `JoinFlowTests` (stages, refusals, kick, the shop closed, rejoin success, give-up after 30 s, a hanging attempt, the
host's own drop, leave and cancel), `JoinRulesTests` (version check, places, kick rule, names, every reason's words, the rejoin line,
the Steam connect string through LaunchArgs, lobby rows, the menu camera, the held item's return rule, `PlaceSpots.TryGetSpotOf`),
`ConnectionSurfaceTests` (the surface lines, the build hash), `LaunchArgsTests` (`-max-players`, `-tour menu`).

**Visual check (AC3).** `bash scripts/visual-check.sh --tours menu --out .verify/visual047` (new `menu` tour, `Dev/MenuTourRunner`;
local server only): the tour window starts on the title screen and presses the menu's buttons (`MainMenuView.Press`, the click's
own action); the observer is a `-host` that drops the tour player's connection without a notice, then kicks it, then quits.
`.verify/visual047/menu/sheet.png`: 01 title, 02 settings, 03 join-by-address, 04-07 joining (the loading card), 08 in-the-shop,
09 lobby-guest, 10 rejoining (connection lost, countdown), 11 rejoined, 12 kicked (error card), 13 menu-again, 14-17 opening (host:
New shop), 18 lobby-host (Kick-less, Invite friends, Close the shop), 19 closed-to-menu. `menu-observer/sheet.png`: 01
lobby-host-with-guest (Kick button), 02 guest-rejoined, 03 after-kick. PASS, 0 Exception lines; `look` re-run with the new
authenticator: PASS, 0 Exception lines. Bots: `run-loadtest.sh --bots 2 --ramp 10 --hold 20` PASS (3 of 3 alive, places 3).

**Fix found by the tour.** `Player/WorldLabelFade` (owner only) destroyed its label materials with the player, leaving every scene
label drawing magenta after a rejoin, a kick or a closed shop until the next player's first scan. It now gives each label its own
material, size and switch back on destroy.

**Checks.** `verify.sh` PASS: EditMode 1266 (was 1221), sim 400.

**Not covered / follow-ups.**
- AC4 (Steam on two PCs) is the human's; rich presence, Join Game, invites and the friends list are untested with one account.
- The held item's return is covered by its pure rule only (no tour step holds an item while leaving).
- `--server wsl` (dedicated server) was not re-run; the authenticator runs there too (the 045 MTU issue still blocks it).
- A kicked player can rejoin by hand at once (no ban list); the save ("Continue") waits for its own spec.
- During the first ~0.5 s of a new session in the same process the labels show without their panels (before the new player's
  first scan).
- Steam friends see the `status` rich-presence key only under "View game info". The friends-list line itself needs
  `steam_display` plus a rich-presence localization file uploaded for the app id (not possible with 480).
- Joining a friend's Steam game while hosting leaves the own shop at once; it should ask for confirmation first.
- Alt-F4 (or a crash) while hosting sends no HostClosed notice: the guests see "Connection lost" and try to rejoin for 30 s.

**047 review fixes.**
- Names: `JoinRules.CleanName` also drops format characters (bidi controls, zero-width, BOM; the U+200B..200F, 202A..202E and
  2060..206F ranges by value as well, for older Unicode tables) and `<`, `>`. Rich text is off on the lobby names, the friend
  buttons and the card text (which shows `ConnectionNotice.Detail` and the typed address). Tests: `<size=400>X`, bidi, zero-width.
- Held item on leave: `PickupService.PlanReturn` asks the same place veto as `TryPlace` (the kitchen's `canPlace`) for a spot
  origin; refused, the item drops. `PickupOrigin.GoesBack(allowed, spotTaken, occupied)`: the constant `hasOrigin` parameter is
  gone (the caller already drops an item without an origin); its test line now checks the veto. New test: a cup filled on the
  lemonade spot and lidded in the hands is not returned there.
- `-steam` takes only a SteamID64 (digits, fits a ulong, not 0: `RichPresenceRules.TryParseSteamId`, shared with the connect
  string); errors name "SteamID64".
- Nits: `SendRoster`'s dead parameter dropped; 1 s (was 0.3 s) before the closing host stops the server; broadcast fields
  ordered by `MetadataToken` (the hash is unchanged, declaration order was already used); doc lines for struct parameters in
  `ProtocolSurface`, the pending entry's start in `JoinAuthenticator`, the stall wording in `SpamGuard`; the build line is set once.

**Follow-ups (2026-11-11).** Five items of the list above; all in `game/Assets/_Project` plus `game/Steam/`. No RPC, SyncVar or
broadcast changed: the only new wire value is the reason byte `KickedBefore = 10` inside the existing `ConnectionNotice`
(`ConnectionSurfaceTests.ANewReason_RidesInTheNotice_AndChangesNoSurfaceLine`; enum values are not hashed, so the build hash is unchanged
and an older client shows "Disconnected (reason 10)"). Tests: `Followup047Tests`.
1. **Leave your shop?** `Rules/LeaveShopRules` (+ `JoinChoice`): a join while this game is in a session (`NetBootstrap.RequestJoin`; Steam's
   Join Game and invites go through `JoinSteam` into it) joins at once when idle, leaves then joins as a guest, asks first as the host,
   is ignored by a dedicated server. The host's question ("Leave your shop?" / "Your guests will be disconnected.", Leave / Stay) is a
   card in `UI/LobbyView` (cursor free); Leave closes the shop (HostClosed notice) and joins when it is shut, Stay drops the join; the
   question lapses if the shop closes first. There is no join-by-address inside a session today (the F1 debug connect buttons hide
   while connected), so only Steam's path reaches it in play; `RequestJoin` takes address targets too (the tour uses one).
2. **Quit while hosting.** `NetBootstrap` hooks `Application.wantsToQuit`: a host with guests sends HostClosed (`NotifyClosing`), runs
   the usual `CloseSoon` (1 s), then quits; `Rules/HostQuitRules` holds the quit at most 2 s whatever happens. Not held in the Editor
   (leaving Play mode) nor in scripted runs (`LaunchArgs.IsScripted`: tours, bots, metrics, crash/perf/stall tests), since a second
   `Application.Quit()` would lose their exit code; so no tour exercises it (pure rule only). A crash sends nothing: guests then see
   "Connection lost" after FishNet's remote timeout (60 s) and try to rejoin for 30 s.
3. **Session ban list.** `Rules/SessionBans` in `JoinAuthenticator`: `Kick` bans the connection's transport address (FishySteamworks
   gives the SteamID64: key `steam:id`; Tugboat the IP without port: `address:ip`, IPv4-mapped IPv6 folded), never the host's own
   player or an empty address; a banned join request is refused first (`JoinRules.Decide(..., kickedBefore)`) with the new reason
   "Kicked by the host" (no Try again: `ConnectionMessages.CanRetry`); the list is cleared when the server stops. The log names only
   the key's kind. Limits: players behind one public IP who join by address share a ban; the host's loopback client is exempt only
   because it joins first (FishNet marks it local after authentication) and the list is empty then.
4. **Friends-list text.** `SteamFriendsLink` sets `steam_display` = `#Status_Hosting` with `players` (and still `status`) while hosting,
   `#Status_Playing` as a guest. The tokens are in `game/Steam/rich_presence_localization.vdf` (Steamworks' multi-language format,
   `"lang" > "english" > "tokens"`, LF); a test checks it holds every token the code sets. **Human:** upload that file in Steamworks
   (App Admin, Community, Rich Presence) for the real app id; with 480 Steam cannot use it and shows only "Spacewar".
5. **Labels at session start.** `WorldLabelFade` scans every 0.1 s for `SettleSeconds` (1 s) after the own player spawns, then every
   0.5 s as before (`ScanDelay`), so scene objects FishNet activates around the spawn get their panels at once. Cheap; not visible in a
   shot (the tour shoots seconds later).

Checks: `verify.sh` PASS (EditMode 1370, was 1353; sim 403; gateway 175); BuildAll OK (Windows and Linux server). Visual check
`bash scripts/visual-check.sh --tours menu,cook --out .verify/visual-fu2`: PASS, 0 Exception lines. The menu tour gained
`menu/19-rejoin-refused` ("Kicked by the host", Back to the menu only; the host logs "refused: KickedBefore") and
`menu-observer/02-leave-question` (Leave / Stay over the shop; Stay kept it open).

**Follow-up review fixes (2026-11-11).**
- Quit: `NotifyClosing` runs through `GuardedCall` (a throw still starts `CloseSoon` and the quit), `RemoteGuests` counts 0 on a throw
  (quit at once rather than hang), and a second quit goes through once the hold is older than `LimitSeconds` (`HostQuitRules.StillHeld`;
  the coroutine may have died).
- A dedicated server's quit is never held (`HoldsQuit(..., dedicated)`, and `_quitLetThrough` is set before the stall exit), so
  `TickStallRules.DedicatedExitCode` survives.
- `LeaveShopRules.Decide(dedicated, closing, server, client)`: the real `_dedicated` flag (Ignore); a join while the shop is already
  closing is queued (`JoinWhenClosed`), not asked; any other running server asks, and `LobbyView` shows a waiting question even outside
  the shop.
- Labels: 0.1 s scans during the settle second (not every frame).
- `JoinAuthenticator`: comment that `IsLocalClient` has no effect at join-request time.
- Checks: `verify.sh` PASS (EditMode 1373, sim 403, gateway 175); BuildAll OK; visual check `--tours menu,cook --out
  .verify/visual-fu2b` PASS, 0 Exception lines (`menu/19-rejoin-refused`, `menu-observer/02-leave-question` as before).

**045 review follow-ups.**
1. `SpamGuard` is now a sliding window: kick when the TooFast replies of the last 10 s reach 300 (30/s sustained); a ring of 10
   per-second counts, allocation-free; requests that pass no longer reset anything. Doc comment rewritten with the configured
   timeouts (FishNet's remote timeout, 60 s on ServerManager and ClientManager; Tugboat's 1800 s is only the socket fallback; the
   "longer than a connection survives" claim is gone). `SpamGuard.ShouldKick(tripped, isLocal)` is the host seam (PickupInteractor
   uses it). `SpamGuardTests`: 50/s and 100/s kick within 10 s, 1000/s within 1 s, an honest 10/s never over 60 s, a 10 s stall then
   ~100 queued never, the host never (ShouldKick), plus trip-once, window, reset and bad-limit tests.
2. `docs/loadtest-results.md`: verdict "At the limit, no headroom: 49.6 KB/s payload only. Wire bytes, with UDP/IP and LiteNetLib
   headers, are higher; the worst second is 53.0 KB/s; loopback; bots do not cook."; per-packet traffic stats and a 15 Hz send rate
   for remote players are named required next steps.
3. Nits: `SpitDesk.Clear()` removed (unused); `-seed` documented as accepting negative values (a test already pins -3); the 045 ACs
   ticked, its spam paragraph points here.

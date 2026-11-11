---
id: 055
title: AI gateway go-live under D33 (per-host auth, server-built inputs, cost limits, game client)
status: deferred
owner: architect
builder: sonnet       # 055a: opus (identity rules in /sim); 055b, 055c: sonnet
---

> Deferred on 2026-11-11 (D38): no live gateway at Early Access; names are generated offline and shipped.

# 055: AI gateway go-live

## Goal
Make the spec 006 gateway safe to switch on when player PCs are the hosts (D33): a token shipped in the game is public, and
anything a host sends may be forged. This spec closes the four go-live blockers of spec 006 ("Go-live blockers (D33)") and
human-inbox question 20, adds cost limits and the game-side client, and defines how the game behaves without a gateway.
The gateway stays **off** (no URL in builds) until the human answers the OPEN items below (O4, O5, O6).

## This is an integration spec
It touches /sim, /ai-gateway and /game, so it is built as three parts, each its own branch, commit and review, in order:
**055a** (/sim) then **055b** (/ai-gateway, uses 055a) then **055c** (/game). Each part's builder touches only its folders.

## Scope
**055a: canonical tree public (/sim; one /game file forwards)**
- `sim/Runtime/Cooking/ItemTree.cs`, `ItemTreeNode.cs`, `ItemTreeError.cs`, `TreeNaming.cs`, `TreeNamingResult.cs` (new)
- `sim/Runtime/Cooking/KitchenItems.cs` (new: the base-item list the shipped kitchen uses, moved from the game's
  `DemoIngredients.All`/`Catalog`, same order)
- `sim/Runtime/Cooking/ItemFactory.cs`, `ItemNaming.cs` (change only to share naming code with `TreeNaming`, see Design)
- `game/Assets/_Project/Scripts/Cooking/DemoIngredients.cs` (change: forwards to `KitchenItems`; look-byte order unchanged)
- `sim/Sim.Tests~/Cooking/ItemTreeTests.cs`, `TreeNamingTests.cs` (new)

**055b: gateway (/ai-gateway, `.env.example`, `ai-gateway/README.md`)**
- `ai-gateway/src/Worktown.Gateway/Worktown.Gateway.csproj` (change: `ProjectReference` to `sim/Sim.csproj`)
- `Auth/`: `SteamTicketVerifier.cs`, `ISteamTicketVerifier.cs`, `SessionTokens.cs`, `SessionFilter.cs`, `AuthEndpoint.cs` (new);
  `ServiceTokenFilter.cs` (change: dev/test mode only)
- `Api/ItemNameRequestV2.cs`, `Api/RequestValidator.cs` (change), `Pipeline/ItemNamePipeline.cs` (change),
  `Pipeline/SingleFlight.cs` (new)
- `Limits/`: `HostQuota.cs`, `GlobalBudget.cs`, `UsageCost.cs` (new)
- `Cache/CacheKey.cs` (change: `gw2` preimage), `Admin/CacheAdminEndpoint.cs` (new)
- `Moderation/IModerator.cs` (change: `bool IsPlaceholder`), `Config/GatewayOptions.cs` (change: new vars, live gate)
- `ai-gateway/tests/Worktown.Gateway.Tests/` (new tests below; `StubSteamHandler.cs`)

**055c: game client (/game)**
- `game/Assets/_Project/Scripts/Ai/GatewayClient.cs`, `SteamWebTicket.cs`, `GatewayItemNameSource.cs`, `GatewayRules.cs` (new;
  `GatewayRules` pure)
- `game/Assets/_Project/Scripts/Cooking/KitchenState.cs` (change: `NameOf(item, source)`),
  `KitchenService.cs` (change: pass the source, refresh live labels when a name arrives)
- `game/Assets/_Project/Scripts/Net/Rules/LaunchArgs.cs` (change: `-gateway-url`, dev builds only)
- `game/Assets/_Project/Tests/EditMode/GatewayClientTests.cs` (new)

## Out of scope
- Choosing the hoster (O5), the backend or a persistent store (O4), the moderation approach (O6). Deploy, TLS, Docker.
- Other content types (recipe text, NPC lines). Player-written text. A "report this name" button (later spec).
- Turning the gateway on in a shipped build. Steam store AI-content disclosure text (human step).

## Design

### Threat model (what changes under D33)
A host is a player's PC running code we shipped: it can be modified. So: no shared secret in the game; everything in a
request except a verified identity is untrusted; the cost anchor is one Steam account that owns the game.

### Auth options (decision for the human; recommended default in Open question 1)
| Option | How | For | Against |
|---|---|---|---|
| A. Steam Web API ticket | Host: `SteamUser.GetAuthTicketForWebApi("worktown-gateway")` (in our Steamworks.NET). Gateway: `GET partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1` with the publisher key, app id, hex ticket, identity; gets the SteamID; issues its own short session token | No own accounts; one owned copy = one quota; bans by SteamID | Needs a real app id (480 cannot), a publisher key on the gateway, one Steam call per session; a cheater can still script requests with their own ticket (quota caps it) |
| B. Encrypted app ticket | `RequestEncryptedAppTicket`; gateway decrypts offline with the app's secret key (Valve's native lib) | No Steam call per session | Native lib on the gateway host; key rotation by hand |
| C. Shared token (today) | `GATEWAY_SERVICE_TOKEN` in the build | Simple | Public; anyone can spend our budget. Dev/test only |
| D. Own accounts | Login on our backend | Full control | Needs O4; another login for players |
| E. No live gateway | Generate names offline for the trees the menu can make; ship them in a `NameCatalog` | No runtime cost or abuse; works offline | Only known trees get names; new combinations keep the fallback |

Design below assumes A (the inbox default) and keeps C for tests and local dev.

### Session flow (option A)
1. Host starts hosting (Steam ready, gateway URL set): `SteamWebTicket` gets a ticket (callback `GetTicketForWebApiResponse_t`).
2. `POST /v2/auth/steam` `{ "ticket": "<hex, 2..4096>" }`. Gateway calls Steam (timeout 5 s), requires a SteamID, the app
   owned (check `ownersteamid`/`publisherbanned` if returned; verify with a real key), returns
   `{ "session": "<token>", "expiresIn": 3600 }`. 401 `steam_rejected` on any failure (one fixed message).
3. Session token: `v1.<steamid>.<expiryUnix>.<HMAC-SHA256(GATEWAY_SESSION_KEY)>`, stateless, checked in constant time.
   Every content call sends `Authorization: Bearer <session>`; 401 `session_expired` makes the host re-auth once.
4. The quota partition is the SteamID from the token; `X-Worktown-Server-Id` is gone in v2.
5. Logs never contain tickets, tokens or raw SteamIDs: a SteamID is logged as the first 12 hex of
   `SHA256(GATEWAY_LOG_SALT | steamid)`.
`GATEWAY_AUTH_MODE = steam | service_token`; `service_token` is refused at startup when `GATEWAY_LIVE=true`.

### Request v2: only the tree (fallbackName built server-side)
`POST /v2/content/item-name` body `{ "canonicalTree": "...", "itemHash": "<64 hex>" }` (unknown properties rejected).
Gateway: `ItemTree.TryParse` (400 `invalid_request`), `ItemTree.Hash(node) == itemHash` (400 `hash_mismatch`),
`TreeNaming.TryName(node, KitchenItems catalog, OperationCatalog.CreateDefault())` (422 `unknown_item` for an id, bucket or
arity the shipped data does not know). The prompt's plain name and tags come only from `TreeNaming`. v1 stays for tests;
`GATEWAY_LIVE=true` maps it to 404.

### 055a: /sim interfaces
```csharp
public sealed class ItemTreeNode { public string Id; public bool IsBase;          // sketch; immutable in code
    public IReadOnlyList<KeyValuePair<string,string>> Buckets; public IReadOnlyList<ItemTreeNode> Inputs; }
public static class ItemTree {
    public static bool TryParse(string tree, out ItemTreeNode? node, out ItemTreeError error); // length <= 2048, depth and
                                                                                               // input limits = CookingLimits
    public static string Hash(ItemTreeNode node);    // same preimages as ItemCanonical (v1), no second copy of the rules
    public static string Format(ItemTreeNode node);  // Format(Parse(t)) == t for every canonical t
}
public static class TreeNaming {
    public static TreeNamingResult TryName(ItemTreeNode node, ItemCatalog items, OperationCatalog ops);
}   // TreeNamingResult: Ok(FallbackName, Tags) or Error(ItemTreeError)
```
Grammar (from `ItemCanonical.OpTree`): `base = id`, `op = id "(" [k=v {"," k=v}] ";" tree {"," tree} ")"`, ids `[a-z0-9_]{1,32}`.
`TreeNaming` must give exactly `ItemNaming.FallbackName` and the item's tags for every item the factory can make. No copied
naming logic: the builder moves the template choice (fry shapes, griddle sides, assembly names) into shared code used by
both paths, and proves it with a corpus test. Bucket check: every `k=v` is a param and bucket of that operation.

### Cache-poisoning protection
- With v2 every prompt input is derived from the tree on the gateway, so the first writer only chooses which valid tree
  gets generated, never the prompt or the name. "First write wins" stays.
- Cache key `gw2|item_name|<schema>|<prompt>|<model>|<naming>|<hash>`: `<naming>` = first 16 hex of SHA-256 over the naming data
  (base display names, templates, bucket labels), so a data change regenerates instead of hitting 409.
- Output still passes schema, the vegan rule (D27) and `IModerator` before the cache write; nothing invalid is cached.
- Admin removal: `DELETE /v1/admin/cache/{key}` with `GATEWAY_ADMIN_TOKEN` (never in a game build); the next request
  regenerates. Logged with the key prefix.
- Single flight: concurrent misses for one key share one upstream call.
- Persistence is O4/O5: `IContentCache` stays the seam; in-memory only in this spec.

### Cost limits (all env vars, `TimeProvider` for time; single instance, in memory)
| Limit | Default | On hit |
|---|---|---|
| Misses per SteamID | burst 10, 30 per hour, 200 per UTC day | 429 `rate_limited`, `Retry-After` |
| Requests per SteamID (hits too) | 120 per minute, burst 30 | 429 |
| Auth calls per IP | 10 per minute | 429 |
| Upstream calls in flight (global) | 8 | queue up to 32, then 503 `busy` |
| Global spend per UTC day | `GATEWAY_DAILY_BUDGET_USD` (no default: required when live) | 503 `budget_exhausted` until 00:00 UTC; hits still served |
Spend: `usage.prompt_tokens` and `completion_tokens` of each upstream response x `GATEWAY_PRICE_IN_PER_MTOK` /
`GATEWAY_PRICE_OUT_PER_MTOK`; a response without `usage` counts `max_tokens` at the output price. A warning log at 80 %.

### Moderation (O6 OPEN; nothing decided here)
The pipeline keeps the vegan rule and the `IModerator` seam. `IModerator.IsPlaceholder` is true for
`PlaceholderDenyListModerator`; with `GATEWAY_LIVE=true` startup fails while the placeholder is registered. Choosing and
building the real moderator is a later spec after the human decides O6.

### 055c: game-side client (host only)
- `GatewayItemNameSource : IItemNameSource`: in-memory `hash -> name`. On `TryGetName` miss it queues the hash with its tree
  (deduplicated), at most 2 requests in flight, off the tick (UnityWebRequest, async; never awaited by /sim or a tick).
- `KitchenState.NameOf(item, source)` uses `ItemNaming.Resolve(item, NameCatalog.Empty, source)`; on arrival
  `KitchenService` re-sends `Food.DisplayName` for live foods with that hash and vessel titles. Clients already receive names
  through the existing SyncVars: no new network message.
- Client-side check of every received name: the schema's name regex and `ContentRules` (D27); a failing name is dropped.
- Off unless a URL is set: release builds have no URL until go-live; dev builds take `-gateway-url`. No Steam (Tugboat dev,
  bots) means no ticket, so no gateway.

### Missing gateway: fallback (`GatewayRules`, pure)
| Situation | Game does |
|---|---|
| No URL, no Steam, auth 401 twice | No requests this session; fallback names everywhere |
| 400 / 409 / 422 for a hash | Remember the hash as failed for the session; fallback name |
| 429 / 503 | Pause all requests for `Retry-After` (else 60 s, max 10 min); queued hashes wait |
| Timeout / network error | Retry that hash once after 30 s, then failed for the session |
| 3 consecutive network failures | Circuit open for 5 min |
A name only ever replaces the fallback once per hash per session; it never changes stats, quality or rules (D5).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes after each part.
- [ ] AC2 (055a): `Format(Parse(t)) == t` and `Hash(Parse(t)) == item.Hash` for a corpus of at least 2000 items (every
      `RecipeTemplates.Default` dish plus a seeded random walk over the factory with `KitchenItems`); `TryName` equals
      `ItemNaming.FallbackName` and the item's tags for all of them; the spec 005 fries fixture is pinned.
- [ ] AC3 (055a): Unbalanced, over-long, too deep, uppercase, unknown base, unknown op, unknown bucket and wrong arity each
      return their `ItemTreeError`. `DemoIngredients.All` keeps its order (existing look tests green).
- [ ] AC4 (055b): With `StubSteamHandler`: a valid ticket gives a session; a rejected ticket, a Steam timeout and a Steam
      5xx give 401 with no upstream call; an expired or tampered session gives 401; the ticket and session never appear in
      captured logs.
- [ ] AC5 (055b): v2 with a hash that does not match the tree: 400 `hash_mismatch`; unknown id: 422; no upstream call.
      The captured upstream prompt contains the server-built plain name (fries fixture) and never any caller text.
- [ ] AC6 (055b): The SteamID partition: the 11th miss in a burst is 429 while another SteamID still gets 200; after the
      daily budget (made-up prices and `usage`) the next miss is 503 `budget_exhausted` and a cached hit is 200.
- [ ] AC7 (055b): 5 concurrent identical misses make 1 upstream call; the admin delete makes the next request a miss; the
      admin endpoint without its token is 401.
- [ ] AC8 (055b): Startup fails with `GATEWAY_LIVE=true` and any of: `service_token` mode, the placeholder moderator, no
      daily budget, no session key. The message names the variable, never a value.
- [ ] AC9 (055c): `GatewayClientTests` with a fake transport: every row of the fallback table; a name that fails the regex or
      `ContentRules` is dropped; no request without URL or Steam; a name never arrives on a tick thread call path.
- [ ] AC10 (055c): The cook tour of the visual check (no URL) shows the same names as before and 0 Exception lines.

## Test plan
- 055a: `sim/Sim.Tests~` (seeded corpus, no randomness without a seed). 055b: `dotnet test ai-gateway/Gateway.sln`, no network
  (stub Steam host `https://steam.test`, stub upstream). 055c: EditMode tests with a fake `IGatewayTransport`.
- Command: `bash scripts/verify.sh`.

## Human steps
- Decide O5 (hoster), O4 (persistent cache store) and O6 (moderation) before `GATEWAY_LIVE=true`.
- A real Steam app id and a publisher Web API key (gateway `.env` only); one manual ticket check with a real account.
- Price and daily budget numbers; the Steam store's AI-generated content disclosure; a privacy note (the gateway sees SteamIDs).

## Open questions (recommended defaults)
1. Auth: option A (Steam Web API ticket + 1 h gateway session). [default: A; B if Steam calls prove a problem]
2. Is a live gateway needed for Early Access, or is option E (offline names for the menu's trees) enough first?
   [default: ask; E is cheaper and needs no O5]
3. Family sharing: does a borrowed copy get a quota? [default: yes, quota by the playing SteamID]
4. Quota numbers in the cost table. [default: as listed]
5. Where generated names show: food labels and vessel titles only; orders, menu and tickets keep plain names.
   [default: yes, avoids two names for one order]
6. Persist received names in the host's save (fewer calls next session). [default: yes, in a later save spec]
7. Version skew: the gateway runs the /sim of the newest release; older or newer games get 422 for unknown ids.
   [default: yes, redeploy the gateway with each release]
8. Persistent cache store (O4) and hoster (O5). [OPEN: not decided here]
9. Moderation approach (O6). [OPEN: not decided here]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)

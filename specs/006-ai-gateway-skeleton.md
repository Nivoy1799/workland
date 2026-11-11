---
id: 006
title: AI gateway skeleton (C# ASP.NET, item names via OpenRouter)
status: approved
owner: architect
builder: sonnet
---

# 006: AI gateway skeleton

## Goal
A small C# ASP.NET (.NET 10, D25) service in `/ai-gateway`. The game server calls it to get a
display name and description for an item hash (spec 005). It runs the whole pipeline: service-token
auth, rate limit, cache lookup, upstream OpenRouter call (D24), JSON-schema validation, moderation,
cache write and response. The output is flavor text only (D5). It never changes stats or rating.

## Scope
Only `/ai-gateway`, plus `scripts/verify.sh`, `.env.example` and one line in `docs/architecture.md`.
- `ai-gateway/Gateway.sln` (new; create with `dotnet new sln -n Gateway --format sln`, because the
  .NET 10 SDK defaults to `.slnx`)
- `ai-gateway/src/Worktown.Gateway/Worktown.Gateway.csproj` (new; `Microsoft.NET.Sdk.Web`, net10.0,
  `Nullable enable`, `TreatWarningsAsErrors`, `InternalsVisibleTo` the tests). Schemas are embedded
  with `<EmbeddedResource Include="../../schemas/*.json" LogicalName="schemas.%(Filename)%(Extension)" />`.
- `src/Worktown.Gateway/`: `Program.cs` (ends with `public partial class Program {}`),
  `Config/GatewayOptions.cs`, `Auth/ServiceTokenFilter.cs`, `Api/ItemNameRequest.cs`,
  `Api/ItemNameResponse.cs`, `Api/ErrorResponse.cs`, `Api/RequestValidator.cs`,
  `Pipeline/ItemNamePipeline.cs`, `Pipeline/PipelineOutcome.cs`, `Cache/IContentCache.cs`,
  `Cache/CachedContent.cs`, `Cache/InMemoryContentCache.cs`, `Cache/CacheKey.cs`,
  `Prompts/ItemNamePrompt.cs`, `Upstream/IUpstreamClient.cs`, `Upstream/OpenAiCompatibleClient.cs`,
  `Upstream/UpstreamResult.cs`, `Upstream/UpstreamSchema.cs`, `Validation/SchemaValidator.cs`,
  `Moderation/IModerator.cs`, `Moderation/ModerationResult.cs`,
  `Moderation/PlaceholderDenyListModerator.cs`
- `ai-gateway/schemas/item_name.v1.schema.json` (new)
- `ai-gateway/tests/Worktown.Gateway.Tests/`: `Worktown.Gateway.Tests.csproj` (net10.0, same xunit
  versions as `sim/Sim.Tests~/Sim.Tests.csproj`, plus `Microsoft.AspNetCore.Mvc.Testing` 10.0.x),
  `StubUpstreamHandler.cs`, `GatewayFactory.cs`, `CapturingLoggerProvider.cs`, `CacheKeyTests.cs`,
  `RequestValidatorTests.cs`, `SchemaValidatorTests.cs`, `UpstreamSchemaTests.cs`,
  `ModeratorTests.cs`, `ItemNameEndpointTests.cs`, `AuthRateLimitTests.cs`, `LoggingTests.cs`
- `ai-gateway/README.md` (change: stack is C#, how to run and test, env vars)
- `scripts/verify.sh` (change: section 3, see Design)
- `.env.example` (change: add the new `GATEWAY_*` vars from the config table with safe placeholders)
- `docs/architecture.md` (change: the /ai-gateway line "Speaks the OpenAI-compatible chat
  completions API to the game server" becomes "Exposes typed content endpoints to the game server
  and speaks the OpenAI-compatible chat completions API to the upstream")
- This spec file (`## Implementation notes`)

## Out of scope
- Docker image, k3s/Agones manifests, TLS, deployment (later /infra spec).
- Postgres or any persistent cache. O4 is OPEN; only `IContentCache` plus the in-memory version.
- Real moderation. O6 is OPEN; ship only the placeholder below.
- Other content types (npc_line, recipe text). Streaming. Single-flight dedup of concurrent misses.
- The game-side client and `IItemNameSource` implementation (own follow-up spec; spec 007 shipped without it). Any change in `/sim` or `/game`.
- Metrics/Prometheus, cost budgets, upstream concurrency caps.

## Design

### Why a typed endpoint, not an OpenAI-compatible inbound API
The gateway exposes **typed content endpoints** and builds prompts itself from in-game IDs and
properties; upstream it speaks OpenAI-compatible chat completions. Reasons: (1) guardrail: a raw
chat endpoint would accept arbitrary prompts; (2) schema, prompt version and cache key are fixed per
content type server-side; (3) D3 holds: cloud to vLLM is still only base URL and model env vars.
`docs/architecture.md` is updated to match (Open question 1).

### No reference to sim/Sim.csproj (yet)
The useful reuse would be recomputing `Item.Hash` from `CanonicalTree` to check that a request is
consistent. That code (`ItemCanonical`) is `internal` in /sim. Making it public is a /sim change, so
it gets its own spec. Copying the preimage rules into the gateway would create the second
implementation D25 wants to avoid. So the gateway treats `itemHash` as an opaque key, and the
service token means only official servers can call it (D16). It guards consistency with the cache
check `tree_mismatch` (below). Nothing here needs Unity.

### API (game server to gateway)
`POST /v1/content/item-name`. Headers: `Authorization: Bearer <GATEWAY_SERVICE_TOKEN>`,
`X-Worktown-Server-Id: <id>` (`^[a-z0-9-]{1,64}$`; rate-limit partition and log field, not a secret).
```json
{ "itemHash": "<64 lowercase hex>",
  "canonicalTree": "fry(temp=hot,time=medium;cut(shape=sticks;potato))",
  "fallbackName": "crispy fried potato sticks",
  "tags": ["fried"] }
```
Validation (`RequestValidator`, pure): `itemHash` `^[0-9a-f]{64}$`; `canonicalTree`
`^[a-z0-9_(),;=]{1,2048}$` with balanced parentheses; `fallbackName` `^[A-Za-z0-9 ,'-]{1,200}$`
(built by `ItemNaming.FallbackName` from game data, never player text); `tags` 0..32 items, each
`^[a-z0-9_]{1,32}$`, sorted, distinct. Unknown JSON properties are rejected
(`JsonUnmappedMemberHandling.Disallow`). Body limit 8 KiB. Exact stats and quality are **not** sent:
they are not part of identity (D20), so they must not affect a per-hash name.

`200` response:
```json
{ "itemHash": "...", "name": "Golden Spud Sticks", "description": "Crisp sticks of fried potato.",
  "cacheHit": false, "contentVersion": "item_name.s1.p1", "model": "qwen/qwen3.8-27b" }
```
Errors use the body `{ "error": { "code": "<code>", "message": "<short, fixed text>" } }`. The
message never contains upstream output.
| HTTP | code | when |
|---|---|---|
| 400 | `invalid_request` | validation failed, bad/missing server id header |
| 401 | `unauthorized` | token missing or wrong (same body for both) |
| 409 | `tree_mismatch` | cache hit for this key holds a different `canonicalTree` (logged as warning) |
| 422 | `content_rejected` | moderation rejected the output (not cached) |
| 429 | `rate_limited` | limiter rejected; `Retry-After` header set |
| 502 | `upstream_invalid_output` | output failed schema validation on every attempt (not cached) |
| 502 | `upstream_error` | non-2xx from upstream after retries |
| 504 | `upstream_timeout` | upstream timed out after retries |
`GET /healthz` returns 200 `{"status":"ok"}`. It needs no auth, skips the rate limit and never calls
upstream.

### Pipeline order (`ItemNamePipeline.RunAsync(ItemNameRequest, string serverId, CancellationToken)`)
auth (endpoint filter), rate limit, request validation, cache key, cache get (a hit with the same tree
returns `cacheHit: true`, a hit with a different tree returns 409), upstream call, parse
`choices[0].message.content` as JSON, schema validation, moderation, cache set, response. Auth comes
before the rate limit so that unauthenticated traffic cannot use up a server's quota.
Retry: at most **2 attempts total** for schema-invalid output, upstream 5xx/429, timeout or network
error. 4xx responses and moderation rejections are not retried. Each attempt is validated from
scratch. Output is never repaired: no stripping of code fences, no trimming, no truncation.
`finish_reason` other than `stop` counts as invalid.

### Cache
```csharp
public static class CacheKey { // SHA-256, lowercase hex, UTF-8 preimage, "|" separated
    public static string Compute(string contentType, int schemaVersion, int promptVersion,
                                 string modelId, string itemHash); } // "gw1|item_name|1|1|<model>|<hash>"
public sealed record CachedContent(string ContentType, string ItemHash, string CanonicalTree,
    string PayloadJson, string ContentVersion, string Model, DateTimeOffset CreatedAt);
public interface IContentCache {
    Task<CachedContent?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, CachedContent value, CancellationToken ct); } // first write wins
```
`InMemoryContentCache`: `ConcurrentDictionary`, `TryAdd` semantics, max entries from config. When
full it does not add and logs a warning. `CreatedAt` comes from an injected `TimeProvider`.
Bumping the schema or prompt version changes the key, so the content is regenerated.
`contentVersion` = `item_name.s{schema}.p{prompt}`.

### Schema (`schemas/item_name.v1.schema.json`, draft 2020-12)
`type: object`, `additionalProperties: false`, `required: [name, description]`;
`name`: string, minLength 3, maxLength 40, pattern `^[A-Za-z]+([ '-][A-Za-z]+)*$`;
`description`: string, minLength 10, maxLength 160, pattern `^[A-Z0-9][A-Za-z0-9 ,.'!?-]*[.!?]$`.
English ASCII only, so URLs, `@`, markup and emoji cannot pass. `UpstreamSchema.ForUpstream(JsonNode)`
(pure) returns a copy without `$schema`, `$id`, `pattern`, `minLength` and `maxLength` for
`response_format`, because provider support for those keywords varies. Local validation of the full
schema is the authority.
Library: **Corvus.Text.Json.Validator** (Apache-2.0, draft 2020-12, targets net10.0, schema loaded at
runtime via `JsonSchema.FromText`). JsonSchema.Net was rejected: since 2026 its binary releases carry
an Open Source Maintenance Fee for users with revenue >= US$10k (Open question 6). If Corvus gives no
error paths, log only `schema_invalid` and the output length (uncertain, check while building).

### Prompt (`ItemNamePrompt`, `Version = 1`, pure `Build(ItemNameRequest) -> messages`)
System: fixed text. It describes a cozy low-poly town life game, asks for a short, appetizing,
family-friendly English dish name (2 to 5 words, no brands, no real people) plus one description
sentence, JSON only. User: `Process: <canonicalTree>\nPlain name: <fallbackName>\nTags: <tags>`.
Only validated fields are inserted. Any change to the text bumps `Version`.

### Upstream (`OpenAiCompatibleClient : IUpstreamClient`, typed HttpClient)
`POST {GATEWAY_UPSTREAM_BASE_URL}/chat/completions` with `Authorization: Bearer <key>`. Body: `model`,
`messages`, `temperature`, `max_tokens: 300`,
`response_format: {type: "json_schema", json_schema: {name: "item_name", strict: true, schema}}`.
With `GATEWAY_UPSTREAM_FLAVOR=openrouter` it also sends
`provider: {require_parameters: true, only: [...] if set, allow_fallbacks: true}` and
`reasoning: {effort: "<GATEWAY_REASONING_EFFORT>"}` (omitted if empty). With `openai` (vLLM later)
neither is sent. `UpstreamResult` is a discriminated outcome: Ok(content, finishReason), Timeout,
HttpError(status), NetworkError.
OpenRouter, checked 2026-11-05 (docs: structured-outputs, provider-routing, reasoning-tokens):
`response_format.json_schema{name,strict,schema}` works, but support is **per provider**, so
`require_parameters: true` is needed. `provider.only/order/allow_fallbacks` exist. `reasoning.effort:
"none"` disables reasoning unless it is mandatory. `qwen/qwen3.8-27b` is listed (2026-08-14, ~23
providers, `response_format` accepted). **Uncertain:** which providers do strict JSON for it, whether
`"none"` is allowed, and whether pattern/length keywords are honored. A human checks with a real key.

### Config (env vars only; no appsettings.json values; tests inject the same keys via `UseSetting`)
| var | default | rule |
|---|---|---|
| `GATEWAY_UPSTREAM_BASE_URL` | none | required, absolute https URL (tests: `https://upstream.test/v1`) |
| `GATEWAY_UPSTREAM_API_KEY` | none | required, secret |
| `GATEWAY_MODEL` | none | required |
| `GATEWAY_SERVICE_TOKEN` | none | required, >= 32 chars, secret |
| `GATEWAY_UPSTREAM_FLAVOR` | `openrouter` | `openrouter` or `openai` |
| `GATEWAY_OPENROUTER_PROVIDERS` | empty | comma list for `provider.only` |
| `GATEWAY_REASONING_EFFORT` | `none` | empty means omit |
| `GATEWAY_TEMPERATURE` | `0.7` | 0..2 |
| `GATEWAY_UPSTREAM_TIMEOUT_SECONDS` | `20` | 1..120, per attempt |
| `GATEWAY_RATE_LIMIT_PER_MINUTE` | `120` | token replenishment per server id |
| `GATEWAY_RATE_LIMIT_BURST` | `30` | token bucket size |
| `GATEWAY_CACHE_MAX_ENTRIES` | `100000` | in-memory cap |
`GatewayOptions` uses `ValidateOnStart`: a missing or invalid value stops startup with an error that
names the variable, never its value.

### Auth and rate limiting
`ServiceTokenFilter` (endpoint filter) compares `SHA256(presented)` with `SHA256(configured)` using
`CryptographicOperations.FixedTimeEquals`, so the length does not leak. Rate limiting uses the
built-in `AddRateLimiter` with a `PartitionedRateLimiter` token bucket keyed by `X-Worktown-Server-Id`
(`QueueLimit = 0`). On reject it returns 429, the error body and `Retry-After`, taken from the lease
metadata or 60 if absent.

### Logging and secrets (hard rule 8)
Log per request: server id, item hash, cache key prefix (12 chars), hit/miss, attempts, outcome code,
latency. Never log the API key, service token, `Authorization` headers, prompt messages or raw
upstream output, at any level. Test secrets must not match the verify.sh pattern
`sk-[A-Za-z0-9]{20,}` (use for example `test-upstream-key-not-a-secret-0001`).

### Moderation (O6 OPEN, placeholder only)
`IModerator { Task<ModerationResult> CheckAsync(string contentType, JsonElement payload, CancellationToken ct); }`,
`ModerationResult(bool Allowed, string? ReasonCode)`. It runs after schema validation and before the
cache write. `PlaceholderDenyListModerator` matches case-insensitive whole words in all string
fields against a short list of obvious slurs and profanity in a `static readonly` array. Its class
doc says `// PLACEHOLDER (O6 OPEN): replace before Early Access; choosing the approach is a human decision.`
Tests replace it through DI with a list containing `forbiddenword`.

### verify.sh section 3
Remove the npm/pytest branches (D25). If `ai-gateway/Gateway.sln` exists and `dotnet` works:
`run "ai-gateway: dotnet test" dotnet test ai-gateway/Gateway.sln --nologo --verbosity quiet`, else
skip ("dotnet not installed or not working" / "not created yet"), mirroring section 1.

### Later use from the game (follow-up spec, note only)
The /game server owns a `GatewayItemNameSource : IItemNameSource` (hash to name). On the first
creation of an unknown hash it fires an async request, off the tick thread and never awaited by /sim.
`ItemNaming.Resolve` returns the fallback until the result arrives. Failures are remembered per
session. Spec 007 is already built without this, so the game-side client gets its own spec. Readable mix-name order (spec 005 follow-up) is a separate /sim spec (Open question 9).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes and shows `PASS ai-gateway: dotnet test`, plus all
      existing checks. There is no npm or pytest branch left in section 3.
- [ ] AC2: `dotnet test ai-gateway/Gateway.sln` runs >= 30 tests with no network. The only base
      URL used is `https://upstream.test/v1`, served by `StubUpstreamHandler`.
- [ ] AC3: Miss then hit: the first call returns `cacheHit: false` with 1 upstream call. The second
      identical call returns `cacheHit: true`, the same name and description, and the stub count is
      still 1.
- [ ] AC4: `CacheKey.Compute("item_name",1,1,"qwen/test-model", 64 x "a")` equals a literal pinned
      from `printf '%s' 'gw1|item_name|1|1|qwen/test-model|aaaa...a' | sha256sum` (or `shasum -a 256` on macOS). Changing any
      one input changes the key.
- [ ] AC5: Schema-invalid output (missing field, extra field, name 41 chars, `http://` in the
      description, fenced JSON) returns 502 `upstream_invalid_output` after exactly 2 stub calls. The
      next identical request calls upstream again (nothing was cached).
- [ ] AC6: Output containing `forbiddenword` returns 422 `content_rejected` after 1 stub call. It is
      not cached.
- [ ] AC7: Missing token or wrong token returns 401 with no stub call. `/healthz` returns 200 without
      a token.
- [ ] AC8: With burst 2 and 1 per minute, the 3rd request from the same server id returns 429 with
      `Retry-After`. A different server id still gets 200.
- [ ] AC9: The captured logs (all levels, `CapturingLoggerProvider`) contain neither the API key, the
      service token nor the system prompt text after a miss, a hit and a 401.
- [ ] AC10: A request with an unknown property, uppercase hash, unbalanced tree or a 33-char tag
      returns 400 with no stub call. A hit with a different tree returns 409.
- [ ] AC11: The upstream request body captured by the stub has `response_format.type ==
      "json_schema"`, `strict == true`, no `pattern` in the schema and
      `provider.require_parameters == true` (flavor openrouter). With flavor `openai`, there is no
      `provider` key.
- [ ] AC12: Startup without `GATEWAY_SERVICE_TOKEN` fails. The error mentions the variable name and
      no secret value.

## Test plan
- Unit tests: `CacheKeyTests`, `RequestValidatorTests` (valid fixture from spec 005: the fries tree
  and hash `fry(temp=hot,time=medium;cut(shape=sticks;potato))`, plus each invalid case),
  `SchemaValidatorTests` (valid, and each AC5 case), `UpstreamSchemaTests`, `ModeratorTests`.
- Integration tests (`GatewayFactory : WebApplicationFactory<Program>`): sets the env keys via
  `UseSetting`, swaps the primary handler of the typed upstream client for `StubUpstreamHandler`
  (queued responses, call count, captured bodies), replaces `IModerator` and adds
  `CapturingLoggerProvider`.
- Command: `bash scripts/verify.sh` (fast loop: `dotnet test ai-gateway/Gateway.sln`).

## Human steps
- Put a real OpenRouter key in `.env`, run locally, curl one request; confirm which providers give
  strict JSON for `qwen/qwen3.8-27b` and set `GATEWAY_OPENROUTER_PROVIDERS`.

## Open questions (answered 2026-11-07: human approved all recommended defaults)
1. Typed content endpoint, not an OpenAI-compatible inbound API; reword architecture.md. [yes]
2. Text limits: name 3..40, description 10..160, English ASCII only. [yes]
3. Rate limit 120/min, burst 30 per server id; timeout 20 s; 2 attempts. [yes]
4. Model `qwen/qwen3.8-27b` (D4); `require_parameters: true`; `only` empty until smoke test. [yes]
5. Temperature 0.7; `reasoning.effort: "none"` (set empty if the model rejects it). [yes]
6. Schema library Corvus.Text.Json.Validator (Apache-2.0), not JsonSchema.Net (fee). [Corvus]
7. No Sim reference now; a later /sim spec makes hash-from-tree public for verification. [yes]
8. Moderation stays a placeholder; O6 stays OPEN, decide before Early Access. [yes]
9. Readable mix/plate input order (alphabetical, hash unchanged) in a separate /sim spec. [yes]
10. No negative cache in the gateway; the game remembers failures per session (game-side follow-up spec). [yes]

## Implementation notes
- Packages: Corvus.Text.Json.Validator 5.8.8 (Apache-2.0; pulls in Corvus.Text.Json and
  Corvus.Regex.Ecma262, both Apache-2.0), Microsoft.AspNetCore.Mvc.Testing 10.0.15. Corvus gives
  error paths only through a results collector, which is not used: a failed attempt logs only the
  outcome code, never the output or its length. Corvus ships analyzer CTJ001 (prefer `u8`
  literals), which flows into both projects: the gateway uses `u8` literals, the tests set
  `NoWarn CTJ001`.
- Auth before rate limit: the built-in rate limiter middleware runs before endpoint filters, so the
  partition function calls the same `ServiceTokenFilter.IsAuthorized` and returns a no-limiter
  partition for a bad token or bad server id. Those requests get 401/400 and use no quota (tested).
  Token bucket: `TokenLimit = burst`, 1 token every `60 s / perMinute`.
- Upstream typed client: `RemoveAllLoggers()` (no HttpClient request logs at all),
  `HttpClient.Timeout` infinite, per-attempt timeout via a `CancellationTokenSource` on the
  injected `TimeProvider`. A malformed 2xx envelope counts as invalid output (retried). Network
  errors map to 502 `upstream_error`. The last attempt's failure picks the code.
- Stricter than the spec text: `tags` is required (may be `[]`); duplicate JSON properties are
  rejected in the request and in upstream output (.NET 10 `AllowDuplicateProperties = false`); a
  body over 8 KiB is 400 `invalid_request` (the table has no 413). Request regexes use `\z`,
  because .NET `$` also matches before a trailing newline. Whitespace around the JSON document
  is valid JSON and is accepted (that is parsing, not repair). `GATEWAY_MODEL` must not contain
  `|` or whitespace (the cache-key separator).
- Prompt messages are a `(Role, Content)` tuple list, so no extra message type file. Options
  validation is `GatewayOptions.Validator` (nested), wired with `ValidateOnStart`.
- D27 (vegan) was not in the spec. I added one sentence to the system prompt ("the game world is
  fully vegan: never mention meat, fish, seafood, eggs, dairy, honey or any other animal product").
  `ItemNamePrompt.Version` stays 1 because nothing has shipped. **Not enforced**: the placeholder
  moderator holds only profanity and slurs. A vegan word check (in moderation or validation) is a
  follow-up and a human decision (ties to O6).
- Tests: 119 (`dotnet test ai-gateway/Gateway.sln`). No network: the only base URL is
  `https://upstream.test/v1`, and the stub throws for any other host. verify.sh was run in the
  worktree as `env -u UNITY_PATH bash scripts/verify.sh`, so the Unity section was skipped. That
  section is unchanged.
- Smoke check: the built app started with test env vars. `/healthz` returned 200 and a POST
  without a token returned 401. Without `GATEWAY_SERVICE_TOKEN`, startup fails with
  `OptionsValidationException: GATEWAY_SERVICE_TOKEN is required.`

## Blockers
(Builder fills this in after 3 failed attempts.)

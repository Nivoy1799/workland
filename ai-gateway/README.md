# /ai-gateway

**Purpose:** a small C# / ASP.NET service (.NET 10, D25) between the game server and the
upstream LLM (OpenRouter now, D24; own vLLM later, D3). It exposes **typed content endpoints**
and builds the prompts itself from in-game IDs and computed properties; upstream it speaks the
OpenAI-compatible chat completions API. Output is flavor text only (D5). Spec: `specs/006-ai-gateway-skeleton.md`.

Pipeline for `POST /v1/content/item-name`: service-token auth, rate limit per server id,
request validation, cache lookup (stable hash of inputs), upstream call (at most 2 attempts),
strict JSON-schema validation (`schemas/`), moderation (placeholder, O6 OPEN), cache write,
response. Invalid output is rejected, never repaired. `GET /healthz` needs no auth.

## Layout

```
Gateway.sln
schemas/item_name.v1.schema.json     embedded into the build
src/Worktown.Gateway/                the service (Program.cs and one folder per pipeline step)
tests/Worktown.Gateway.Tests/        xunit; in-memory host with a stub upstream
tools/NameBake/                      the offline name bake (spec 056, see "Offline names")
offline/                             its data: reachable.v1.json, name-cache.jsonl, review.tsv (committed)
```

## Test

```bash
dotnet test ai-gateway/Gateway.sln      # fast loop
bash scripts/verify.sh                  # everything (section 3 runs the gateway tests)
```

Tests need no network and no key: the upstream is `StubUpstreamHandler` at
`https://upstream.test/v1`.

## Run locally with a real key

1. `cp .env.example .env` in the repo root (`.env` is gitignored) and fill in
   `GATEWAY_UPSTREAM_API_KEY` and `GATEWAY_SERVICE_TOKEN` (32+ random characters,
   e.g. `openssl rand -hex 32`).
2. Start it from the repo root (bash):
   ```bash
   set -a; . ./.env; set +a
   ASPNETCORE_URLS=http://127.0.0.1:8080 dotnet run --project ai-gateway/src/Worktown.Gateway
   ```
3. One request:
   ```bash
   curl -s http://127.0.0.1:8080/v1/content/item-name \
     -H "Authorization: Bearer $GATEWAY_SERVICE_TOKEN" \
     -H "X-Worktown-Server-Id: local-dev" -H "Content-Type: application/json" \
     -d '{"itemHash":"2ab1f86227c96055d1d69a98ce1485820e94a0805096d923ef51a6b4286857d3","canonicalTree":"fry(temp=hot,time=medium;cut(shape=sticks;potato))","fallbackName":"crispy fried potato sticks","tags":["fried"]}'
   ```
   Do not paste the key or token into chat or logs.

## Offline names

D38: no live gateway at Early Access. A developer runs the same pipeline once, on their own
PC, for every item the menus can produce; a human reviews the results; the approved names
ship with the game (spec `specs/056-offline-dish-names.md`). Moderation is still the
placeholder (O6 OPEN): real names ship only after the human's go-ahead.

Files in `ai-gateway/offline/` (committed; they hold no secret, only generated text, cache
keys and model ids):

| file | what |
|---|---|
| `reachable.v1.json` | every reachable item (hash, tree, plain name, tags, routes), from `scripts/item-list.sh` |
| `name-cache.jsonl` | the bake's cache, one JSON line per generated item (same cache key as the web gateway) |
| `review.tsv` | the review sheet: `key12, routes, target, plain_name, name, description, flags, decision, note` |

Steps, from the repo root (bash). The key lives only in `.env` (gitignored) or the shell;
the bake never needs `GATEWAY_SERVICE_TOKEN`.

1. `bash scripts/item-list.sh --out ai-gateway/offline/reachable.v1.json` (no key, /sim only;
   prints the count and the set hash).
2. `bash scripts/bake-names.sh bake --dry-run` shows how many items are cached and how many
   would be asked (needs only `GATEWAY_MODEL`).
3. `bash scripts/bake-names.sh bake [--limit 500]` asks for every item not cached yet, one call
   at a time (needs `GATEWAY_UPSTREAM_BASE_URL`, `GATEWAY_UPSTREAM_API_KEY`, `GATEWAY_MODEL`).
   It stops after `--limit` upstream misses (exit 0, run again to go on) or after 5 upstream
   errors in a row (exit 3). It prints counts only. Repeat until nothing is pending.
4. Review `review.tsv` in a spreadsheet or editor: set `decision` to `ok` or `no` on every row.
   Check that the name describes the item, is vegan (D27), names no real people, brands or
   real-world history, refers only to the game's own past (D28), and does not call a burnt or
   raw item golden or perfect (D21). `flags` only help: `doneness` (a doneness word of the
   plain name is missing), `same` (equals the plain name), `dup` (same name on another route),
   `long` (over 30 chars). You may edit a name or description; publish checks it again.
   On the next bake every `no` row is generated once more (counted in `--limit`); its old name
   moves to `note`. Decisions and edits are kept across bakes.
5. `bash scripts/bake-names.sh publish` writes `game/Assets/_Project/Resources/ItemNames.json`
   from the `ok` rows that are still reachable, after checking each against the schema, the
   vegan rule and the moderator (any failure: exit 2, nothing written). The same inputs give a
   byte-identical file. It prints published, pending, rejected and stale counts.
6. Commit the four files.

When cooking data changes, the /sim pin test fails with "reachable set changed": rerun
steps 1 to 6 (only new items cost money). A new prompt, schema or model changes every cache
key, so everything is generated and reviewed again.

## Environment variables

Configuration comes only from these variables (no appsettings.json values). A missing or
invalid value stops startup with an error that names the variable, never its value.

| var | default | rule |
|---|---|---|
| `GATEWAY_UPSTREAM_BASE_URL` | none | required, absolute https URL |
| `GATEWAY_UPSTREAM_API_KEY` | none | required, secret |
| `GATEWAY_MODEL` | none | required (no `\|` or whitespace) |
| `GATEWAY_SERVICE_TOKEN` | none | required, >= 32 chars, secret |
| `GATEWAY_UPSTREAM_FLAVOR` | `openrouter` | `openrouter` or `openai` |
| `GATEWAY_OPENROUTER_PROVIDERS` | empty | comma list for `provider.only` |
| `GATEWAY_REASONING_EFFORT` | `none` | empty means omit |
| `GATEWAY_TEMPERATURE` | `0.7` | 0..2 |
| `GATEWAY_UPSTREAM_TIMEOUT_SECONDS` | `20` | 1..120, per attempt |
| `GATEWAY_RATE_LIMIT_PER_MINUTE` | `120` | token replenishment per server id |
| `GATEWAY_RATE_LIMIT_BURST` | `30` | token bucket size |
| `GATEWAY_CACHE_MAX_ENTRIES` | `100000` | in-memory cap |

**Typically built by:** architect (Opus) writes the spec; builder on Sonnet for routine parts.

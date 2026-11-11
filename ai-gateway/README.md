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

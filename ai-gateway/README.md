# /ai-gateway

**Purpose:** OpenAI-compatible proxy between game servers and the upstream LLM.
Pipeline per request: rate limit, cache lookup (stable hash of inputs), upstream call,
JSON schema validation (`schemas/`), moderation, cache write. Invalid output is rejected,
never repaired. Upstream configured by `GATEWAY_UPSTREAM_*` env vars.

**Typically built by:** Claude writes the spec (language is decision O3, still OPEN);
Qwen Code for routine parts.

**How to test:** `bash scripts/verify.sh` (runs `npm test` or `pytest`, once the stack is chosen).

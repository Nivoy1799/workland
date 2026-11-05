---
id: 001
title: Repo skeleton and tooling
status: approved
owner: architect
builder: claude
---

# 001: Repo skeleton and tooling

## Goal
A git repo where every agent can start working: folder layout, verify script
running green, hooks active, rules in place.

## Scope
- Initialize git (if not yet), default branch `main`.
- Create empty folders with a short `README.md` each: `/sim`, `/game`, `/ai-gateway`,
  `/backend`, `/infra`, `/tools/bots`.
- Make `scripts/verify.sh` and `scripts/stop-hook.sh` executable.
- Root `README.md`: project name, one-paragraph description, link to AGENTS.md and docs/.

## Out of scope
- Creating the Unity project (human step, see spec 003).
- Any game code.
- CI pipelines (later spec).

## Design
Folder layout exactly as in AGENTS.md "Repo layout".
Each folder README states: purpose, owner (which agent typically builds here), how to test.

## Acceptance criteria
- [ ] AC1: `git status` works, initial commit exists on `main`.
- [ ] AC2: All six folders exist with a README.md.
- [ ] AC3: `bash scripts/verify.sh` exits 0 (everything skipped or passing).
- [ ] AC4: `.env` is gitignored: `git check-ignore .env` prints `.env`.

## Test plan
- Run `bash scripts/verify.sh` and `git check-ignore .env`.

## Human steps
- Create a private GitHub (or other) remote and push when you are happy.

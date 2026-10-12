# Bruno reliability correction

Status: Done — 2026-10-03. Scope: worktree-aware offline preflight and failure-first recovery rules. Changes are uncommitted. API/business behavior, worker order and coverage requirements were not changed.

- [x] BR-01 — Done — Inspect active worktree, existing local diff, helper and recorded HC-031/HC-033 failures. Active target is `.codex/worktrees/care-homes-main`, not the older detached root checkout. Preserve the pre-existing owner-deployment note in the Care homes execution handoff.
- [x] BR-02 — Done — Scout confirmed launcher ambiguity (verified direct Node/CLI 4.2), multipart method-selector trap, root helper port 5235 versus worktree guard 55819, non-repeatable onboarding fixtures and cascading assertions. File manifest is bounded to preflight/helper, focused tests and governing docs.
- [x] BR-03 — Done — Added `docs/tools/Invoke-BrunoPreflight.ps1`: pins worktree/HEAD/CLI, validates explicit paths and known multipart/URL guards, prints ordered command with correct Bruno working directory; no runtime actions. Corrected fixture helper port-conflict guidance.
- [x] BR-04 — Done — `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/BrunoPreflight.Tests.ps1`: 24 focused checks passed, exit 0. Test-author worker unavailable after worker usage limit; Mastermind authored/executed the offline checks. No independent test-author claim.
- [x] BR-05 — Done — Mastermind performed the bounded review after worker usage limit. Corrected root-versus-Bruno path handling, literal PowerShell command quoting, ancestor reparse-point checks, HTTP-request selection and multipart method-block scope. No independent reviewer claim and no additional review cycle.
- [x] BR-06 — Done — Mastermind completed documentation fallback: mandatory governance/workflow rules, Bruno runbook, onboarding/Admin/discovery README corrections and concrete preflight example. No API contracts changed; Postman is inapplicable.
- [x] BR-07 — Done — Three PowerShell files parse; 10 relative documentation links resolve; diff whitespace check exits 0 with Git line-ending notices. Actual installed CLI metadata and all four discovery paths pass offline preflight at HEAD `609077fb34346bf92066002570209c0066f663ab`, CLI 4.2.0. No endpoint runtime gate was run or claimed.

## Limits and next use

Preflight checks selected static request declarations; it is not a complete Bruno parser and cannot prove database identity, process ownership or fixture readiness. Runtime checks stay mandatory. The fixture helper still generates a fresh run identity on startup: the runbook explicitly prevents treating it as a stateless restart command. Existing authorization is reused within scope. Independent workers stopped after the implementer hit a usage limit; Mastermind finished the bounded implementation/check/documentation work without changing the standing worker sequence.

The pre-existing deployment note in `docs/operations/care-homes/Execution_Handoff.md` is untouched. Root checkout and other worktrees are untouched. Next Mastermind should use [the Bruno runbook](bruno-failure-first.md) for the current unfinished task and preserve valid completed runtime evidence.

No commits, database resets/migrations, server restarts, provider calls or process termination authorized/performed by this tooling correction. Do not repeat completed Care homes runtime gates.

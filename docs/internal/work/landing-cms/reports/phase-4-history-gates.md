# Phase 4 report — history/audit + guides + gates

Branch: `opencode/landing-cms`
Base: `main @ b0ac0d0`
Prior: `e63fa63` (Phase 3 closeout)
Final: `73af19b` (implementation; report committed separately after)

Scope per tasks.md Phase 4 (no provider calls, no migration applies, no
seed data — verification is automated gates only).

## Changed files

New:
- Domain: `LandingSectionHistory` (immutable; no update/delete paths),
  `LandingHistoryAction` (Published/VisibilityChanged/Deleted),
  `LandingSectionHistoryId`.
- `LandingSectionHistoryConfiguration` (`cms.landing_section_history`,
  no FK so rows survive section deletion) + migration
  `20261012071340_AddLandingSectionHistory` (+ Designer; snapshot updated).
  Authored via `dotnet ef migrations add`; NOT applied anywhere.
- `tests/.../Cms/LandingHistoryTests.cs` (6 tests).
- This report.

Edited:
- Publish/visibility/delete commands take `ActorUserId`; each writes one
  audit row atomically (actor, section, action, UTC; visibility records the
  value, deletes record the type key). Failed ops write nothing; empty
  actor rejected.
- Controller passes the authenticated admin id (401 guard on all three).
- Existing admin-test call sites updated for new signatures.
- `docs/admin/landing.md` (audit paragraph), Admin Postman (audit notes on
  the three mutating requests), `tasks.md`.

No new routes (frozen catalog unchanged); no history read endpoint by
design — history is write-only in v1.

## Gates (joint gate, run once)

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` → 0 warnings, 0 errors.
- Full unit suite: 2940 passed, 0 failed, 3 skipped (pre-existing
  Postgres-concurrency skips). Landing suites: 27/27 (Reads 8, Admin 9,
  Refs 4, History 6) with InMemory + SQLite twins throughout.
- Architecture suite: 1 passed, 0 failed.
- `git diff --check`: clean. Postman JSONs parse. Contract-map: 590
  requests, 0 missing / 0 orphan Postman, 0 orphan Bruno.

## Notes and deviations

- Per-type asset rules stayed at handler level (keeps domain `Publish`
  stable across phases).
- Two EOF-blank-line whitespace slips from scripted test edits were caught
  by `diff --check` and trimmed before commit.
- One Postman description edit collided mid-sentence; repaired precisely
  and re-validated.
- No push, no merge, no migration applied, no seeds, no live API in this
  phase (per its gate definition).

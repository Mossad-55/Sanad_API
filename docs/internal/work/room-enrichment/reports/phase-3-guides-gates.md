# Phase 3 report — guides + gates (room enrichment closeout)

## Identity

- Branch: `opencode/room-enrichment`
- Base: **`4e7f956`** (origin/main, unchanged)
- Phase 0: `759a5d2` · Phase 1: `cea3300` + `a861763` + tracking `e253716` ·
  Phase 2: `90ca26f` + tracking `c61fa85`
- Phase 3 (this phase): docs/Postman polish only — no code, no migration
  applies, no seeds, no provider calls, no live API (per tasks.md scope).
- Final deliverable SHA: the commit immediately after this report (see
  `git log`); phase content is the three files changed below.

## Delivered

1. **Guide caveat (owner-approved):** `docs/care-homes/inventory-and-availability.md`
   and `docs/care-homes/discovery.md` now state that `coverUrl`/`coverImageUrl`
   are metadata only — no GET file route serves them (live-404 confirmed in
   Phase 2 check #16), the contract stays frozen per owner decision 5, and
   clients must not render covers until a serving route ships.
2. **Postman polish:** Owner collection folder "04" — the `Rename room` example
   body no longer sends `roomTypeId`, demonstrating the frozen contract's
   optional-type semantics (omitted = no change; the request description already
   explains both variants). All Phase 1–2 requests keep their descriptions;
   legacy HC-023 requests without descriptions were left untouched (out of
   manifest scope). Arabic example text verified as real UTF-8 in the file
   bytes (console-only rendering substitutes `?`).

## Gate battery (final tree, once — publish.md canonical commands)

| Gate | Command / scope | Result |
|---|---|---|
| Whitespace | `git diff --check origin/main...HEAD` | exit 0 |
| Full build | `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` | **0 warnings / 0 errors** |
| Full unit suite | `dotnet test tests/Sanad.UnitTests/... --no-build --no-restore --nologo -v minimal` | **2986 passed / 0 failed / 3 skipped** (3 = pre-existing PostgreSQL-only static skips in `SubscriptionTaxRuleTests`) |
| Architecture suite | `dotnet test tests/Sanad.ArchitectureTests/...` | **1/1 passed** |
| Postman JSON | `python -m json.tool` on Owner collection | exit 0 (607 requests) |
| Contract map | `docs/internal/tools/Verify-ApiContractMapping.ps1` | **Postman 0/0 absolute** (607 requests); orphan Bruno 0; missing Bruno **139** = origin/main baseline **135** + **4** waived (ruling A; script exit 1 is the waived-Bruno condition, unchanged from Phase 2) |

## Carried-forward known notes (unchanged, out of scope)

- Cover serving gap: `coverUrl`/`coverImageUrl` have no GET file route (404);
  contract frozen per owner decision 5 — now also documented in both guides.
- `CareHomeReviewNotificationProcessor` (HC-013) and the
  `CareHomeBookingNotificationProcessor` retry noise are pre-existing LINQ
  translation issues, untouched by this slice.
- Bruno coverage for the 4 media endpoints stays waived (ruling A) in favor of
  the Phase 1/2 live multipart proofs.

## Package state

All three phases delivered on `opencode/room-enrichment`:
`759a5d2` → `cea3300` → `a861763` → `e253716` → `90ca26f` → `c61fa85` →
(phase 3 content) → tracking commit. Reports for `phase-1-types`,
`phase-2-rooms`, and `phase-3-guides-gates` exist with gate evidence; boxes
checked; ready for integrator discovery. Workers never merge or push.

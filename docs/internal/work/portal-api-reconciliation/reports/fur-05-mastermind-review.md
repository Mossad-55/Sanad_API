# FUR-05 — Mastermind review

Date: 2026-10-17 (Africa/Cairo). **Status: Accepted for release; deployment is pending.**

Traceability: [owner-approved contract](../contracts/fur-05-owner-settlements.md) →
[worker prompt](../worker-prompts/fur-05-owner-settlements.md) →
[worker delivery](fur-05-worker-delivery.md) → this Mastermind review.
Reviewed at base `75eb7a412fdb177398f53d76eee20955ecbeb1c4` plus the FUR-05
worktree delta.

## Review findings

No implementation or test defect found. The route uses the controller's existing
class-level `CareHomeOwnerAccess` policy, derives facility scope from the
authenticated actor, and accepts no caller-selected facility ID. The owner
projection returns recorded payouts and only currently eligible positive unpaid
rows, while preserving checkout-time-descending ordering and the existing ledger
shape. Admin ledger behavior and payout-writing handlers remain unchanged. The
tests cover owner/facility isolation, recorded and unpaid rows, debt, exclusions,
empty/no-facility results, missing Finance configuration, and read-only behavior.

One documentation wording correction was applied to the Postman request: its
description now states that rows are all ordered by checkout time, without
implying grouping by payout status. The saved response example parses to two
rows and matches the endpoint and DTO fields.

## Mastermind verification

- Strict solution build:
  `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true`
  — exit 0; 0 warnings, 0 errors.
- Worker focused tests:
  `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true --filter "FullyQualifiedName~Sanad.UnitTests.CareHomes.CareHomePayoutTests|FullyQualifiedName~Sanad.UnitTests.API.CareHomeBookingControllerTests|FullyQualifiedName~Sanad.UnitTests.API.CareHomeOwnerPortalTests"`
  — worker-reported 56 passed, 0 failed, 0 skipped.
- Guarded full Unit suite:
  `pwsh -NoProfile -File docs/internal/tools/Test-SanadPortalGate.ps1 -Suite Unit`
  — after a testhost connection timeout on the restricted first attempt, the
  normal-access retry passed: 3,482 passed, 0 failed, 31 predeclared skips.
  The guard confirmed integration targets absent, nine module targets isolated,
  and migrations/seeders disabled in the test child.
- Guarded Architecture suite:
  `pwsh -NoProfile -File docs/internal/tools/Test-SanadPortalGate.ps1 -Suite Architecture`
  — after the same first-attempt testhost timeout, the normal-access retry passed:
  1 passed, 0 failed, 0 skipped.
- Postman collection and embedded two-row response parsed successfully.
- `powershell -NoProfile -ExecutionPolicy Bypass -File docs/internal/tools/Verify-ApiContractMapping.ps1`
  reported 557 controller method signatures and 621 Postman API requests. Its
  printed missing/orphan counts are not used as independent evidence because the
  helper's comparison collections are unpopulated. The prior accepted normalized
  route-set baseline was 556 unique methods/paths on each side; this phase changes
  one controller route and adds one matching Postman request, making the unique
  sets 557/557 with no other route delta.
- `git diff --check` — exit 0; only Git's informational LF-to-CRLF notices.

No Bruno/live HTTP test was selected. No API was started, and no database,
migration, provider, configuration, or runtime state was touched. The change has
no migration. Deployment remains subject to the fresh package, target preflight,
and deployment verification recorded in the release receipt.

## Decision

FUR-05 implementation and documentation are accepted for release. FUR-06 remains
deferred at the owner's direction and is not dispatched by this review.

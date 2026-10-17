# Admin portal — FUR-04 Care Home readback — revision media, dispute, owner/actor display

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-04.
Purpose: backend contract delivery notice; client adoption unconfirmed.
Audience: Admin portal (Admin audience = Admin portal + Care Home Owner portal).
Communication state: worker delivery complete; Mastermind review and owner release approval pending — not yet forwarded as accepted.

No frontend repository was inspected. No frontend code defect is claimed.
This notice communicates the delivered backend readback contract and what
admin-portal clients should rely on once released. No new endpoint, route,
policy, or lifecycle change is included.

## Baseline and authority

- Original intake and issue IDs: audit items `#25` (admin revision media),
  `#27` (dispute display), `#28` (application owner display), `#29` (review
  actor display) from the A3 missing-fields audit.
- Canonical plan/approved contract: [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) (owner-approved 2026-10-16, including the test-schema addendum).
- Source baseline: `14677d961813a94be602390e7801623c766ad53c` (`main == origin/main`, clean at phase start).
- Delivery report: [fur-04-worker-delivery](../../../../internal/work/portal-api-reconciliation/reports/fur-04-worker-delivery.md) (worker evidence; Mastermind review pending).
- Deployment status: **not performed — awaiting Mastermind review and owner release approval.**
- Evidence limits: the original phase included focused unit proof plus
  disposable-PostgreSQL proof (7/7 tests passed against
  `SanadFur04VerifyDb`); that PostgreSQL proof was not rerun during C1. C1's
  later focused correction run passed 131/131 tests (131 passed / 0 failed / 0
  skipped). No frontend repository, provider, Bruno, or production run.
  Frontend repositories inspected: none.

## What is delivered and should be relied on

| Issue | Correct behavior/usage | Evidence |
| --- | --- | --- |
| #25 | `GET /api/v1/admin/care-homes/{careHomeId}` now embeds each revision's ordered media metadata: `id`, `kind`, `position`, `contentType`, `length`, and a `uri` pointing at the existing Admin file route `GET /api/v1/admin/care-homes/{careHomeId}/media/{mediaId}/file`. Storage keys are never serialized. Owner and public file routes keep their existing authorization; public access stays limited to the approved revision. The Admin/owner/public file-guard predicates were corrected to a translatable shape after exact Npgsql proof showed the previous correlated form fails translation; allow/deny semantics and policies are unchanged. | Worker delivery §§2–3, §6; `AdminCareHomeApplications.cs`; `CareHomeProfileMediaController.cs`; `CareHomeReadbackPostgresTests` (PostgreSQL passed) |
| #27 | The Admin **list** `GET /api/v1/admin/care-homes/check-in-disputes` now carries the booking's stored `residentArabicName`/`residentEnglishName` snapshots and the original booking `originalBookingTotalAmount` in `originalBookingCurrency`, which carries the current fixed Care Home currency convention (`EGP`); the booking does not persist a currency property. The amount remains the booking's original `TotalAmount` — not a refund, payout, or net amount. The Admin resolve and Family submission responses keep their pre-existing shapes. Dispute status/history fields and `CareHomesOperationalAdmin` authorization are unchanged. No owner dispute list or resolution power is added (owner dispute list remains FUR-08). | Worker delivery §§2–3; `CareHomeStayCommands.cs`; original 7/7 disposable-PostgreSQL proof (not rerun during C1); C1 focused correction run: 131 passed / 0 failed / 0 skipped |
| #28 | Admin application list/detail now carry nullable `ownerArabicName`/`ownerEnglishName` account display names. `OwnerUserId` is preserved. Missing or anonymized identities yield null enrichment. Owner phone is intentionally not exposed (the user-detail API is SuperAdmin-only while operational Admin includes SupportAdmin). | Worker delivery §§2–3; `AdminCareHomeApplications.cs` |
| #29 | Admin application review history keeps actor IDs and event ordering and adds nullable `actorArabicName`/`actorEnglishName` plus separately named `actorCurrentRoles`: current account roles at read time, not the role held when the event occurred. Missing/anonymized actors yield null enrichment without breaking history. No historical role snapshots or migration were added. Owner history shape is unchanged. | Worker delivery §§2–3; `AdminCareHomeApplications.cs` |

| Route | Verb | Policy | Status behavior |
| --- | --- | --- | --- |
| `GET /api/v1/admin/care-homes` | GET | `CareHomesOperationalAdmin` (SuperAdmin + SupportAdmin, normal access) | 200 page with `ownerArabicName`/`ownerEnglishName` per item; invalid paging/status → 400 `CareHomes.Admin.InvalidQuery` |
| `GET /api/v1/admin/care-homes/{careHomeId}` | GET | `CareHomesOperationalAdmin` | 200 detail with per-revision `media[]` and enriched review history; unknown ID → 404 `CareHomes.Admin.ApplicationNotFound` |
| `GET /api/v1/admin/care-homes/check-in-disputes` | GET | `CareHomesOperationalAdmin` | 200 newest-first cases with resident snapshots and original booking amount/currency (Admin list only; resolve and Family submission shapes unchanged) |
| `GET /api/v1/admin/care-homes/{careHomeId}/media/{mediaId}/file` | GET | `CareHomesOperationalAdmin` | 200 private image bytes for own-revision media; foreign media → 404; predicate shape corrected, policy unchanged |

## What needs correction or backend work

No open correction is known on these reads. Bounded follow-ups stay in their
own phases: owner dispute list (FUR-08); any owner-phone exposure needs an
explicit owner decision and scoped access design; historical role snapshots
would need a migration and are not included.

## Implementation connection

| Issue | Contract | Implementation/revision | Verification | Deployment | Remaining action |
| --- | --- | --- | --- | --- | --- |
| #25 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | `CareHomeAdminRevision.Media` + Admin file-route URI; file-guard predicate correction in `CareHomeProfileMediaController.cs` (same allow/deny, translatable shape) | Focused unit + 7/7 disposable-PostgreSQL proof; strict build 0 warnings/errors; guarded full unit 3473/0/31; architecture 1/1 | Not performed — awaiting Mastermind review and owner release approval | After release, render per-revision media from `media[].uri`; never parse storage keys |
| #27 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | `AdminCareHomeCheckInDispute` resident snapshots + `originalBookingTotalAmount`/`originalBookingCurrency` | Original focused unit + 7/7 disposable-PostgreSQL proof (not rerun during C1); C1 focused correction run: 131 passed / 0 failed / 0 skipped | Not performed — awaiting Mastermind review and owner release approval | Display the original booking total as labeled; do not substitute refund/payout/net figures |
| #28 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | Nullable `ownerArabicName`/`ownerEnglishName` on list/detail via Identity display lookup | Focused unit (incl. real-Identity SQLite twins) + 7/7 disposable-PostgreSQL proof | Not performed — awaiting Mastermind review and owner release approval | Render names when present, blank state when null; do not request phone |
| #29 | [FUR-04 contract](../../../../internal/work/portal-api-reconciliation/contracts/fur-04-care-home-readback.md) | `CareHomeAdminReviewHistory` with `actorArabicName`/`actorEnglishName`/`actorCurrentRoles` (current-at-read) | Focused unit incl. live role-change proof + 7/7 disposable-PostgreSQL proof | Not performed — awaiting Mastermind review and owner release approval | Label roles as current; never present them as the historical decision-time role |

The linked delivery report is backend evidence, not proof of client adoption.

## Decisions and evidence requested

- Owner decisions: release approval for this phase is still required; no new scope is requested.
- Frontend evidence: confirm the admin portal renders the new nullable fields with null-safe fallbacks and labels `actorCurrentRoles` as current roles.
- Acceptance: no new endpoint; existing routes/policies/DTO meanings unchanged; additive nullable fields only.
- Configuration/migrations: none required (no migration or config change in this phase).
- Next action: Mastermind review, then owner release decision; owner forwards this notice after acceptance. No frontend code defect is asserted.

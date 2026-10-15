# Portal frontend handoff — Admin and Care Homes API reconciliation

Source: integrated `main` at PORTAL-07 delivery (plan:
`docs/internal/work/portal-api-reconciliation/plan.md`; evidence:
`reports/phase-01-reactivation.md` through `reports/phase-07-integration.md`).
Local verification only — nothing here proves deployed behavior. Production
revision remains last verified `8cbeedd` until the owner reports a new
deployment. No publish or deploy is authorized by this handoff.

## 1. Reactivation (PORTAL-01)

- Missing activation readiness now returns **409
  `Identity.Admin.ActivationNotReady`** with a safe message and trace ID —
  no more HTTP 500. The target status is unchanged; no audit/notice row and
  no session restore is created.
- Pending users must still complete normal OTP verification; admins cannot
  force-verify or force Active. Verified Suspended/Blocked users still
  reactivate to Active with 200.
- Frontend: surface the safe 409 detail and guide the user through required
  verification/account setup. It does not identify a specific unverified
  channel; do not infer one or retry reactivation as success.

## 2. Library (PORTAL-02)

- Section keys accept **camelCase and legacy PascalCase**; strict JSON
  numbers are preserved (quoted numbers are rejected).
- Article/Video business rules kept: positive unique display orders, at
  least one Article section, positive Video duration, field length limits.
- Failures return field/indexed `errors` plus trace ID under the existing
  `Cms.Library*` codes. Omitted multipart `sectionsJson` keeps existing sections;
  explicit `[]` clears to empty (still rule-checked); `[null]` is 400.
- Canonical examples: `docs/app/library/README.md` and the Admin Postman
  Library folder (examples are exercised locally).

## 3. OpenAPI accuracy (PORTAL-03)

- All **28 previously-missing success schemas** are published from actual
  handler DTOs (see the required inventory table in `plan.md`); create-admin
  correctly documents **201**.
- Five parent/date query parameters (`through`, `governorateId` ×2,
  `cityId` ×2) are flagged required. Omitted/empty parent IDs return 400 with
  validation field errors/trace IDs; malformed IDs return binding 400.
  Unknown but well-formed parent IDs return **200 `[]`**, not 400.
  Omitted license-expiration `through` returns 400 `CareHomes.Admin.InvalidQuery`.
- Enums stay **numeric on the wire** with published values plus names;
  wrapper IDs (e.g. `GovernorateId`) keep their `{ value }` shape with
  `value` now required. No global converter migration.
- Community category update: route is authoritative
  (`PUT /api/v1/admin/community/categories/{categoryId}`); the body has no
  `Id` (legacy bodies carrying an ignored `Id` remain accepted).
- Finance charge-rule current read: **204** when no rule exists, **200**
  with the rule DTO once configured.

## 4. Care Homes owner routes (PORTAL-05, verified locally with real handlers)

- Covered: `GET facilities/mine`, `GET inventory/mine`,
  `GET booking-requests/mine`, `GET visits/mine`, `GET dashboard/mine`,
  `GET revenue/mine`, `GET/PUT facilities/mine/visit-settings`.
- Reads work for **Draft and approved** facilities (no approved-only gate);
  an owner with no facility gets **404** (`CareHomes.Facility.NotFound`,
  `CareHomes.Bookings.NotFound`, `CareHomes.Visits.NotFound` per route).
  Empty lists/views return 200 with empty arrays/zero totals, not 404.
- Ownership is strictly scoped: a second owner's facility, inventory,
  bookings, visits, and settings never leak into `/mine` reads, and failed
  writes leave the target and foreign settings/versions untouched.
- Settings PUT uses `expectedVersion`; stale versions get **409
  `CareHomes.Visits.Conflict`**. Capacity below 1 is **400
  `CareHomes.Visits.InvalidRequest`**. **Days are numeric `DayOfWeek`
  (1 = Monday)** — string names such as `"Monday"` are rejected with 400.
- Revenue `from`/`to` are inclusive Cairo-local dates; a reversed range is
  **400 `CareHomes.Revenue.InvalidRange`**.
- Auth: anonymous 401; non-owner roles (including SuperAdmin) 403 on owner
  routes. Synthetic Postman placeholders are in `Sanad.CareHomes.Owner`
  and `Sanad.CareHomes.Finance` collections (authored examples, not live
  evidence).
- Limits: visit-write concurrency still relies on the production Postgres
  advisory lock (not proven by isolated tests); CSV export, receipts,
  notes, decisions, stay operations, transfers, maintenance, availability,
  and media/detail routes were outside this verification.

## 5. Intentionally unchanged (do not "fix" from the frontend)

- Unset support contact stays **404 `Cms.Help.SupportContactNotFound`**
  (the "not configured" state, documented in `docs/admin/help-center.md`).
- Admin calls to app-audience Help/Legal stay **403
  `Cms.Content.UnsupportedAudience`** — use the Admin management routes.
- Existing numeric-enum and wrapped/plain-ID wire conventions are
  preserved (PORTAL-03); CORS stays owner-confirmed unchanged.

## 6. Open items (not complete, not waived)

- Deployed caregiver data: six reported Identity-mismatched rows remain
  **unverified** — see `diagnostics/caregiver-identity.sql` and
  `diagnostics/caregiver-identity-runbook.md`. No repair authorized yet.
- Reference geography (PORTAL-06): owner accepted a provisional V1 catalogue
  (27 governorates / 347 Cities / 5712 Areas); approved display cleanup and
  standalone importer are locally tested, **not applied to the VPS**. Existing
  deployed lookup coverage remains fixture-thin until an authorized import.
  `CareHomeProfileDraft` still uses location **strings**; no wire change.
  See [importer closeout](reports/phase-06-importer.md). Disposable local PostgreSQL
  rehearsal passed (full import and zero-insert repeat); actual target
  preview/reconciliation and backed-up maintenance-window apply remain.
- Untested nested/detail/mobile endpoints; deferred landing-media cleanup.
- Chat Plus runtime/device/soak gates remain separate (Prompt 7).

## 7. Release handoff (owner order only)

1. Owner packages **only** the reviewed pushed `main` revision and deploys.
2. After the deployment report: anonymous revision check
   (`/api/v1/deployment/info`), applied-migration check (nine required
   heads), and smoke GETs (OpenAPI, location lookups).
3. Authenticated verification only over **HTTPS or an approved SSH
   tunnel** with designated test actors — never send tokens over public
   HTTP. Bruno remains excluded by owner direction.

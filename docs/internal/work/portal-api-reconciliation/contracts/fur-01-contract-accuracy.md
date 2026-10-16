# FUR-01 — Existing API contract accuracy

Date: 2026-10-16 (Africa/Cairo).
**Approval state: owner-approved 2026-10-16; execution In progress.**

The owner separately approved this exact contract after approving the 19-phase
roadmap. Only FUR-01 execution is authorized here. The source revision and
documentation delta below remain the intake baseline; gates will be recorded
in the closeout report, not assumed from approval.

Owner terminology: Admin = Admin portal + Care Home Owner portal; App = Family,
Caregiver and Elderly clients. Those audience labels do not alter authorization.

Plan: [canonical FUR sequence](../plan.md#frontend-follow-up-sequence--2026-10-16).
Evidence: [46-item intake audit](../frontend-report-audit-2026-10-16.md).
Source inspected for this contract: `4b65369d760c8af6b2242ebd2022a6cb10057580`.
Local documentation delta exists; preserve it and the root frontend intake.
Repin the actual HEAD and delta before any worker starts.

## 1. Outcome and approval boundary

Make existing API behavior discoverable and unambiguous for the frontend without
changing that behavior. Fix wrong examples, add missing success response metadata
and publish existing enum/role/limit rules with literal contract tests.

Approval of this contract authorizes worker metadata annotations, scoped guide/
Postman corrections, isolated tests and the related handoffs. It does **not**
authorize new endpoints, runtime fixes, role changes, token/ID/enum serialization
changes, new validation, migrations, data repair or external sending. Workers
cannot commit/push/publish/deploy. The Mastermind's later accepted-phase release
workflow is recorded in section 6. No VPS configuration is needed for this slice.

Intake coverage: #30–35 and existing-contract portions of #37, #39, #43, #45,
#46; A6's photo clarification. #34 is bounded by the 29 operations below, not a
repository-wide schema rewrite. Earlier resolved #4/#6/#36 remain evidence links,
not reopened implementations. Current profile readback, OTP reliability/recovery,
media query fixes and payout/moderation features remain in their own FUR slices.

## 2. Existing contract rules to preserve

- Register uses `arabicFullName`, `englishFullName`, `email`, `phoneNumber`,
  `password`, `accountType`; verify uses `verificationRequestId` + `code`;
  resend uses `verificationRequestId`. Do not document email/purpose payloads.
- Publish complete actual register/verify/resend/login/refresh response examples
  from the existing DTOs and MVC serializer, including wrapper IDs, nullable
  session/refresh values and verification flags. Restricted login still has no
  verification request IDs; do not promise the future FUR-02 recovery feature.
- Request GUIDs remain plain GUIDs even where response IDs are `{value: guid}`.
  Headers such as `X-Device-Session-Id` use the inner GUID, not a JSON wrapper.
- Existing response property spellings remain unchanged, including the current
  verification response's `attemptesRemaining`; document literal truth rather
  than silently renaming a public field in this metadata slice.
- Numeric enums stay numeric. Use existing CLR definitions/transformer and explain
  names plus values; do not install global converters or merge business enums.
  Keep BookingStatus 1–10 distinct from CareHomeBookingStatus 1–9. SOS history
  action is not SOS status. DayOfWeek is 0 Sunday through 6 Saturday.
- Help aggregate remains a dictionary. Prove actual enum-key serialization using
  MVC options before publishing a literal example; do not guess or add an envelope.
- Keep existing success/error runtime behavior, empty states, auth/ownership and
  revision rules. Platform-charge current remains 204 when empty / 200 populated;
  support contact's unconfigured 404 remains intentional.
- Certificate files use CaregiverReviewAdmin (SuperAdmin + SupportAdmin), not
  CaregiversAdmin. Preserve finance-rule versus payout-role distinctions.
- Publish existing resource-specific limits: Care Home required review/document
  rejection reason 1000; caregiver payout-account reason 500; caregiver payout
  reason/evidence 2000 and reference 200. Do not alter validators/domain limits.
- Assessment illustrations accept JPEG/PNG/WebP, not SVG. Explain multipart-size
  overhead; do not increase request/file limits. Submission still requires all
  four document types in the current revision plus two profile photos including
  cover; required verification for approval is a separate state.

## 3. Exact response metadata inventory — 29 existing operations

Use `ProducesResponseType` over the existing DTOs. Attribute/import edits only
in production controllers; no controller method-body, mapper, handler or domain
changes. Existing file/CSV operations outside this matrix remain untouched.

All routes below have prefix `/api/v1`.

| Controller action / method and suffix | Count | Success contract |
| --- | --- | --- |
| AdminElderlyHelpRequests: GET `/admin/elderly/help-requests/{requestId}` | 1 | 200 `ElderlyHelpRequestResponse`. |
| Same: GET `/{requestId}/history` | 1 | 200 `IReadOnlyList<ElderlyHelpRequestHistoryResponse>`. |
| CareHomeInventory: GET `/care-homes/inventory/mine`; CreateType, UpdateType, ArchiveType, CreateRoom, RenameRoom, ArchiveRoom, CreateBed, RenameBed, ArchiveBed, Maintenance, AmendMaintenance, CancelMaintenance | 13 | 200 `CareHomeInventoryDto`; preserve each existing method/path/binding. These 13 include Mine. |
| Same: UploadTypeMedia and UploadRoomMedia | 2 | 201 `CareHomeRoomTypeMediaDto` / `CareHomeRoomMediaDto` respectively. |
| Same: RemoveTypeMedia and RemoveRoomMedia | 2 | 204, no body/schema. |
| Same: POST `/care-homes/inventory/mine/availability` and `/care-homes/inventory/admin/{facilityId}/availability` | 2 | 200 `IReadOnlyList<CareHomeAvailability>`. |
| CareHomeBookingRequests.List: GET `/care-homes/booking-requests/mine` | 1 | 200 `IReadOnlyList<CareHomeBookingListItem>`. |
| CareHomeVisits.OwnerList: GET `/care-homes/visits/mine` | 1 | 200 `IReadOnlyList<CareHomeVisitResponse>`; nested visit enums become discoverable. |
| Same: GET/PUT `/care-homes/facilities/mine/visit-settings` | 2 | 200 `CareHomeVisitSettingsResponse`. |
| CareHomeFinance: GET `/care-homes/dashboard/mine`, `/care-homes/revenue/mine` | 2 | 200 `CareHomeDashboard` / `CareHomeRevenueReport` respectively. |
| Notifications.UnreadCount: GET `/notifications/unread-count` | 1 | 200 bare `int`. |
| AdminFinance.Create: POST `/admin/finance/platform-charge-rules` | 1 | 201 `Guid`; remove misleading default success-200 documentation without changing runtime. |

Total: 29 operations across seven controllers. Already typed admin inventory,
finance-current/history, original PORTAL-03 28-operation matrix, admin Care Home
bookings/ledger and caregiver payout-policy operations are regression controls,
not additional mutation scope. Booking operational/receipt/note endpoints and
visit decision/cancel metadata are outside this bounded inventory.

Error behavior is not redesigned. Do not invent uniform problem bodies/statuses
in prose or metadata. Keep existing documented errors; add an error annotation
only when the current action/pipeline and an isolated test prove its exact
status/body. New runtime discrepancies must be reported for separate scope.

## 4. Owned file manifest

Production changes permitted only in these seven files under
`src/API/Sanad.API/Controllers/`:

- `AdminElderlyHelpRequestsController.cs`, `CareHomeInventoryController.cs`,
  `CareHomeBookingRequestsController.cs`, `CareHomeVisitsController.cs`,
  `CareHomeFinanceController.cs`, `NotificationsController.cs`, `AdminFinanceController.cs`.

Tests under `tests/Sanad.UnitTests/API/`:

- New `PortalRemainingResponseContractTests.cs` for the 29-operation matrix and
  success status/schema assertions, with nested component resolution.
- New `PortalAuthExampleContractTests.cs` for literal documented auth request/
  response shapes and dictionary/enum/ID representation checks.
- Reuse existing PortalApiMetadataHost/isolation tests and owner portal fixtures;
  do not create a second host. Any required host/fixture change is a scope finding
  to review before editing, not a reason to weaken isolation.

Documentation edit allowlist (edit only contradicted sections; link rather than
duplicate already accurate rules):

- `docs/auth/registration-and-verification.md`, `email-password-login.md`,
  `refresh-and-sessions.md` in the same directory.
- `docs/admin/api-contract-conventions.md`, `caregivers-review.md`,
  `care-assessments.md`, `care-homes.md`, `elderly-help-requests.md`,
  `finance-platform-charges.md` in the same directory.
- `docs/care-homes/owner-onboarding.md`, `inventory-and-availability.md`,
  `visits.md`, `finance-reporting.md` in the same directory.
- `docs/app/caregivers/payouts.md`, `docs/app/notifications.md` for existing
  payout limits and unread-count truth only.
- Existing Postman collections: `docs/postman/Sanad.Auth.postman_collection.json`,
  `docs/postman/admins/Sanad.Admin.postman_collection.json`,
  `Sanad.Admin.Finance.postman_collection.json`,
  `Sanad.Admin.CareHomes.postman_collection.json` in that admins directory;
  `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` and
  `Sanad.CareHomes.Finance.postman_collection.json` in that care-homes directory.
  Restrict edits to relevant existing request descriptions/examples. No tokens,
  environments, execution scripts, guessed routes or live response captures.

Delivery documentation:

- New internal `reports/fur-01-contract-accuracy.md` with exact diff/gates/limits;
  update FUR-01 status and short master handoff/index pointers.
- Admin delivery reports under `docs/frontend-handoffs/admin/admin-portal/` and
  `admin/care-home-owner/`, phase `phase-01-contract-accuracy`, dated topic files.
- A shared App contract notice under `docs/frontend-handoffs/app/shared/` may
  cover auth/unread-count conventions only. Say no mobile frontend was audited.
  Register issued files in the handoff index and link original intake IDs.

Do not edit any other production files, schema transformers, DI/auth policies,
error mappers, config, migrations, handlers, domain types or generated model
snapshots. Preserve existing dirty documentation and user intake. Expand this
manifest only through a reported, reviewed concrete need.

## 5. Fixture and test contract

Before launching any host, inspect the existing isolated host: nine dummy
connection keys; nine InMemory contexts replacing eagerly captured registrations;
migration/seed flags off; production background jobs removed; temporary storage;
ephemeral DataProtection. Validate inherited integration targets are absent in
the **same child process** that runs the selected tests.

- Metadata GET tests are business-fixture-free. Use real generated OAS, not only
  reflection tests over attributes. Record pre-change failures where relevant.
- Literal MVC serialization samples are synthetic, contain no real tokens/PII,
  and use production JSON options. Compare request examples to actual binding
  DTOs; response examples must not depend only on a handwritten expected schema.
- For representative HTTP evidence use deterministic dispatch fakes or the
  existing minimal InMemory fixtures: seeded records read back before request,
  no new real DB connections/providers. Cover inventory DTO 200, both media
  types' 201, removals' empty 204, nonzero and zero unread integers, finance
  create's 201 GUID, help detail/history and owner read DTO serialization.
- Metadata assertions cover all 29 methods/statuses/shapes, resolved child enum
  definitions, no schema/body on 204 and no spurious success-200 for 201 actions.
- Preserve existing 28-operation matrix, nullable enums, ID wrappers, query flags
  and finance 204/200 tests. No global JSON changes or role/lifecycle changes.
- Provider behavior, PostgreSQL query execution, SMS reliability, live startup
  and VPS smoke are not claimed. Those belong to later approved slices.

Acceptance gates after the final edit:

1. Strict solution build: successful exit, zero warnings/errors.
2. Focused new contract tests plus existing Portal isolation/OpenAPI/serialization
   and relevant owner fixture tests: no failures, skips explained, zero warnings.
3. Architecture test; Postman JSON parse and Postman-only controller mapping gate
   via `docs/internal/tools/Verify-ApiContractMapping.ps1`. Bruno is excluded;
   inspect invocation/options before running rather than assuming its defaults.
4. Literal guide examples match test evidence; relative links, whitespace and
   diff clean. No route signatures added/removed or runtime-body changes.
5. Final audience reports clearly distinguish deployed historical behavior from
   new metadata/docs tested locally and not yet published/deployed.

No full suite, live DB, destructive fixture, signup, OTP, provider or VPS run is
required for this bounded slice. Run additional checks only if an actual
cross-cutting change is proposed and approved. A static/provider failure is
reported honestly; it does not authorize broad runtime refactoring.

## 6. Execution and closeout

After owner approval, issue one pinned worker brief using this contract. The
worker is not alone, preserves unrelated edits, works under the standing main
instruction and does not start FUR-02. Worker owns all phase implementation,
tests, feature docs/Postman and delivery report; Mastermind owns planning,
review/integration and release. Any needed implementation correction goes back
to the worker, not silently to the planning Mastermind.

Owner subsequently established the complete-phase release workflow: worker
reports; Mastermind reviews and resolves corrections; Mastermind commits/pushes
the accepted whole phase, publishes/deploys and verifies before the next phase.
Worker never commits/pushes/deploys or marks final Done. This standing instruction
updates the earlier separate-release-approval wording for accepted phases, but
does not authorize partial releases, resets, provider sends, unrelated work or
new target/config/policy decisions. No deployment occurs during worker execution.

**Configuration delta: none. Database/migration delta: none. New routes: none.
Runtime behavior changes: none.** The OpenAPI document will change, intentionally,
to describe existing runtime truth. Notify frontend type-generation users of
that documentation change without promising a new wire-format version.

Owner lock recorded: approved after the explicit request to approve the FUR-01
contract. Subsequent detailed phase contracts remain unapproved. The Mastermind's
release workflow is conditional on full-phase acceptance and preflight, not on
merely receiving a worker completion claim.

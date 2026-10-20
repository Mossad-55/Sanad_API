# F6 — Minimal operational reads (A8/A15)

Phase: `accepted` (Owner authorized); task: `completed`; release: authorized for the
delegated release executor. The Worker never commits, publishes or deploys. Base: pushed
`main` HEAD recorded at dispatch. Worker B:
`river/deepseek-ai/DeepSeek-V4.1-Flash#high`; the dispatched session is recorded in Current
result.

**Mastermind acceptance (2026-10-20):** phase `accepted`; task `completed`; release
`authorized`. Accepted after F5 released the runtime slot and against these actual gate
results: strict `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true` → 0
warnings / 0 errors; the four F6 focused classes → 31/31 passed; the whole unit project →
4021 passed with only the pre-existing baseline `NestedResponseRefs_ResolveToActualShapes`
failure retained and untouched; and `tools/Invoke-F6LocalHttpVerification.ps1` → exit 0 with
27/27 real localhost HTTP checks. The gate exposed two delivery defects and a fragile
fixture choice; the Mastermind corrected all three (see Current result). F6 adds no
migration, so its release is a code-only deploy.

Read root `AGENTS.md`, [governance](../../../governance/AGENTS.md), the
[handoff](../../../Mastermind_Handoff.md) and the approved
[F6 section](../contracts/frontend-follow-up-2026-10-20.md#f6--minimal-operational-reads-a8a15).
F5 runs concurrently and owns the Identity, Cms, Support, Notification, policy-registration
and `docs/postman/admins/Sanad.Admin.postman_collection.json` files listed in its own brief.
Preserve every unrelated dirty edit and never stage it. No subagents, no new test
framework, no OTP work, no migration (this phase is read-only and adds no schema).

## Anchors already verified (do not re-derive)

- Families: `Families/Domain/.../Families/Family.cs:8` (`Name`, `OwnerUserId`,
  `DeletedOnUtc`), `FamilyMember.cs:6` (`Role`, `RelationshipType`, `AddedByUserId`,
  `JoinedOnUtc`), `FamilyRole.cs:3` (`Owner/Editor/Viewer`); dependents are `Elderly`
  (`Elderlies/Elderly.cs:12`, `FamilyId` `:59`, `IdentityUserId` `:58`,
  `RelationshipType` `:60`). No admin family read exists. Self-service reads live in
  `.../Families/FamilyProfiles.cs`, `.../Families/FamilyMembers.cs`
  (`GetFamilyMemberQuery:68`), `.../Elderlies/Dependents.cs:20`. The closest
  safe-projection precedent is `.../CheckIns/AdminElderlyCheckInQueries.cs` (deliberately
  excludes phone/address/clinical fields, enforced by
  `tests/Sanad.UnitTests/Families/AdminElderlyFilterPostgresTests.cs`-style tests) — copy
  that discipline, not its fields.
- Care Home visits: `CareHomes/Domain/.../Bookings/CareHomeVisits.cs:126`
  (`CareHomeVisit`), status enum `:9-19` (`Pending, Approved, Rejected, FamilyCancelled,
  FacilityCancelled, Expired, ReschedulePending, Rescheduled`), recorded facts
  `DecidedByUserId/DecidedOnUtc/DecisionReason` `:166-168`,
  `CancelledByUserId/CancelledOnUtc/CancellationReason` `:169-171`,
  `RescheduleOfVisitId` `:164`. Existing queries `.../Visits/CareHomeVisitHandlers.cs:23-36`,
  DTOs `.../Visits/CareHomeVisitModels.cs:14`, routes
  `src/API/Sanad.API/Controllers/CareHomeVisitsController.cs:27` (family + owner only).
  **No admin visit read exists.** Filtered paged read pattern:
  `.../Bookings/AdminCareHomeBookings.cs:72-116`.
- Caregiver booking detail: `Families/Application/.../Bookings/CaregiverBookingQueries.cs:103`
  (`GetCaregiverBookingDetailQuery`), DTO `Bookings/BookingQueries.cs:27-55`
  (`BookingDetailResponse`) — it exposes `CaregiverId`/`CaregiverType` but **no caregiver
  name/photo/rating**, while the family-facing list already resolves
  `CaregiverArabicName`/`CaregiverEnglishName`/`CaregiverAvatarUrl`
  (`Bookings/BookingCommands.cs:25-39`) — reuse that same resolution seam. Route:
  `src/API/Sanad.API/Controllers/CaregiverBookingsController.cs:29-31,78-104`
  (`CaregiverAccess`, unchanged).
- Paging convention: default 25 / max 100, `Items/Page/PageSize/TotalCount`
  (`.../Community/Application/.../Posts/AdminCommunityRestrictionQueries.cs:61-93`).

## Pinned behavior (Mastermind decision — implement exactly; do not extend)

1. **Admin family read** (`GET /api/v1/admin/families`, `GET /api/v1/admin/families/{familyId}`):
   read-only list/detail with family id, `Name`, owner user id, member count, created and
   (when deleted) deletion facts; each member's user id, `FamilyRole` and status plus
   relationship/joined facts; and each dependent's id, `IdentityUserId` and
   `RelationshipType`. **No** medical profile, notes, address, phone, health or assessment
   data; no subscription/search enrichment; no impersonation and no write route. Support
   obvious name and exact id lookup with bounded paging (`page`/`pageSize` clamped
   1..100, default 25) and a stable newest-first order with `totalCount`. Unknown id →
   404; unknown/blank search that matches nothing → an empty page, never an error.
2. **Admin Care Home visit read** (`GET /api/v1/admin/care-homes/visits`,
   `GET /api/v1/admin/care-homes/visits/{visitId}`): read-only list/detail with facility,
   family/dependent, kind, status, scheduled window, and the recorded decision/cancellation
   facts (actor id, time, reason) and reschedule link. Filters: `facilityId`, `status`,
   `from`/`to` on the scheduled window, bounded paging with `totalCount`. No Admin
   scheduling, approval, rejection, cancellation or any override route; unknown id → 404.
3. **Caregiver booking detail**: add only the caregiver display fields that already resolve
   elsewhere (bilingual name, avatar url, and the recorded rating average/count if that
   value is already available to the same seam) plus the lifecycle facts already persisted
   on the booking (paid/confirmed/started/completed/cancelled/refunded timestamps and the
   existing cancellation facts). Do not invent a status ledger, event history or derived
   timeline; do not change eligibility, amounts or existing fields, and keep the existing
   caregiver-only access boundary.
4. **Roles.** Reuse `AuthorizationPolicies.CareHomesOperationalAdmin` for the new admin
   visit controller (already SuperAdmin + SupportAdmin, same as the sibling admin bookings
   controller) and `AuthorizationPolicies.OperationalReadAdmin` for the new admin family
   controller — both constants already exist in the base (the coordinating Mastermind
   prepared `OperationalReadAdmin`), so **do not edit** `AuthorizationPolicies.cs` or
   `DependencyInjection.cs`. No route may widen existing Finance access to new family
   data, and no new read may be reachable by a family, caregiver or owner token.
5. **Docs and Postman.** New admin family reads get a **new** collection
   `docs/postman/admins/Sanad.Admin.Families.postman_collection.json`; add the visit routes
   to your own `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json` and the
   caregiver detail fields to `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`.
   Never edit F5's `docs/postman/admins/Sanad.Admin.postman_collection.json`. Publish
   exactly the implemented fields, including the safe-field exclusions.

## Exact writable manifest

- New: `src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/AdminFamilyReadQueries.cs`
- New: `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/AdminCareHomeVisitQueries.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/CaregiverBookingQueries.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingQueries.cs`
- New: `src/API/Sanad.API/Controllers/AdminFamiliesController.cs`
- New: `src/API/Sanad.API/Controllers/AdminCareHomeVisitsController.cs`
- New: `tests/Sanad.UnitTests/Families/AdminFamilyReadQueryTests.cs`
- New: `tests/Sanad.UnitTests/CareHomes/AdminCareHomeVisitQueryTests.cs`
- New: `tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs`
- New: `tests/Sanad.UnitTests/API/AdminOperationalReadAuthorizationTests.cs`
- `docs/admin/user-management.md` (or a new `docs/admin/families.md` if that guide is the wrong home — record the choice)
- `docs/care-homes/visits.md`, `docs/admin/care-homes.md`, `docs/admin/bookings.md`
- `docs/app/caregivers/bookings.md`
- New: `docs/postman/admins/Sanad.Admin.Families.postman_collection.json`
- `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json`
- `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`
- New: `tools/Invoke-F6LocalHttpVerification.ps1`
- This brief.

Everything else — including `AuthorizationPolicies.cs`, `DependencyInjection.cs`, all
Identity/Cms/Support files, F5's Postman collection, the coverage matrix and the handoff —
is read-only. Request a precise manifest amendment if the scope truly needs one.

## Parallel verification gate

F5 owns the first runtime slot (it applies an Identity migration locally). Implement and
author your focused tests and docs now, and run only static checks (diff/JSON/PowerShell
parse, link checks). Then **stop at implementation handback before any `dotnet` build or
test, before starting the API and before writing fixtures**, and report the exact pending
commands and the minimal fixture plan. No polling or wait loops; Mastermind resumes you
when F5 releases the slot. Acceptance then requires one strict successful build/affected-test run with zero
warnings plus real localhost HTTP proof (role denial, unknown id 404, bounded paging,
empty reads, owner/family/caregiver isolation) using minimal reusable synthetic records
and own-session cleanup. `tools/Invoke-F6LocalHttpVerification.ps1` must dot-source
`tools/Invoke-LocalFixtureVerification.ps1` `-LibraryOnly` and reuse its helpers as
`tools/Invoke-F4LocalHttpVerification.ps1:94` does, be loopback-only on a free port, and
fail closed on the all-context/non-live preflight; no reset/delete/bulk seed and no
production or provider call.

## Current result

**Handback (round 1) plus the Mastermind gate.** The Worker's delivery arrived at
`ready_for_review` with every static check passing and the build, focused tests and HTTP
gate `NOT RUN` by dispatch design (F5 held the runtime slot). The Mastermind then ran the
gate after F5 released the slot, corrected the two defects it exposed and the fragile
fixture choice, and accepted the phase — see the acceptance note at the top and the
corrections below.

**Session / model / base.** Worker `ses_ea54f7a0bffeu65ZvoEt3nXvbr`,
`river/deepseek-ai/DeepSeek-V4.1-Flash#high`. Pushed `main` HEAD at dispatch
`607d33fcb651bed25239050989cb94a8cb36de41`; working tree `HEAD` observed at
`0ab1f3fda862c8fa239af5a5f8fd81332b020a90` (F3 release executor, concurrent). The Worker
wrote no git state: nothing staged, committed, pushed, deployed or reset, and every
pre-existing dirty/untracked entry (F5's Identity/Cms/Support files and its
`Sanad.Admin.postman_collection.json`, `.codex/*`, `README.md`, the handoff, the e05/e06
contracts, F3's `buildout*/testout*/migout.txt`) is preserved byte-for-byte.

**Changed files (exact manifest).** New
`src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/AdminFamilyReadQueries.cs`
(224), new
`src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/AdminCareHomeVisitQueries.cs`
(235), `.../Families.Application/Bookings/CaregiverBookingQueries.cs` (+28/−2),
`.../Bookings/BookingQueries.cs` (+10/−1), new `src/API/Sanad.API/Controllers/AdminFamiliesController.cs`
(45), new `src/API/Sanad.API/Controllers/AdminCareHomeVisitsController.cs` (50), new
`tests/Sanad.UnitTests/Families/AdminFamilyReadQueryTests.cs` (363), new
`tests/Sanad.UnitTests/CareHomes/AdminCareHomeVisitQueryTests.cs` (294), new
`tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs` (245), new
`tests/Sanad.UnitTests/API/AdminOperationalReadAuthorizationTests.cs` (637), new
`tools/Invoke-F6LocalHttpVerification.ps1` (1542), new `docs/admin/families.md` (122),
`docs/care-homes/visits.md` (+49/−1), `docs/admin/care-homes.md` (+4),
`docs/admin/bookings.md` (+2), `docs/app/caregivers/bookings.md` (+15/−1), new
`docs/postman/admins/Sanad.Admin.Families.postman_collection.json` (142),
`docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json` (+52),
`docs/postman/app/Sanad.App.Caregiver.postman_collection.json` (+2/−1), this brief. No
other file was written; `AuthorizationPolicies.cs`, `DependencyInjection.cs`, all
Identity/Cms/Support files and F5's collections are untouched.

**Mastermind corrections (what the gate found).** (1) `New-F6CodeAssert` called the L1
script-scope helper `Get-SafeErrorSummary` from inside a `.GetNewClosure()` assertion; a
PowerShell closure resolves only the global scope, so the four code-asserting checks would
have thrown instead of reporting (the same class of defect F5's adapter hit). It now inlines
`status=… code=…`. (2) `AdminOperationalReadAuthorizationTests` compared the controllers'
`RouteAttribute.Template` against a leading-slash constant; the repository declares templates
without a leading slash, so `FamiliesRouteTemplate`/`VisitsRouteTemplate` were added for
those two assertions while the leading-slash constants stay for the OpenAPI path lookups.
That pair was exactly the two focused-test failures. (3) The adapter now provisions its own
dedicated `ContentAdmin (6)` and app-family `FamilyViewer (1)` session identities instead of
signing in as the shared L1 Minimal `content.admin@test.sanad.local` /
`family.viewer@test.sanad.local`, which had already refused the first preflight login with
the confirmed `Identity.Login.SessionLimitReached` at the five-session policy cap. No
production code changed in these corrections.

**Delivered behavior.** (1) `GET /api/v1/admin/families` and
`GET /api/v1/admin/families/{familyId}` under the existing
`AuthorizationPolicies.OperationalReadAdmin`; (2) `GET /api/v1/admin/care-homes/visits`
and `GET /api/v1/admin/care-homes/visits/{visitId}` under the existing
`CareHomesOperationalAdmin` (reused, not edited — verified `DependencyInjection.cs:396-420`
requires `Normal` + `SuperAdmin`/`SupportAdmin`, so ContentAdmin/FinanceAdmin/owner/Family
are denied and no Finance access to family data is widened); (3) additive
`BookingDetailResponse` members `cancellationActorSide`, `cancellationAction`,
`cancellationReasonCategory`, `refundEntitlement`, `refundDecisionReason` (all `null` by
default and filled only by the caregiver detail handler) plus a fact-aware `refundState`.
No migration, entity, column, write route, impersonation, edit action, subscription or
search enrichment, admin visit override route or invented ledger/timeline.

**Decisions recorded.** (a) *Doc home*: a new `docs/admin/families.md` was created rather
than extending `docs/admin/user-management.md`, because that guide is Identity
SuperAdmin account management and a family register shares none of its policy, routes or
payloads. (b) *Paging*: both new lists **clamp** (`page = max(1, page)`,
`pageSize = clamp(pageSize <= 0 ? 25 : pageSize, 1, 100)`) and never return 400 for
paging; `totalCount` is the filtered count. (c) *Errors*: no new error code — family
detail reuses `Families.Family.NotFound` (404), visit detail reuses
`CareHomes.Visits.NotFound` (404), and the three rejected visit filters reuse the existing
`CareHomes.Admin.InvalidQuery` (400) via `AdminCareHomeVisitErrors.InvalidQuery`. (d)
`cancellationReasonCategory` is published as the recorded category **name string**
(`.ToString()`), matching the already-published sibling
`AdminCancellationItemResponse.ReasonCategory` (`AdminCancellationQueries.cs:116`); the
other four added facts stay numeric enums, consistent with
`docs/admin/api-contract-conventions.md`. (e) Visit `from`/`to` are `DateOnly?` inclusive
UTC days resolved as a half-open overlap (`EndsAtUtc > from 00:00Z`,
`StartsAtUtc < (to+1) 00:00Z`, `DateOnly.MaxValue` guarded); `Guid.Empty`, undefined
status and `to < from` are the only 400s. (f) Deleted families are retained and report
`deletedOnUtc`; their deletion reason/message and member identity display data
(`ArabicFullName`/`EnglishFullName`/`Email`/`PhoneNumber`/`AvatarUrl`) are never projected
even though the member profile seam resolves them.

**Authored tests — 31, all `NOT RUN`.** `AdminFamilyReadQueryTests` (7):
`List_clamps_paging_and_reports_the_filtered_total`,
`List_pages_stably_without_omitting_or_repeating_a_family`,
`List_searches_by_exact_id_and_by_name_and_never_errors_on_an_empty_match`,
`List_retains_a_deleted_family_without_publishing_its_deletion_reason_or_message`,
`Detail_publishes_members_and_dependents_without_any_personal_or_clinical_field`,
`Detail_includes_dependents_of_a_deleted_family_and_reports_the_deletion_instant`,
`Detail_returns_not_found_for_an_unknown_or_empty_family_id`.
`AdminCareHomeVisitQueryTests` (6): `List_orders_newest_first_and_clamps_paging`,
`List_filters_by_facility_status_and_the_inclusive_utc_day_window`,
`List_rejects_an_empty_facility_an_undefined_status_and_an_inverted_window`,
`Detail_publishes_the_recorded_decision_cancellation_and_reschedule_facts`,
`Detail_never_returns_visitor_contact_data_or_the_requesting_family_user`,
`Detail_returns_not_found_for_an_unknown_or_empty_visit_id`.
`CaregiverBookingDetailTests` (4):
`Detail_publishes_the_persisted_cancellation_facts_and_a_fact_aware_refund_state`,
`Detail_reports_no_cancellation_facts_when_none_was_recorded`,
`Detail_keeps_the_caregiver_only_boundary_and_never_serves_another_caregivers_booking`,
`Detail_never_exposes_the_caregiver_incident_flag_a_ledger_or_a_derived_timeline`.
`AdminOperationalReadAuthorizationTests` (14):
`FamilyReadController_UsesOperationalReadAdminPolicy_AndExposesGetOnly`,
`FamilyListRoute_BindsClampedPagingAndSearchAndPublishesOnlyOk`,
`FamilyDetailRoute_TakesAFamilyIdAndPublishesNotFound`,
`VisitReadController_UsesCareHomesOperationalAdminPolicy_AndExposesGetOnly`,
`VisitListRoute_BindsFacilityStatusWindowAndPagingOnly`,
`VisitDetailRoute_TakesAVisitIdAndPublishesNotFound`,
`OperationalReadAdmin_AllowsSuperAdminAndSupportAdminOnly`,
`CareHomesOperationalAdmin_KeepsTheSiblingAdminBookingsPolicySet`,
`FamilyController_ForwardsListAndDetailQueriesAndReturnsThePayload`,
`VisitController_ForwardsListAndDetailQueriesAndMapsNotFound`,
`OpenApi_OperationalReadPathsExistAndAreReadOnly`,
`OpenApi_AdminFamilyListPublishesTheExactPinnedFields`,
`OpenApi_AdminVisitListPublishesTheExactPinnedFields`,
`OpenApi_CaregiverBookingDetail_PublishesTheF6FactFieldsInsideTheUnchangedBoundary`.

**Static checks run (PASS).** `git diff --check` exit 0 with no output; the only diff
deletions are three single-line replacements that were expanded in place
(`docs/app/caregivers/bookings.md` "Detail matches family booking detail…" →
`### Detail` section, `docs/care-homes/visits.md` verification-limits sentence, and the
Caregiver Postman detail line), and `docs/admin/bookings.md` line endings were normalized
to the index LF so the file is not mixed.
All three Postman collections parse as JSON v2.1.0 with the expected item counts (new
Families 6, CareHomes 8 folders, Caregiver 9 folders) and every changed Markdown/JSON file
is valid UTF-8. Markdown link+anchor check over the five touched guides: 0 problems —
including the new `../postman/admins/Sanad.Admin.Families.postman_collection.json`,
`../care-homes/visits.md#admin-read-operational-register` and
`../app/caregivers/bookings.md#detail` targets. F6 adapter: `Parser::ParseFile` reports
**0 AST errors** (`#requires -Version 5.1`, 17 `Get-F6*` functions, no name colliding with
L1's 27 helpers), and every L1 call site was re-checked against the real signatures
(`Get-FixtureToken -Client/-Url/-Email/-Password/-DeviceName`, `Invoke-PlannedChecks
-Client/-Url/-Plan/-LogPath`, `Invoke-TargetVerification -RepoRoot/-AssemblyPath
/-Environment/-ApiDirectory`, `Get-LocalFixtureEnvironment -Password/-EnableSeed`,
`Start-FixtureApiProcess`, `Wait-ForEndpoint -Url/-TimeoutSeconds/-Process`,
`Get-CapturedLogLines -Path`, `Get-TrimmedRedactedLine`, `ConvertFrom-SafeJson`,
`Assert-LoopbackBaseUrl`), and the default
`bin/Release/net10.0/Sanad.API.dll` path matches the API's `net10.0` target. Referenced
helpers (`PortalApiMetadataHost`, `BookingCancellationPolicyInput.ForCaregiverCancellation`,
`BookingRefundStates.ResolveWithFact`, `IFamiliesDbContext.BookingCancellationFacts`) were
confirmed to exist.

**Pending commands — NOT RUN (must run after F5 releases the slot).**

```powershell
dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true
dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release `
  --filter "FullyQualifiedName~AdminFamilyReadQueryTests|FullyQualifiedName~AdminCareHomeVisitQueryTests|FullyQualifiedName~CaregiverBookingDetailTests|FullyQualifiedName~AdminOperationalReadAuthorizationTests"
pwsh -NoProfile -File tools/Invoke-F6LocalHttpVerification.ps1   # default http://localhost:5243
```

No `dotnet` build, test, API start, database row or adapter run has happened in this
session, and no gate above is claimed.

**Minimal fixture plan (as authored in the adapter).** `-SkipFixtureWrite` exists for a
report-only dry run. Default `BaseUrl http://localhost:5243` (avoids 5235/5237/5241),
loopback-only, and a fail-closed guard requires all nine module contexts to be
`Development` + `PersistentLocal` with no pending migration and no live Paymob/SMS/SMTP/
Push input before anything is written. INSERT-only, guarded, idempotent psql: four synthetic
identities — the `SupportAdmin (7)` actor `support.admin.f6@test.sanad.local`, the caregiver,
and the dedicated `ContentAdmin (6)` `content.admin.f6@test.sanad.local` and app-family
`FamilyViewer (1)` `family.viewer.f6@test.sanad.local` that the Mastermind added so neither
role-denial check ever signs in as a shared L1 identity — all `Active (2)` with the password
hash copied from the shared L1 Minimal identity, never invented and never printed; one active
family with two members, one soft-deleted family,
one `Approved (5)` facility and two visits (kind `Prospective (1)`/`Resident (2)`) with
fixed UTC windows: an `Approved (2)` visit carrying recorded decision facts on 2027-01-15
and a `FamilyCancelled (4)` visit on 2027-01-16 carrying cancellation facts and a
reschedule link. The L1 Minimal `family.viewer@test.sanad.local` row stays a read-only visit
data reference and `content.admin@test.sanad.local` only supplies the copied password hash;
neither is ever signed in to. The plan is 27 checks (23 read-only + one own-session logout per actor of four); every session
the run creates is logged out by the same run, no reset/delete/bulk seed is issued, and no
production or provider call is possible.

**Limitations (explicit).**
1. **Pinned #3 caregiver display fields cannot be implemented in this manifest.** The
   bilingual caregiver name, avatar url and rating average/count the family list resolves
   (`CaregiverArabicName`/`CaregiverEnglishName`/`CaregiverAvatarUrl`,
   `Bookings/BookingCommands.cs:25-39`) are declared but **never assigned anywhere in the
   solution**, so there is no existing resolution seam to reuse: Families' only outbound
   Caregivers ports are `ICaregiverBookingPricing`, `ICaregiverCatalogLookup`
   (`IsSelectedPairAsync` only) and `IMedicalCaregiverGateway`, the rating aggregate lives
   in Caregivers, `Families.Application` has no Caregivers reference, and the DI file that
   would wire a new gateway is read-only for this phase. The in-manifest part (the
   persisted lifecycle/cancellation facts) is delivered; the display half needs a
   **manifest amendment** — the cleanest options are to add
   `src/API/Sanad.API/Controllers/CaregiverBookingsController.cs` (it already injects
   `ICaregiversDbContext`) plus a matching Postman/OpenAPI assertion, or to add a new API
   gateway with a `DependencyInjection.cs` edit — together with an Owner decision on which
   route/screen must show the caregiver identity. I did not invent a value or stub a seam.

   **Mastermind resolution.** The claim was verified against source: the three members are
   declared at `Bookings/BookingCommands.cs:28-30` and assigned nowhere in `src`, and the
   rating aggregate lives in Caregivers behind no Families reference. The pin only ever asked
   for fields that *already resolve elsewhere*, so with that precondition factually absent the
   conditional half is **not** delivered and no manifest is expanded for a value that would
   have to be invented. Deferred to the Owner as an open frontend question: the desktop report
   (`BACKEND_ENDPOINTS_REQUEST.md:241`) actually asks for the **Admin** `GET /admin/bookings/{id}`
   to gain the caregiver id/name (plus status history, which this contract explicitly
   excludes), whereas this brief pinned the caregiver-facing detail whose caller is the
   caregiver themselves. The Owner decides the surface; a seam plus a manifest amendment
   would then be needed.
2. The adapter proves the caregiver-detail delta through the published OpenAPI schema only;
   no caregiver booking read is sent over HTTP (no caregiver-owned booking fixture).
3. The adapter asserts an empty `dependents` array over HTTP; the populated dependents
   projection and the deleted-family detail `200` are proven by unit tests only.
4. **Resolved by the Mastermind (approved manifest amendment).** `docs/admin/families.md` is
   now listed in `docs/admin/admin-overview.md` (`| Family register (families, members,
   dependents) | docs/admin/families.md |`), and that index's Postman row now also names
   `docs/postman/admins/Sanad.Admin.Families.postman_collection.json`. That is the whole of
   the amendment applied for this phase.
5. `BookingDetailResponse` is also published by the family and Admin booking-detail
   OpenAPI schemas, so those schemas gain the five nullable members (always `null` for
   those reads). `docs/app/families/bookings.md` and the family Postman collection are
   outside this manifest and unchanged; document them if the frontend treats the published
   schema as a contract.
6. Existing divergent convention left as found: `docs/admin/bookings.md` documents the
   Admin surface as `CaregiversAdmin` (SuperAdmin + ContentAdmin) while the sibling Care
   Homes reads use `CareHomesOperationalAdmin` (SuperAdmin + SupportAdmin); F6 reuses the
   latter for visits exactly as the brief pins and changes neither.
7. Evidence reused, not re-derived: the F6 brief's own anchor set (Families/CareHomes/
   caregiver enquiry lines, paging and error-code conventions) was re-verified against
   source during this round, so the anchors above remain current for `0ab1f3fd`.

The Worker claimed no reviewer/verifier pass, no acceptance and no release; the Mastermind
accepted completion after the gate above and handed the release to the delegated release
executor.


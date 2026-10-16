# FUR-01 delivery report — existing API contract accuracy

Date: 2026-10-16 (Africa/Cairo). Revised with R1–R5 review corrections.
Phase status: **In progress — Mastermind local acceptance complete; deployment pending.**
Contract: [fur-01-contract-accuracy](../contracts/fur-01-contract-accuracy.md) (owner-approved 2026-10-16).
Brief: [fur-01 worker brief](../worker-prompts/fur-01-contract-accuracy.md).
Review: [Mastermind acceptance review](fur-01-mastermind-review.md) (changes requested) →
corrections: [bounded brief](../worker-prompts/fur-01-review-corrections.md).

## 1. Intake SHA and changed-file manifest

- Intake HEAD at worker start: `4b65369d760c8af6b2242ebd2022a6cb10057580` (matches the contract's pinned preparation HEAD; no branch/worktree created, standing `main` checkout).
- Pre-existing local delta at intake (preserved untouched): modified `docs/README.md`, `docs/internal/Mastermind_Handoff.md`, `docs/internal/Project_Main_Goal.md`, `docs/internal/work/portal-api-reconciliation/frontend-handoff.md`, `.../plan.md`; untracked `Frontend_Report.md`, `docs/frontend-handoffs/`, `contracts/`, `frontend-report-audit-2026-10-16.md`, `worker-prompts/fur-01-contract-accuracy.md`. No reset, delete, wholesale stage, or overwrite of user files occurred.
- Final HEAD is unchanged (`4b65369d...`); no implementation commit exists — the worker does not commit. The FUR-01 delta is the working-tree diff described below.

Worker-owned changes (exact manifest):

Production (attribute/import edits only, seven approved controllers under `src/API/Sanad.API/Controllers/`):

- `AdminElderlyHelpRequestsController.cs` — 2 attributes (Detail, History).
- `CareHomeInventoryController.cs` — 19 attributes (Mine, Create/Update/ArchiveType, Create/Rename/ArchiveRoom, Create/Rename/ArchiveBed, Maintenance, Amend/CancelMaintenance, UploadTypeMedia, UploadRoomMedia, RemoveTypeMedia, RemoveRoomMedia, MyAvailability, Availability). No new imports needed (existing `Inventory`/`Facilities` usings cover all DTOs).
- `CareHomeBookingRequestsController.cs` — 1 attribute (List).
- `CareHomeVisitsController.cs` — 3 attributes (OwnerList, OwnerSettings, UpdateOwnerSettings).
- `CareHomeFinanceController.cs` — 2 attributes (Dashboard, Revenue).
- `NotificationsController.cs` — 1 attribute (UnreadCount).
- `AdminFinanceController.cs` — 1 attribute (Create).

Tests (new, under `tests/Sanad.UnitTests/API/`):

- `PortalRemainingResponseContractTests.cs` — 29-operation OpenAPI matrix + nested resolution + representative HTTP evidence. R-correction: visit `kind`/`status` properties now follow their actual `$ref`s to `CareHomeVisitKind` (1–2, Prospective/Resident) and `CareHomeVisitStatus` (1–8 with all names), instead of only asserting component existence.
- `PortalAuthExampleContractTests.cs` — literal auth request/response + enum/dictionary/ID conventions. R-correction: new `PortalLiteralExampleConformanceTests` bridge reads the actual three auth-guide JSON blocks and all seven edited Postman response bodies, binds request literals to real DTOs with MVC options, and shape-matches response literals against serialized DTOs (values/samples may differ; wrapper/nullability/fields must match). Superseded plain-GUID literals are proven to fail the guard. Activation prose is checked as documentation text only (handler rule by source inspection).

Guides (contradicted sections only):

- `docs/auth/registration-and-verification.md` — actual register/verify/resend field names, literal response examples (wrapper IDs, `attemptesRemaining`, replacement ID + expiry), restricted-login limits. R-correction: activation prose now states the both-channels rule (either supported channel can be the final verification that activates; verified against `VerifyOtpCommandHandler` source, no runtime change).
- `docs/auth/email-password-login.md` — R-correction: pending/active response examples now show wrapper IDs (`userId` / `deviceSessionId` as `{ "value": guid }`) with inner-GUID extraction for header/body reuse.
- `docs/auth/refresh-and-sessions.md` — `X-Device-Session-Id` inner-GUID note. R-correction: response example corrected to the wrapper shape plus a JSON request example (was curl-only, so no JSON literal existed to test).
- `docs/admin/care-assessments.md` — JPEG/PNG/WebP (SVG rejected), whole-request 2 MiB overhead note.
- `docs/admin/caregivers-review.md` — certificate download policy `CaregiverReviewAdmin`.
- `docs/admin/elderly-help-requests.md` — detail/history schemas + enum-name dictionary keys.
- `docs/admin/finance-platform-charges.md` — current-rule empty state is `204`, not JSON null.
- `docs/admin/care-homes.md` — 1000-character review/document reason limits.
- `docs/app/caregivers/payouts.md` — payout reason/evidence 2000, reference 200, scoped 500 note.
- `docs/app/notifications.md` — bare-integer unread-count example, no envelope.

Postman (existing requests only; no tokens/environments/scripts/routes):

- `docs/postman/Sanad.Auth.postman_collection.json` — 7 response examples corrected to wrapper IDs (register 201, verify 200 ×2, resend 200, login 200 ×2, refresh 200).
- `docs/postman/admins/Sanad.Admin.Finance.postman_collection.json` — create/current/history descriptions.
- `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json` — inventory/availability descriptions.
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json` — 15 inventory descriptions.

Delivery documentation:

- This report (`reports/fur-01-contract-accuracy.md`).
- `docs/frontend-handoffs/admin/admin-portal/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md`.
- `docs/frontend-handoffs/admin/care-home-owner/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md`.
- `docs/frontend-handoffs/app/shared/phase-01-contract-accuracy/2026-10-16-contract-notice.md`.
- `docs/frontend-handoffs/README.md` index rows for the three handoffs.

No other production files, transformers, DI/auth policies, error mappers, configs, migrations, handlers, domain types, or snapshots were edited.

## 2. Completed 29-operation status/schema matrix

All routes prefixed `/api/v1`. Statuses/shapes proven against the real generated OpenAPI document plus representative HTTP. No error annotations added (none proven beyond existing mappings); no runtime behavior changed.

| # | Operation | Success | Schema evidence |
| --- | --- | --- | --- |
| 1 | `GET /admin/elderly/help-requests/{requestId}` | 200 `ElderlyHelpRequestResponse` | `$ref` resolves; HTTP readback returns `status: 1` |
| 2 | `GET /admin/elderly/help-requests/{requestId}/history` | 200 `IReadOnlyList<ElderlyHelpRequestHistoryResponse>` | array of `$ref`; HTTP single entry `action: 1` |
| 3 | `GET /care-homes/inventory/mine` | 200 `CareHomeInventoryDto` | `$ref`; real-fixture HTTP readback (`facilityId`, nested `allocationMode: 1`) |
| 4 | `POST /care-homes/inventory/mine/room-types` | 200 `CareHomeInventoryDto` | `$ref`; HTTP 200 |
| 5 | `PUT /care-homes/inventory/mine/room-types/{id}` | 200 `CareHomeInventoryDto` | `$ref` |
| 6 | `POST /care-homes/inventory/mine/room-types/{id}/media` | 201 `CareHomeRoomTypeMediaDto` | `$ref`; HTTP 201; no spurious 200 |
| 7 | `DELETE /care-homes/inventory/mine/room-types/{id}/media/{mediaId}` | 204, no body/schema | no `content`; HTTP 204 empty |
| 8 | `POST /care-homes/inventory/mine/room-types/{id}/archive` | 200 `CareHomeInventoryDto` | `$ref` |
| 9 | `POST /care-homes/inventory/mine/rooms` | 200 `CareHomeInventoryDto` | `$ref` |
| 10 | `PUT /care-homes/inventory/mine/rooms/{id}` | 200 `CareHomeInventoryDto` | `$ref` |
| 11 | `POST /care-homes/inventory/mine/rooms/{id}/media` | 201 `CareHomeRoomMediaDto` | `$ref`; HTTP 201; no spurious 200 |
| 12 | `DELETE /care-homes/inventory/mine/rooms/{id}/media/{mediaId}` | 204, no body/schema | no `content`; HTTP 204 empty |
| 13 | `POST /care-homes/inventory/mine/rooms/{id}/archive` | 200 `CareHomeInventoryDto` | `$ref` |
| 14 | `POST /care-homes/inventory/mine/beds` | 200 `CareHomeInventoryDto` | `$ref` |
| 15 | `PUT /care-homes/inventory/mine/beds/{id}` | 200 `CareHomeInventoryDto` | `$ref` |
| 16 | `POST /care-homes/inventory/mine/beds/{id}/archive` | 200 `CareHomeInventoryDto` | `$ref` |
| 17 | `POST /care-homes/inventory/mine/maintenance` | 200 `CareHomeInventoryDto` | `$ref` |
| 18 | `PUT /care-homes/inventory/mine/maintenance/{id}` | 200 `CareHomeInventoryDto` | `$ref` |
| 19 | `POST /care-homes/inventory/mine/maintenance/{id}/cancel` | 200 `CareHomeInventoryDto` | `$ref` |
| 20 | `POST /care-homes/inventory/mine/availability` | 200 `IReadOnlyList<CareHomeAvailability>` | array of `$ref`; HTTP 200 array |
| 21 | `POST /care-homes/inventory/admin/{facilityId}/availability` | 200 `IReadOnlyList<CareHomeAvailability>` | array of `$ref` |
| 22 | `GET /care-homes/booking-requests/mine` | 200 `IReadOnlyList<CareHomeBookingListItem>` | array of `$ref`; HTTP `status: 3` |
| 23 | `GET /care-homes/visits/mine` | 200 `IReadOnlyList<CareHomeVisitResponse>` | array of `$ref`; HTTP `kind: 1`; nested `CareHomeVisitKind`/`CareHomeVisitStatus` components resolve |
| 24 | `GET /care-homes/facilities/mine/visit-settings` | 200 `CareHomeVisitSettingsResponse` | `$ref`; HTTP `visitorCapacity: 50` |
| 25 | `PUT /care-homes/facilities/mine/visit-settings` | 200 `CareHomeVisitSettingsResponse` | `$ref`; HTTP 200 |
| 26 | `GET /care-homes/dashboard/mine` | 200 `CareHomeDashboard` | `$ref`; nested `periodRevenue: CareHomeRevenueTotals`; HTTP 200 |
| 27 | `GET /care-homes/revenue/mine` | 200 `CareHomeRevenueReport` | `$ref`; nested `totals`/`transactions`; HTTP 200 |
| 28 | `GET /notifications/unread-count` | 200 bare `int` | integer/int32 schema; HTTP bare `0` and `3` |
| 29 | `POST /admin/finance/platform-charge-rules` | 201 `Guid` | string/uuid; HTTP 201 GUID; no spurious 200 |

Nested/ID/enum correctness: inventory `roomTypes/rooms/beds/maintenance` refs resolve; visit kind/status enum components exist; dashboard/revenue totals/transaction refs resolve; history `action`/`status` present; booking `status`/`paymentStatus` present. Wrapper-ID and numeric-enum conventions re-verified (existing PORTAL-03 tests still green). Literal MVC serialization truth: help aggregate uses enum-**name** keys (`{"Pending":4,"Resolved":9}`), register/verify/resend/login/refresh use wrapper IDs with plain-GUID requests, verify keeps the literal `attemptesRemaining` spelling, restricted login carries no verification request IDs, and `BookingStatus` (10, 1–10) stays distinct from `CareHomeBookingStatus` (9, 1–9).

## 3. Test commands, results, and fixture-isolation evidence

Isolation inspected BEFORE any host launch: reused existing `PortalApiMetadataHost` (nine dummy connection keys, nine InMemory contexts, migration/seed flags off, background jobs removed, temporary storage, ephemeral DataProtection). No second host created; no host/fixture file edited.

Inherited integration configuration found in the parent shell (values never printed): `App__TestUserSeed__Enabled=true`, a real-local `ConnectionStrings__IdentityDatabase`, and `*IntegrationDatabase` keys. Every `dotnet test` below ran in the same child process with all nine module connection strings overridden to an unreachable dummy (`127.0.0.1:1`), `Database__ApplyMigrationsOnStartup=false`, `FinanceMigrations__ApplyOnStartup=false`, `App__TestUserSeed__Enabled=false`, `App__Hc034FamilyFixture__Enabled=false`, `DataProtection__KeyRingPath` empty, and integration-database keys removed. `PortalHostIsolationTests` (7/7, incl. `ConfigurationPrecedence_TestOverridesWinOverInheritedLayer`) proves overrides win structurally.

Metadata GET tests are business-fixture-free (real generated OAS over HTTP). Representative business HTTP uses deterministic `ISender` dispatch fakes (synthetic DTOs; production MVC pipeline, auth policies, and JSON options) — no database network, provider, migration, or seed. One inventory read reuses the existing `CareHomeOwnerPortalFixture` (seed facility + room type/room/bed, read back counts/ownership, then `GET /inventory/mine`). No production/disposable-DB reset, signup, OTP, provider, or VPS action occurred.

Gates (after final code edit; no failures, no skips):

1. Strict solution build: `dotnet build Sanad.slnx --nologo -v minimal -p:TreatWarningsAsErrors=true` → exit 0, **0 warnings, 0 errors**.
2. Focused tests (same child-process isolation as above): `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --nologo -v minimal --filter "FullyQualifiedName~PortalRemainingResponseContractTests|FullyQualifiedName~PortalAuthExampleContractTests|FullyQualifiedName~PortalLiteralExampleConformanceTests|FullyQualifiedName~PortalHostIsolationTests|FullyQualifiedName~PortalOpenApiContractTests|FullyQualifiedName~PortalResponseSerializationTests|FullyQualifiedName~PortalRequiredQueryRuntimeTests|FullyQualifiedName~PortalFinanceCurrentStateTests|FullyQualifiedName~CareHomeOwnerPortalTests|FullyQualifiedName~AdminResponseSchemaContractTests|FullyQualifiedName~AdminElderlyHelpRequestControllerTests|FullyQualifiedName~NotificationsControllerTests|FullyQualifiedName~AdminFinanceControllerTests"` → **179 passed, 0 failed, 0 skipped** after corrections (172 pre-correction + 7 new R3 conformance checks; strengthened visit assertions included). Earlier pre-fix run: 47/50 with 3 test-authoring failures (DayOfWeek Sunday expectation against a stale binary, dict numeric-key assumption, int type-union shape); a temporary probe test established literal truth, was deleted, and the clean run above is on rebuilt binaries. Correction-run debugging also caught and fixed a real test bug: the shape matcher compared `JsonValueKind.True` vs `False` as different shapes, i.e. it compared boolean values; booleans are now one shape kind.
3. Architecture: `dotnet test tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj --nologo -v minimal` → **1/1 passed**.
4. Postman: all 20 `*collection.json` files parse (`ConvertFrom-Json` 20/20 OK); edited example bodies re-validated as JSON (7/7); `docs/internal/tools/Verify-ApiContractMapping.ps1` → exit 0, `Controller route-method signatures: 556`, `Postman API requests: 620`, missing/orphan 0/0. The script's missing/orphan lists are never populated upstream, so 0/0 alone is not coverage proof: the worker additionally ran a read-only independent comparison (same normalization, `Method + route` HashSet both directions) → 547 extracted controller keys (extraction limited to class-`[Route]` controllers, as in the script), 620 Postman requests (556 unique), **0 missing**. Nine apparent orphans were each verified as existing controller actions with full-path Http attributes in classes without class-level `[Route]` (all nine are pre-existing profile/room/discovery file-serving routes in `CareHomeProfileMediaController`/`CareHomeRoomMediaController`, untouched by FUR-01) — extraction gaps, not coverage gaps. Mastermind's independent 0-missing/0-orphan result (fuller extraction) stands as the stronger evidence; no route-parser rewrite was performed. Bruno excluded per contract.
5. Docs/diff/links: `git diff --check` clean; route-verb diff shows attribute-only additions (no `[Http*]`/`[Route]` template change); guide literals are now read and asserted by `PortalLiteralExampleConformanceTests`; all 28 local Markdown file links under `docs/frontend-handoffs/` resolve (0 broken).

No full suite run (not required for this bounded slice; no cross-cutting change proposed). No live DB, provider, OTP, or VPS run — recorded as scope limitations, not passes.

## 4. Documentation/Postman/handoff links

- Guides: `docs/auth/registration-and-verification.md`, `docs/auth/email-password-login.md` (R1 wrapper examples), `docs/auth/refresh-and-sessions.md` (R1 wrapper + JSON request example), `docs/admin/api-contract-conventions.md` (linked, unchanged), `docs/admin/caregivers-review.md`, `docs/admin/care-assessments.md`, `docs/admin/care-homes.md`, `docs/admin/elderly-help-requests.md`, `docs/admin/finance-platform-charges.md`, `docs/care-homes/owner-onboarding.md` (verified, unchanged), `docs/care-homes/inventory-and-availability.md` / `visits.md` / `finance-reporting.md` (verified accurate, unchanged), `docs/app/caregivers/payouts.md`, `docs/app/notifications.md`.
- Postman: `docs/postman/Sanad.Auth.postman_collection.json`, `docs/postman/admins/{Sanad.Admin,Sanad.Admin.Finance,Sanad.Admin.CareHomes}.postman_collection.json`, `docs/postman/care-homes/{Sanad.CareHomes.Owner,Sanad.CareHomes.Finance}.postman_collection.json` (Finance read for dashboard/revenue descriptions verified accurate, unchanged).
- Audience handoffs (dated, phase/topic structure; Admin = Admin + Care Home Owner portals; App = Family/Caregiver/Elderly): `docs/frontend-handoffs/admin/admin-portal/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md`, `docs/frontend-handoffs/admin/care-home-owner/phase-01-contract-accuracy/2026-10-16-contract-accuracy.md`, `docs/frontend-handoffs/app/shared/phase-01-contract-accuracy/2026-10-16-contract-notice.md`; index rows added in `docs/frontend-handoffs/README.md`. No mobile frontend audited; local changes are **not** labeled deployed.

## 5. Unresolved findings and deviations

- None blocking. Three honest limitations: (a) the Postman mapping script's 0/0 output is vacuous by construction — compensated by the worker's independent HashSet comparison above (0 missing; 9 apparent orphans verified as extraction gaps) plus Mastermind's independent 0/0; script repair remains an explicitly out-of-scope follow-up; (b) the pre-existing stray directory `docs/frontend-handoffs/app/care-home-owner/` (FUR-00 layout) was left untouched as out of scope; (c) audience-report verification counts were refreshed to the corrected 179/179 run.
- Pre-change OpenAPI failures were not snapshotted before annotation (attributes were applied before the first generated-document read); the contract's "record pre-change failures where relevant" is satisfied by the intake audit's documented gap list (audit §7 #34) rather than a fresh failing run. The original 172/172 gate is preserved above as historical worker evidence; the corrected 179/179 gate supersedes it for acceptance. No scope deviation: file manifest matches the contract allowlist plus the review brief's owned files exactly (three auth guides incl. email-password-login.md, two test files, three audience reports, this report; no mapping-script edit).

## 6. R1–R5 resolution table

| ID | Review finding | Worker resolution | Evidence |
| --- | --- | --- | --- |
| R1 | Login/refresh response examples showed plain-string IDs | `email-password-login.md` pending/active responses and `refresh-and-sessions.md` response now show `{ "value": guid }` wrappers with inner-GUID extraction for `X-Device-Session-Id`/refresh bodies; requests/headers stay plain GUIDs; added a JSON refresh-request example | Guide diffs; `PortalLiteralExampleConformanceTests.LoginGuide_*` / `RefreshGuide_*` bind and shape-match the new literals |
| R2 | Activation prose wrongly attributed activation to phone verification | Guide now states the existing both-channels rule (either supported channel can complete activation), verified against `VerifyOtpCommandHandler` lines 129–152; no runtime change | Guide diff; `ActivationProse_StatesBothChannelsRule` (documentation-text check; handler rule by source inspection) |
| R3 | Example tests never read published literals; visit checks only named components | New `PortalLiteralExampleConformanceTests` (7 tests): binds all request literals to real DTOs, shape-matches all response literals incl. seven Postman bodies, rejects superseded plain-GUID literals; visit `kind`/`status` now follow `$ref`s with full value/name assertions | 179/179 focused run (7 new); strict build 0/0; corrected test bug (boolean True/False shape kinds) recorded in §3.2 |
| R4 | Eleven broken audience-report links | All `../../../internal/` → `../../../../internal/` in the three phase-01 reports; full-folder check: 28/28 local Markdown links resolve, 0 broken; phase-00 reports and taxonomy untouched | `fur01_check_links.py` output 28/0; `git diff` on the three reports |
| R5 | Missing-schema fixes labeled frontend mismatches; ambiguous `published, local` | #34 rows rewritten as backend metadata defects (frontend report was correct); assumption rows marked unproven-against-code; all `published, local` → `implemented/tested locally; not published/deployed`; deployed-vs-local shape wording added | Audience-report diffs; no remaining `published, local` / `Frontend contract mismatch` strings |

## 7. Confirmation of no forbidden operations

No new endpoints, runtime behavior, DTO serialization, permissions, business rules, configurations, migrations, or data repairs. No error-mapper, transformer, DI/policy, handler, domain, snapshot, or mapping-script edits. No real database/provider calls, resets, OTP sends, or Bruno runs. No VPS contact, publish, deploy, commit, or push; no hooks bypassed; no branch/worktree created; no files deleted. FUR-02 not started. Existing dirty documentation, user intake, and the Mastermind review file preserved.

FUR-01 is **In progress: worker corrections complete, awaiting Mastermind acceptance.** Stopping here for re-review; no phase marked Done and no release performed.

## 8. Mastermind acceptance (later evidence)

R1–R5 accepted after actual source/docs/test re-review. Mastermind strict build
zero warnings/errors; full guarded unit **3421/0/8**, architecture **1/1**;
20/20 collection JSON parse; 32/32 audience links; actual full route sets
556 unique each with zero missing/orphaned. See the final appendix in the
[Mastermind review](fur-01-mastermind-review.md) for commands, skipped cases,
discarded restricted-runner attempts and the mapping-script limitation.
The worker sections above are historical delivery evidence, not claims of a
worker commit or deployment. Phase release is now pending; FUR-02 not started.

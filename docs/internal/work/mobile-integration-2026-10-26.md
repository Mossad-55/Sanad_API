# Mobile integration review — closing items (ACTIVE BRIEF)

Phase state: `completed`. Release state: `verified` (2026-10-26, Owner-authorized
release; correction follow-up Owner-authorized same day — see "Release record").

Base: remote `main` HEAD `b22d9fa0`; deployed rehearsal revision `e664592e`
(`Staging`, seam preserved). No migrations are expected unless a task proves one
is required — that stops for Owner review first.

Single active brief: this file owns scope, task status and current result for
this slice. The Mastermind handoff carries only the next action.

## Read first

1. `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md` (compact checkpoint).
3. This brief. Inspect `git status`/`diff` first; preserve unrelated and private
   edits (`docs/internal/Mastermind_Handoff.md`, `.opencode-audit-tmp/`, `build*`,
   `testout*`, `migout.txt`, `e07-library-smoke.ps1`).

## Source report (frontend/mobile → backend, translated)

**1. Family sent-invitations list.** `GET /api/v1/family/invitations` currently
returns only invitations *received* by the user. Need an authenticated endpoint
returning invitations *sent by the family*, with fields `id`, `invitedEmail`,
`role`, `relationshipType`, `status`, `createdOnUtc`, `expiresOnUtc`. Goal:
show them after app reopen and allow cancelling a pending invitation via the
existing cancel endpoint.

**2. Confirm and correct Premium plan benefits.** The last log shows two
benefits missing from the catalog and the current subscription: `Library`
(key 2) and `PremiumContent` (key 8). Confirm the approved plan benefits. If
they include the library and premium content, correct the **plan and current
subscription settings, not just the catalog**, and ensure allowed content
returns `canAccess: true`.

**3. Enforce the no-plan-change-during-paid-period policy on the server.** The
app decision: upgrade, downgrade or re-purchase is not allowed before the
current paid period ends. Server must return a documented `409` when a plan
change is attempted during an active paid period, and must not create a new
payment. Turning off auto-renew does **not** end the paid period and does not
lift the block. Blocking purchase with an existing subscription and blocking
early renewal are already documented; confirm they are implemented and test
concurrent requests and two-device scenarios.

**4. Confirm and document CareHome-source care records.** The app displays
`CareHome`-source records for the family and the elderly, but the available
files only document creation from the family and the caregiver. Need the
care-home dashboard record-creation contract with a Request/Response example,
and confirmation that the record appears in
`GET /api/v1/family/dependents/{dependentId}/care-records` and
`GET /api/v1/elderly/care-records`. If already implemented, document it and add
the Postman collection only — do not reimplement.

## Tasks

Worker sets `in_progress`/`blocked`/`ready_for_review`; only Mastermind sets
`completed`.

| Task | Owner | Depends | Acceptance | Status |
|---|---|---|---|---|
| M1 — family sent-invitations endpoint | River GLM 5.3 `#high` | — | New authenticated outgoing-invitations endpoint returns exactly the 7 fields; existing cancel works on returned ids; focused API/handler tests; docs + Postman | completed |
| M2 — Premium plan benefits + plan-change policy | River DeepSeek V4.1 `#high` | — | (a) approved benefits confirmed with source evidence; if `Library`/`PremiumContent` are approved, correct plan + subscription source so `canAccess` is true, catalog-only fix rejected; (b) plan change during an active paid period returns documented `409`, creates no payment, auto-renew off does not lift it; duplicate-purchase and early-renewal blocks confirmed; concurrency/2-device tests | completed (live persisted-data drift found post-release and corrected under Owner authorization — see M2 correction below) |
| M3 — CareHome-source care records documentation | River GLM 5.3 `#high` | — | Confirm and document the CareHome dashboard create contract (Request/Response) and its appearance in both read routes; docs + Postman only if already implemented; report if not implemented | completed |

## Bounded scope and owned files

Edit only the files needed by the owned task; if a required path is missing or
the task needs another file, stop and report path/reason — do not self-expand.
Do not edit another task's files; on a shared file, report the overlap instead of
editing.

### M1 — family sent invitations
- `src/API/Sanad.API/Controllers/FamilyController.cs` (new outgoing route near
  the existing `invitations` routes)
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Invitations/FamilyInvitations.cs`
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Invitations/FamilyInvitation.cs`
  (read-only unless a projected field is missing)
- `tests/Sanad.UnitTests/Families/FamilyInvitationTests.cs`
- `docs/app/families/*` invitation guide + the app-family Postman collection
- Scope rule: outgoing query must be scoped to the caller's family, authenticated
  (`NormalAccess`/family policy as the sibling routes use), and return only the
  7 named fields. Reuse the existing cancel endpoint; do not add a second cancel.

### M2 — Premium benefits + plan-change policy
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionBenefit.cs`,
  `SubscriptionPlan.cs`
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionQueries.cs`,
  `SubscriptionCommands.cs`, `PlanChangeCommands.cs`
- `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryPremiumAccess.cs`
  (read-only unless the access rule is the defect)
- `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPlanChangeTests.cs`
  and the subscription/benefit test class(es) actually covering the change
- `docs/app/families/subscriptions.md` + the app-family Postman collection
- Scope rule: confirm the approved benefit set from source before changing it.
  If `Library`/`PremiumContent` are approved, the fix must correct the plan
  definition **and** the current-subscription benefit grant, not the catalog
  listing alone. Do **not** invent or drop benefits; report the exact approved
  set you find with file/line evidence. Any persisted-data correction (existing
  subscriptions on a target) is **proposed with exact idempotent SQL for Owner
  authorization** — do not write any database.

### M3 — CareHome-source care records
- `docs/app/families/care-records.md`
- `docs/care-homes/*` care-record/record-creation guide
- app-family + care-home Postman collections as needed
- Read-only source references: `src/Modules/Families/Domain/.../CareRecords/CareRecordSource.cs`,
  `src/Modules/Families/Application/.../CareRecords/*`, care-home care-record
  handlers, and the two read routes
- Scope rule: documentation/Postman only. If the CareHome create contract does
  not exist in source, report it precisely instead of implementing.

## Constraints

- No database writes, no provider/financial calls, no auth mutation, no
  migrations, no config/runtime change without explicit Owner approval.
- No commit/push/deploy by workers; the Mastermind runs the authorized release
  after a green integrated gate.
- Preserve unrelated/private edits. Never print secrets.
- Strict Release build must be 0 warnings / 0 errors; no fake passes, no
  file moves to make a gate green.
- Distinguish authored vs executed checks; record every unrun check as NOT RUN
  with a reason.

## Gates (integrated, at handback)

- `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true` → 0/0.
- Focused tests per changed path (M1 invitation, M2 subscription benefit +
  plan-change/concurrency, M3 none — docs only).
- Docs links resolve; Postman collections parse as UTF-8 JSON;
  `git diff --check` exit 0.
- Reuse valid gates; rerun only invalidated checks.

## Lifecycle

Workers hand back `ready_for_review` with changed files, commands/counts,
authored-vs-run and blockers. Mastermind reviews the actual diff, runs the
integrated gate, marks tasks/phase, then (release `authorized`) commits, pushes,
publishes, deploys to the rehearsal (preserve `Staging`, seam, env, unit,
storage) and verifies, then records the receipt here.

## Current result

Release record (2026-10-26, Owner-authorized) — see the end of this section.

- M1: completed — new `GET /api/v1/family/invitations/sent`
  (authenticated, FamilyAccess, family-scoped via `FamilyAccess.ResolveFamilyAsync`)
  returns all statuses of the caller's family's sent invitations, each with
  exactly `id`, `invitedEmail`, `role`, `relationshipType`, `status`,
  `createdOnUtc`, `expiresOnUtc` (`FamilySentInvitationResponse` in
  `FamilyInvitations.cs`; all 7 fields already projected from the existing
  `FamilyInvitation` aggregate — domain file unchanged, no migration). Cancel
  reuses the existing `DELETE /api/v1/family/invitations/{invitationId}`
  (owner-only) — no second cancel; other-family callers still get
  `Families.Invitation.AccessDenied`. Changed:
  `FamilyController.cs`, `Invitations/FamilyInvitations.cs`,
  `tests/Sanad.UnitTests/Families/FamilyInvitationTests.cs` (+3 handler tests:
  family scoping/no cross-family leak + exactly-7-fields, returned-pending-id
  cancels via the existing revoke incl. cross-family 403 pin, and a received-list
  regression), `docs/app/families/invitations.md` (+outbox section; revoke
  section id-source updated), `docs/postman/app/Sanad.App.Family.postman_collection.json`
  (+ sent-list request feeding `invitationId`). Executed: focused
  `dotnet build` of Sanad.API Release → 0/0;
  `dotnet test --filter FullyQualifiedName~FamilyInvitation` → 7/7 passed
  (4 pre-existing + 3 new); Postman JSON parses (ConvertFrom-Json);
  `git diff --check` exit 0. NOT RUN: full sln gate (parent runs it),
  live-endpoint readback (no runtime started).
- M2: completed (source verdict unchanged; live correction executed — below) —(a) approved benefit set confirmed from source, no
  source change needed. `SubscriptionPlan.cs:112-127` defines Free (keys
  1,2,3,5), Premium (1–7) and PremiumPlus (all 8) via `CreatePackage`
  unavailable-list semantics (:129-145); `Library` (key 2) is granted by every
  plan while `PremiumContent` (key 8) is granted by PremiumPlus only
  (`SubscriptionValueObjectTests.cs:51-85`, key 8 false at :72 for Premium and
  true at :84 for PremiumPlus). `LibraryPremiumAccess.cs:51-53` consults key 8
  alone and `FamilySubscription.Create`/`ApplyPlan` copy the plan's own set, so
  the shipped end state already computes `canAccess` correctly; a catalog-listing
  change alone is not the contract, so neither the plan definition nor the
  subscription grant was altered. Pinned by a new
  `SubscriptionPremiumBenefitAccessTests.cs` (snapshot equals the approved set on
  all three plans; Premium `Library=true`/`PremiumContent=false` → `canAccess`
  false; PremiumPlus `PremiumContent=true` → `canAccess` true, `VideoUrl` null).
  (b) an immediate plan change (`POST
  /api/v1/family/subscriptions/plan-change/payment-intent`) is refused with the
  documented `409 Subscriptions.PlanChange.PaidPeriodActive` while the current
  subscription price is greater than `0` and `UtcNow < currentPeriodEndsOnUtc`,
  and a duplicate / second-device retry is refused with the documented `409
  Subscriptions.PlanChange.PaymentPending` while a plan-change attempt is still
  pending. Both refusals happen before any provider call and before any attempt
  write, so **no payment row is created**; turning auto-renew off does not end the
  paid period and does not lift the block (the request still fails — `409
  Subscriptions.PlanChange.CancelRenewalRequested` once cancellation is
  requested, otherwise `PaidPeriodActive` — with no payment). The deferred
  `PUT /pending-downgrade` (applies only at the period boundary, no payment) and
  the read-only plan-change quote stay deliberately ungated. Changed:
  `PlanChangeCommands.cs` (+86/-2: `PlanChangePolicy` + paid-period and
  pending-attempt gates), `FamilySubscriptionsController.cs` (+9: explicit 409
  mapping), `SubscriptionPlanChangeTests.cs` (+194/-8: 6 Free-source fixtures +
  6 new tests including
  `Concurrent_plan_change_requests_during_a_paid_period_create_no_payment` and
  `Two_device_retry_while_a_plan_change_payment_is_pending_creates_no_second_payment`),
  `FamilySubscriptionsControllerTests.cs` (+16: 409 route theory), new
  `SubscriptionPremiumBenefitAccessTests.cs` (+183), `docs/app/families/subscriptions.md`
  (+40/-6: frozen approved set + plan-change/paid-period policy), `docs/postman/app/Sanad.App.Family.postman_collection.json`
  (+35/-6, also corrected the plan-change error code to `Subscriptions.Quote.ChargesNotConfigured`).
  Executed: Release build of `Sanad.UnitTests` with `/p:TreatWarningsAsErrors=true`
  → 0/0; focused
  `dotnet test --filter "FullyQualifiedName~Families.Subscriptions|FullyQualifiedName~Cms.Library|FullyQualifiedName~API.FamilySubscriptionsController"`
  → Passed 305 / Failed 0 / Skipped 3 / Total 308; Postman JSON parses as UTF-8
  (ConvertFrom-Json, 19 items); `git diff --check` exit 0; `subscriptions.md`
  anchors resolve. NOT RUN: full integrated sln gate (parent), live two-device
  HTTP readback (no runtime started), DB-level unique-index enforcement of the
  pending-attempt guard (would need a migration — report only). A full
  unit-suite run (`--no-build`, same Release binaries as the focused pass) was
  attempted and **aborted**: the test host crashed with
  `Import refused: ReportAlreadyExists` / `Import refused: ReviewedPlanHash`
  (a data-import guard, not subscriptions) → Failed 13 / Passed 5705 / Skipped 3.
  Those 13 are out of M2 scope (Identity account/support/verification and CMS
  HelpCenter unsupported-audience tests, the same cluster recorded in the prior
  `testout3.txt`/`testout5.txt`); none are in
  `Families.Subscriptions` / `Cms.Library` / `API.FamilySubscriptionsController`,
  which the focused filter covers green. No persisted-data
  correction required: seeded plan versions already carry the approved set (no
  plan withholds key 2) and no existing subscription row grants a benefit its
  plan withholds; the read-then-write pending guard shares the existing renewal
  TOCTOU pattern.
- M3: completed — CareHome create confirmed in source (no
  reimplementation). Route
  `POST /api/v1/care-homes/residents/mine/{residentId}/care-records`,
  `CareHomeCareRecordsController.Create` (CareHomeCareRecordsController.cs:190,
  policy `CareHomeOwnerAccess` at :35), source/attribution server-derived via
  `CareRecord.CreateForCareHome` (CareRecord.cs:138, `CareRecordSource.CareHome`
  at :153). Surfaces in both reads confirmed: family read combines all three
  sources when no `source` filter (FamilyCareRecords.cs:73-81 with
  CareRecordContracts.cs:124-133); elderly read same (ElderlyCareRecords.cs:61-69).
  Documented: new `docs/care-homes/care-records.md` (create Request/Response +
  both read routes), extended `docs/app/families/care-records.md`, index row in
  `docs/care-homes/README.md`. Postman refreshed: Sanad.CareHomes.Owner folder
  11 description, family elderly-read descriptions. Existing Postman entries
  already covered the create and both reads; no new entries were needed.
  Validated: 3 collections parse as UTF-8 JSON; `git diff --check` exit 0;
  doc links resolve. Limitations: doc-only — examples are authored, not run;
  care-home create has no Idempotency-Key layer (by source), noted in docs.

## M2 correction - live persisted-data drift (2026-10-26, Owner-authorized)

A read-only live check on the rehearsal `SanadDb` proved the M2 verdict "no
data correction required" wrong for **persisted rows** (the source code is
correct - the frontend report reproduced from drifted persisted rows):

- `families.subscription_plan_versions` `plan_key='premium'`:
  `Library` (benefit_key 2) was `is_included=false` on versions
  v1, v2, v3, v5, v6, v7 (v4 true) - source grants key 2 on every plan;
  `PremiumContent` (benefit_key 8) was `true` on v3 - source grants key 8 to
  PremiumPlus only, so premium must be false.
- `families.family_subscription_benefits` for the 14 premium-subscription
  snapshots (incl. the demo family on premium v1): key 2 = false on all 14
  (drift copied from the plan versions); key 8 already false everywhere.

**Correction EXECUTED 2026-10-26T18:36Z (read-only pre-check first; one
transaction; guarded idempotent UPDATEs with opposite-value guards so a rerun
matches nothing; no insert/delete, no other benefit keys, `gold` untouched):**

| Guarded UPDATE (plan_key='premium' scope) | Run 1 rows |
|---|---|
| `subscription_plan_version_benefits`: key 2 `false -> true` | 6 |
| `subscription_plan_version_benefits`: key 8 `true -> false` | 1 (v3) |
| `family_subscription_benefits`: key 2 `false -> true` | 14 |
| `family_subscription_benefits`: key 8 `true -> false` | 0 (already correct) |

Idempotency proof: an identical second run affected **0 rows** on every
statement. Post-check: all 7 premium versions now `key2=true, key8=false`; 0
drift rows remain on both tables; every premium version and every premium
subscription still carries exactly its 8 benefit rows (no row added or
removed); `gold` untouched (open observation: `gold` has no source-defined
package; its single version carries key 2 and key 8 both `true` - needs its
own Owner decision if ever aligned). No migration ran; no provider/financial
call involved.

## Release record (2026-10-26, Owner-authorized)

- Pushed commit `47329bf5e9bc22a0ba8d01a8b7d145830dbb924e` (18 files, +1353/-31)
  to `origin/main` (`b22d9fa0..47329bf5`), staged-diff secret scan clean (1181
  added lines scanned, 0 findings). Package published at the same revision
  (manifest revision == pushed SHA; 110 files; no `.env`,
  `appsettings.Development.json`, keys/certs or `*.local.json`).
- Integrated gate on the frozen tree: strict Release build 0 warnings / 0
  errors (first run failed on stale `testhost` file locks left by worker test
  runs - a process-level issue, not a source defect; re-run green); focused
  tests **326 passed / 0 failed / 3 skipped** across
  `~FamilyInvitation|~Families.Subscriptions|~Cms.Library|~API.FamilySubscriptionsController|~RequestLengthBoundOpenApiContractTests`;
  the stale-log concern `VisitSettingsClosureReason_PublishesTheExactRawValueMaxLength500`
  confirmed present and GREEN on the fresh binary (class 14/14; the
  `[property: StringLength(500)]` annotation at `CareHomeVisitsController.cs:33`);
  full `Sanad.UnitTests` suite **5752 passed / 0 failed / 27 skipped**;
  architecture 1/1; provisioner 75/75; `git diff --check` exit 0; 4 touched
  Postman collections parse as strict UTF-8 JSON; all changed markdown
  links/anchors resolve.
- Deploy to the rehearsal (72.62.92.144, unit `sanad-api`, `Staging`, DB
  loopback `SanadDb`): package zip sha256 `9b4be825...97b875b` verified on
  target before extraction; binary backup `/root/sanad-api-backup-pre-47329bf5`
  (111 files) taken before stop; service stopped, application files replaced
  (host `appsettings.json` + `appsettings.Staging.json` preserved by
  exclusion), restarted. Config fingerprints byte-identical before/after:
  `/etc/sanad/sanad.env` `fad7e7fc...`,
  `/root/sanad-api/appsettings.Staging.json` `fd96e26d...`, host
  `appsettings.json` `200332b4...`, unit `c018192e...`. Startup log: **9/9
  contexts, each `0 pending migration(s) / 0 migration(s) applied`** (no
  migrations this phase), `NRestarts=0`, listening `http://127.0.0.1:5001`.
- Verification: `Verify-SanadDeployment.ps1` **exit 0** over the public base -
  revision match (`47329bf5...`), required/applied 9/9 match, smoke
  `GET /api/v1/lookups/services` HTTP 200. Public `http://72.62.92.144:8091`
  reachable (`/api/v1/deployment/info`: `Staging`, DB reachable). New route
  `GET /api/v1/family/invitations/sent` exists: anonymous **401 (not 404)**;
  public OpenAPI `/openapi/v1.json` (571 paths) contains the new path with a
  `get` operation.
- Provisioner tool refreshed at `/root/sanad-demo-provisioner` at the same
  revision: new DLL sha256
  `60efa2080c087f7e4cb41ce0957c1a30e053cb2d1191e8c65f4473bade2dcc02` (sources
  unchanged this phase; the byte difference vs the prior `756bddf3...` is a
  non-deterministic rebuild, not a source change), host `appsettings.json`
  preserved, `--help` smoke only - **no provisioning run**.
- Limitations / unverified: (1) no authenticated readback of the sent-list
  endpoint was performed - exposure is proven by the anonymous 401 plus green
  handler/controller tests, not a live authenticated response; (2) the
  plan-change 409 paths and the premium `canAccess` flip are unit-proven at
  `47329bf5` but not live-proven over HTTP (no payment flow executed, per
  constraint); (3) the DB correction is proven on the rehearsal target only -
  any other environment seeded through the same path needs its own authorized
  correction; (4) `gold` package alignment remains an open observation; (5)
  consumer-doc examples (care-record Request/Response) remain authored, not
  run. Full send-ready answers for the app team:
  [mobile-response-2026-10-26.md](mobile-response-2026-10-26.md).
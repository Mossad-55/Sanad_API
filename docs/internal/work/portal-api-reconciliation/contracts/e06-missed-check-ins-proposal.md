# E06 — Missed daily wellness check-ins: Owner-approved phase scope brief

**2026-10-20 sequencing amendment:** the [approved follow-up contract](frontend-follow-up-2026-10-20.md)
now owns delivery order and persistent local HTTP verification. E04 is released;
ready operational follow-up fixes precede E05/E06. Existing cohort/deadline/read
semantics below remain approved; new alerts, reminders or alternative deadlines
are not included. Refresh base/file ownership at dispatch. No Worker/start/release
authorized by this amendment.

**2026-10-20 Mastermind dispatch amendment (contract-authorized).** The follow-up
contract pre-authorizes the Mastermind to move E06 ahead of E05 after amending the old
coordination dependency and checking actual shared ownership. That check is done: the only
shared path between the two manifests is
`docs/postman/admins/Sanad.Admin.postman_collection.json`, and E05 additionally owns
`docs/postman/app/Sanad.App.Family.postman_collection.json`, which E06 does not. Ownership
is therefore serialized in the opposite direction — **E06 acquires the Admin Postman
collection first and E05 acquires it only after accepted E06 delivery** — and the two never
run concurrently. F7 runs alongside E06 and owns no shared path with it. Dispatch is
authorized by the Owner's wave direction (F7+E06 before E05+F8); base HEAD is the pushed
`main` tip `66c96b6`; the phase Worker is Worker A `opencode/beam-free#high` (fallbacks
`opencode/minimax-m3.1-flash-free#high`, `opencode/nemotron-3.5-super-vl-free#high`, then a
River model; report every actual switch). No scope, behavior or acceptance line below
changes.

**Owner-approved scope; implementation not started — 2026-10-19.** Audience: Admin operations developers.
Owner expressly directed: “select all the best recommended options and lock them and approve them.” This brief records that approval and the delegated timing/cohort defaults below. Phase state: `approved` (queued); release state: `not_requested`.
Scope approval is not start/implementation, Worker dispatch or release permission. This single brief owns locked scope, pending tasks and current evidence; retain its existing filename for continuity.
**Dispatch-ready and dispatched 2026-10-20:** no remaining Owner product decisions. E04 is
released and accepted; the E05 dependency is satisfied by the Mastermind's ownership
amendment above (E06 first, E05 after). Owner dispatch direction is the approved wave order.
Phase state: `accepted` (2026-10-21); implementation tasks completed by Mastermind;
release authorized by Owner's continuing all-phases direction, pending verified release.

## Intake and verified source evidence

- Requirement: expected population, window/timezone/grace and derived feed. Owner confirms no UI screens are available; none are awaited or required. The locked defaults below need no further product intake and are delegated scope decisions, not claims about existing behavior.
- Historical E06 row: `docs/internal/work/portal-api-reconciliation/plan.md` at revision `b847de46226e9f5089e8a044d3c2d23458aef3ac`; the current compact file no longer carries the rows.
- `docs/app/elderly/check-in.md` and `src/Modules/Families/Application/Sanad.Modules.Families.Application/CheckIns/ElderlyCheckInCommands.cs`: identity-bound daily answer, stored profile IANA local date, same-answer retry, conflicting-answer rejection; negative answer triggers a separate notification flow, not missing status.
- `docs/admin/elderly-check-ins.md`, `src/API/Sanad.API/Controllers/AdminElderlyCheckInsController.cs`, `src/Modules/Families/Application/Sanad.Modules.Families.Application/CheckIns/AdminElderlyCheckInQueries.cs`: recorded-answer reads, payload-free access audit before querying, non-deleted Family filter, bounded dates and minimal projections. These do not derive missing answers today.
- `src/API/Sanad.API/DependencyInjection.cs` verifies `ElderlyMedicationOperationalRead`: Normal SuperAdmin/SupportAdmin. No new policy or CMS access is required.
- `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Elderlies/Elderly.cs` stores `IdentityUserId`, `FamilyId`, `CreatedOnUtc` and `TimeZoneId`; `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Elderlies/ElderlyTimeZone.cs` validates IANA IDs with initial default Cairo. Neither defines enrollment or missed deadlines.
- `src/Modules/Families/Application/Sanad.Modules.Families.Application/Abstractions/Identity/IFamilyIdentityGateway.cs` exposes batched profile/status reads, already used for dependents. `src/API/Sanad.API/IdentityIntegration/FamilyIdentityGateway.cs` returns an empty list on query failure: empty/missing profile results cannot safely prove a zero eligible population.
- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/User.cs` and `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Users/UserStatus.cs` provide current account state, not historical activation intervals. `src/BuildingBlocks/Application/Sanad.BuildingBlocks.Application/Abstractions/IDateTimeProvider.cs` provides an injectable UTC clock.
- `docs/care-homes/booking-lifecycle.md` and `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeStayCommands.cs` distinguish arrival/confirmation/dispute timestamps; `docs/app/families/bookings.md` and `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Bookings/Booking.cs` distinguish caregiver visit start. Those event families remain separate scopes.
- `tests/Sanad.UnitTests/Families/CheckIns/ElderlyCheckInCommandTests.cs` covers retry/conflict, deleted Family, invalid zone and DST conversion; `tests/Sanad.UnitTests/Families/CheckIns/AdminElderlyCheckInQueryTests.cs` covers projections/ranges/auditing. Inspection is not new executed evidence.
- Technical readiness: `UserStatus.Active = 2`; `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/GetUserProfilesByIds.cs` projects `(int)user.Status` for requested IDs. `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/DependencyInjection.cs` registers `IDateTimeProvider` with `SystemDateTimeProvider`; API scanning already includes Families handlers. No Identity dependency, gateway contract or shared registration edit is required; prove the existing integer boundary with focused fixtures rather than changing it.

## Objective and locked scope decisions

1. **Event:** daily Elderly wellness only, one expected answer per Elderly/profile-local date. Both positive and negative saved answers qualify. Preserve submission uniqueness/retry/conflict behavior. Care Home arrivals and caregiver appointment starts require separate event contracts; no cancellation/transfer/extension rules are imported here.
2. **Current cohort:** persisted identity-bound Elderly profiles linked to a non-deleted Family whose resolved Identity user currently has Active status. No first-answer prerequisite, notification-preference enrollment, assisted/non-identity profiles or separate enrollment table. Missing/unresolvable identity status makes evaluation indeterminate, not silently excluded; explicitly inactive accounts are excluded.
3. **Expectation start/end:** delegated current-facts rule: first expected day is the first full profile-local calendar day after Elderly `CreatedOnUtc`; omit creation day. At evaluation, use current cohort eligibility for every requested date; exclude dates before that start and dates not yet due. No claim that creation was consent/enrollment, or that present Active status proves historical activity. Deletion/inactivation removes the profile from recomputed historical results too; reactivation can restore results within the range. No historical activation reconstruction is promised.
4. **Time/deadline:** stored profile IANA timezone, one local calendar day. Deadline is the next local midnight (exclusive end of the day), with **zero grace**; at evaluation instant equal to deadline the unanswered day is due. This simplest bounded timing rule is a delegated recommendation selected under Owner authority, not existing source behavior. No nonzero grace or configurable schedule is added.
5. **Exceptional time:** resolve actual zone rules, never a fixed Cairo/UTC offset. A missing/invalid IANA zone, ambiguous/nonexistent midnight needed for eligibility/deadline, or unusable creation timestamp makes the entire evaluation indeterminate. Return a safe error, no partial missing list/count. Ordinary DST changes with valid unambiguous boundaries work normally. Current timezone is used for all requested dates; no past-zone reconstruction.
6. **Derived/current facts:** compute at one captured server `evaluationOnUtc`; qualifying saved answers with `AnsweredOnUtc <= evaluationOnUtc` match by Elderly/local date. Late answers remove rows on the next evaluation, including historical range reads; do not retain a late/missed snapshot. Past responses may differ. No business-state writes, stored missed records, reminders, alerts or notification recipient selection; existing required access audit remains a write.
7. **Bounds/readers:** Normal SuperAdmin/SupportAdmin only through existing operational read policy. Required inclusive `startDate`/`endDate`, maximum 31 dates; optional Elderly ID filter; page default 1, page size default 20/max 100 with existing normalization. Sort local date descending, then Elderly ID ascending; counts are after cohort/date filtering and before paging. No cross-request snapshot guarantee as source facts can change.
8. **Minimal projection/retention:** response includes `evaluationOnUtc`, `items`, `page`, `pageSize`, `totalCount`; items contain only `elderlyId`, `familyId`, `localDate`, `dueOnUtc`. No names, actor IDs, raw timezone, contact, clinical, medication or payment data. No derived-feed retention; existing source/access-audit retention is unchanged. This is missing-current-record evidence, not a punctuality metric or clinical conclusion.

## API/error and audit contract (approved new behavior, not implemented)

- `GET /api/v1/admin/elderly/check-ins/missed` uses `ElderlyMedicationOperationalRead`. Success is `200` with the bounded response above; no qualifying missing events is an empty page, not an error. Missing/out-of-scope Elderly filter returns an empty page unless source evaluation is indeterminate.
- Validate required dates/range before audit; use existing `400 Families.AdminCheckIn.InvalidDateRange` for invalid ranges and standard validation for missing dates. Audit before reading cohort/check-in data with existing payload-free `CheckIn` resource convention and distinct `ListMissedCheckIns` action. Audit failure stops the read.
- `401/403` follow existing policy. New `409 Families.AdminCheckIn.IndeterminateEvaluation` for invalid time/identity facts; `503 Families.AdminCheckIn.EvaluationUnavailable` for observable source failures. The existing gateway's swallowed failure/missing-profile ambiguity maps conservatively to indeterminate. Never return a misleading successful zero or leak failed identifiers/profile details.
- Keep new HTTP error mappings local to the selected Admin controller, without E04-owned shared mapper/policy edits. Capture evaluation time once with the existing clock abstraction; do not accept caller-controlled “now.” Separate source reads are not a cross-context transactional snapshot; report evaluation semantics accurately.

## Explicit out of scope

Care Home arrival or caregiver appointment feeds; alerts/reminders/SMS/push/email; background evaluation; schedules/grace configuration; acknowledgment/escalation/resolution; stored missed snapshots; historical enrollment/timezone reconstruction; Family/owner-facing feed; frontend screens, clinical inference, data repair and changed submission behavior. Any addition needs separate scope authority.

## Sequential tasks — all pending

| Task | Dependency | Acceptance | State |
|---|---|---|---|
| 1. Implementation preparation | E04 implementation and E05 delivery accepted; incompatible ownership released | **Mastermind preflight recorded 2026-10-20:** base HEAD pinned to the pushed `main` tip `66c96b6`; Worker A `opencode/beam-free#high`; the sole shared path with E05 (`docs/postman/admins/Sanad.Admin.postman_collection.json`) confirmed and the ownership amended so E06 acquires it first; no manifest/new-file collision with F7 in the same wave. The remaining read-only re-confirmation of the `Active=2` gateway mapping, the audit/clock wiring and the bounded batch/query translation checks is the Worker's first task-2 step, named in its dispatch prompt. No new Owner policy or UI input | completed |
| 2. Derived feed and operational boundary | Accepted task 1 + Owner dispatch direction | Approved cohort/time/matching rules, safe indeterminate behavior, paging/projection and audit enforcement pass focused tests; Mastermind accepts handback | ready_for_review |
| 3. Consumer delivery and review | Accepted task 2 | Admin guide/Postman and Elderly semantics boundary match implementation; focused HTTP/schema checks and final build pass; combined diff reviewed | ready_for_review |

Worker updates only assigned task/result and stops at handback; Mastermind alone completes tasks/phase states. Worker A was dispatched 2026-10-20 for task 2 (session recorded in Current result). Maintain one current result, not a separate tracker/report.

## Exact selected-scope file manifest — future implementation only

Repository-relative paths. **New** marks exact planned files, not files created now; their parents are the directories shown and already exist. Existing Identity gateway/Family/CheckIn models are read-only dependencies; no migrations or new gateway contract are needed for this bounded projection. Additional necessary paths require a bounded Mastermind amendment, never concurrent E04 edits.
Ownership sequencing (amended 2026-10-20): E05 and E06 share the Admin Postman collection; **E06 acquires it first and E05 acquires it only after accepted E06 delivery releases ownership.** No E04 source/test files or shared policy/registration/error-mapper edits are included. The inspected dependencies fit this manifest; query translation is implementation verification, not a missing scope decision.

Wave gate (2026-10-20): F7 (ratings visibility and moderation) runs concurrently and is the only phase in this wave with a schema change; E06 runs **no** migration and applies none. Use localhost port **5249** (never 5247), and never write a fixture row F7 owns — the Caregiver/Care Home rating rows, their new moderation/flag tables, or F7's synthetic booking, facility and identity rows.

- `src/API/Sanad.API/Controllers/AdminElderlyCheckInsController.cs`
- **New:** `src/Modules/Families/Application/Sanad.Modules.Families.Application/CheckIns/AdminMissedCheckInQueries.cs`
- **New:** `tests/Sanad.UnitTests/Families/CheckIns/AdminMissedCheckInQueryTests.cs`
- `tests/Sanad.UnitTests/API/AdminElderlyCheckInsControllerTests.cs`
- `docs/admin/elderly-check-ins.md`
- `docs/app/elderly/check-in.md` (document derived-feed boundary only; no submission change).
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- **New:** `tools/Invoke-E06LocalHttpVerification.ps1` (real localhost evidence on port 5249, required by the follow-up contract's per-phase local HTTP gate).
- `docs/internal/work/portal-api-reconciliation/contracts/e06-missed-check-ins-proposal.md` (this lifecycle record).

## Focused verification and safety

- Cover Active/inactive/missing identity and gateway failure, non-deleted/deleted Family, first full day/creation-day exclusion, no first-answer/preference dependency, filtered empty cohort versus failed evaluation, and recomputation after eligibility changes.
- Cover exactly-at-deadline and either side, zero grace, different zones/local midnight, DST short/long days, invalid zones and ambiguous/nonexistent boundary indeterminacy, current-zone changes and history limitations. Use a deterministic clock, no sleeps or real date dependence.
- Cover positive/negative/late answers, duplicate retry/conflict regression, one row per Elderly/date, post-evaluation answers, inclusive 31-day range/reversed or missing dates, stable paging/counts, role denial, minimal DTO and audit-before-read/failure behavior. No booking lifecycle mutations occur.
- Bound source enumeration/batched identity lookups; no clinical/contact data in feed/errors/audit/log payloads. Existing profile gateway can return extra fields internally: consume only ID/status and do not serialize or retain the rest. Query translation/performance must be verified without expanding into a persistent reporting dataset.
- Deliver selected guides/Postman with the feature as primary API truth. Validate Markdown links/JSON and actual method/path/status/schema agreement. Strict Release solution build plus focused affected tests: successful exits and zero warnings; isolated synthetic/disposable fixtures only. No automatic full-suite/Bruno, live API/DB/providers or migration operations. Distinguish authored/executed evidence and run `git diff --check`.

## Current result, technical follow-up and release gate

Scope/policy blockers: **none**. UI awaited: **no**; unavailable screens need no follow-up. Task 1 owns ordinary technical preflight; task 2 proves translation/batching and the already-verified status/clock conventions with focused tests. Swallowed gateway failures remain conservatively indeterminate. Current-facts history limitations are approved, not unresolved policy. Worker model/profile confirmation is dispatch-time operations only.
Current evidence (2026-10-21): Worker A `ses_ea47ed0c9ffecoNn0ngNzihvSo` supplied implementation but left a three-line HTTP stub. Mastermind completed the adapter and corrected required-date binding, ambiguous/invalid midnight handling, observable source-failure mapping (503), date/page overflow, and the fixed-offset deadline/incorrect late-answer tests. Cohort reads project only needed fields; Identity lookups are batched in groups of 200 and retain only status. Strict Release solution build: exit 0, zero warnings/errors. Focused classes: 23/23 passed. `pwsh -NoProfile -File tools/Invoke-E06LocalHttpVerification.ps1`: exit 0, 14/14 actual localhost checks on port 5249, including required dates, 31-day bound, minimal projection, creation-day/late positive and negative answer exclusion, paging/counts, unknown cohort, 409 unresolved identity, role denial and logout. No migration.

Limitations: audit/source failures and exceptional DST boundaries are isolated tests, not induced shared-database outages. The feed materializes minimal current cohort facts and at most 31 expected dates per profile; total cohort cardinality is not capped because that would falsify totals. Two sessions from the adapter's first run were retained after an incorrect logout path returned 404; corrected runs log out their own sessions. No unrelated sessions were revoked. Fixed synthetic rows are retained, INSERT-only provisioning adds missing rows only. No live provider or authenticated Production request is claimed. Next action: serialized code-only release, then E05 may acquire the Admin Postman collection.

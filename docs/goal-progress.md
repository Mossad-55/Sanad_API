# Elderly UI/API Gap-Closure Goal — Progress

Last verified: 2026-09-28. This checklist tracks the active implementation goal; it does not authorize deployment or production-data changes. `Done` means supported by repository/test evidence, not merely reported. Existing unrelated and private worktree changes are preserved.

Current checkpoint (2026-09-28): Phase 6, Elderly OTP implementation is in progress; Phase 2 recipient-flow slice is Done. Phase 2 evidence: API build 0 warnings/0 errors, focused `FamilyNotificationRecipientGatewayTests` 7/7, and isolated scoped Bruno 20/20 requests/assertions. Family owner/viewer, active-booking caregiver and SupportAdmin recipient assertions all passed. Fixture resets affected only `SanadBrunoTestDb`; temporary isolated workspace was removed. Public audit and notification docs were updated; Postman remains unchanged with the gap documented. Historical pre-fix check-in 500 root exception remains unconfirmed, although the current route passes. OTP owner contract: require active Elderly identity and profile; reject every ineligible phone with the same 404 response; no real SMS. Local OTP verification must blank SMS Misr credentials and use the development no-op sender.

## Phase 1 — Baseline, decisions, and foundations

- [x] **Done** — Review the current audit and handoff; preserve completed profile/timezone, Family emergency-contact, and other already-landed work. Evidence: `docs/operations/elderly-ui-audit.md`, current handoff, and current branch/worktree review.
- [x] **Done** — Shared durable notification inbox and Admin metadata-only list/detail/timeline/aggregate foundation. Evidence: implementation and focused tests; solution test run passed.
- [x] **Done** — Notification schema/migration startup path and EF recipient lookup translation corrections. Evidence: local API/DB exercise and successful full solution build/test.
- [x] **Done** — Safe opt-in fixture seeding guard and disposable local fixture setup. Evidence: `TestUserSeedTargetGuard` tests and review of `TestUserDataSeeder`; seeding restricted to Development and explicit credentials/target safeguards.
- [x] **Done** — Help-request dynamic catalog/request and in-app recipient fan-out bounded slice. Evidence: focused tests and help-request Bruno run (14 assertions passed; one request skipped).
- [x] **Done** — Record deferred push/email/provider/outbox/retry work and recipient policy in the operational audit/handoff. Evidence: public audit and private handoff entries.

## Phase 2 — Recipient-flow regression coverage

- [x] **Done** — Verify deterministic overdue-medication fixture exists and is opt-in/idempotent. Evidence: `TestUserDataSeeder.EnsureMedicationFixtureAsync` creates/validates an active medication and deterministic overdue dose using Elderly local time and CMS lateness threshold.

Latest checkpoint (2026-09-28): reviewer confirmed the reordered login flow, event destination IDs, recipient policy, and SOS idempotency assertions. Bruno now also tests conflicting same-day check-in as 409. The earlier `CS0246` and apphost-lock warnings were resolved; the focused gateway test passed 7/7 with zero warnings. Recover only the authorized `SanadBrunoTestDb` fixture state, then rerun Bruno. The exact original check-in 500 remains unconfirmed.

Gate update (2026-09-28): added `using Sanad.BuildingBlocks.Domain.Primitives.Ids;` to the already-dirty fixture seeder to resolve the `CS0246`; focused `FamilyNotificationRecipientGatewayTests` then passed 7/7 with zero warnings. A preceding attempt emitted `MSB3026` apphost-copy warnings because a lingering `Sanad.API.exe` held the output; it was stopped by exact PID and that warning-bearing attempt is not counted as green. The Bruno collection remains unrun after these edits.
- [ ] **In progress** — Bruno recipient-flow coverage for each behaviorally affected endpoint: check-in, medication evaluation, and SOS. Help-request flow is already covered and must not be repeated. Final isolated scoped run passed all 20 requests and 20 assertions after the Family-owner login ordering and deterministic medication fixture selection fixes. Verified success, auth failures, one-shot check-in, SOS replay, Family owner/viewer, active-booking caregiver and SupportAdmin inbox recipients. Test DB recovery was restricted to the named disposable fixture accounts and rows. The original historical check-in 500 exception remains unconfirmed; the current implementation passes. Public audit/handoff update remains before marking this bounded slice complete.
- [ ] **Not started** — Update audit/handoff with exact verified results and any test limitations after the runs.

## Phase 3 — Dynamic Elderly experience and CMS content

- [x] **Done** — Wellness-tip Elderly feed/detail and CMS authoring lifecycle bounded slice. Evidence: audit/docs, implementation tests, and existing Bruno contract requests.
- [x] **Done** — Help-request sentence-builder CMS catalog bounded slice. Evidence: implementation/tests and help-request Bruno flow.
- [ ] **Not started** — Elderly welcome/onboarding content contract and CMS-backed implementation. Current splash API is shared across roles; placement, targeting, benefit tiles, and CTA mapping are **Needs Owner Verification**. Do not change the shared splash contract until resolved.
- [ ] **Not started** — Remaining dynamic Elderly API gaps from the audit, if any, after reconciling against delivered profile, medication, check-in, SOS, help-request, notification, and wellness slices; implement only genuinely open behavior.

## Phase 4 — Admin operational inspection and least privilege

- [x] **Done** — Admin notification metadata-only operational views with audited access and aggregate content metrics policy. Evidence: implementation, focused tests, and audit/docs.
- [x] **Done** — Admin check-in, medication, help-request, and SOS operational APIs and their scoped authorization/audit policies. Evidence: current audit, route implementations, and focused tests; recipient Bruno verification remains tracked separately in Phase 2.
- [x] **Done** — Dedicated operational access separated from ContentAdmin authoring/aggregate content metrics. Evidence: permission policies and audit entries.
- [ ] **Blocked** — Decide whether/how support staff may inspect Elderly profile/contact fields: emergency-contact name/relationship/phone, DOB/age, photo, detailed address, health notes, and latest assessment. Public audit marks these **Needs Owner Verification**; do not expose without owner decision.
- [ ] **Not started** — Implement and test any approved Admin profile/contact views, including audited sensitive reads, after the visibility decision.

## Phase 5 — Contract sync, verification, review, and handoff

- [x] **Done** — API contract mapping and Postman collection consistency check. Evidence: `Verify-ApiContractMapping.ps1`: 306 actions, 346 Postman requests, zero missing/orphan; Postman JSON valid.
- [x] **Done** — Full solution build and test gates for the current integrated tree. Evidence: `dotnet build Sanad.slnx --no-restore --verbosity minimal` succeeded with 0 warnings/0 errors; `dotnet test Sanad.slnx --no-build --no-restore --verbosity minimal` passed Architecture 1/1 and Unit 2040/2040 with no warnings.
- [ ] **In progress** — Complete required sequential scout → implementer → test author → reviewer → documenter workflow for remaining bounded code/test/doc slices, one worker at a time. Current next role depends on Bruno findings; do not rerun completed roles for already-delivered slices.
- [ ] **Not started** — Re-run only gates invalidated by later changes; ensure all endpoint Bruno coverage passes before any commit. Every build/test gate must have zero warnings.
- [ ] **Not started** — Verify exact final diff and stage only authorized tracked files; never stage private handoff files or protected/unrelated paths.
- [ ] **Not started** — Commit/push only after explicit authorization and all applicable gates; no deployment or production mutation in this goal.
- [ ] **Not started** — Deliver consolidated implementation, API/auth, persistence, test/docs, deferred decisions, and deployment-readiness analysis.

## Phase 6 — Elderly OTP pre-validation

- [x] **Done** — Scout the Elderly OTP request flow, account lookup/eligibility rules, OTP provider boundary, existing tests and Bruno coverage before implementation. Evidence: scout mapped the handler, SMS sender, tests, Bruno/Postman/auth docs and confirmed current silent 204/PendingVerification behavior. Owner decisions: require a matching active Elderly account/profile; reject all ineligible numbers before OTP dispatch using HTTP 404 and one stable “Elderly account not registered” response; local-only verification with no real SMS.
- [ ] **In progress** — Implemented active Identity/account checks plus matching Elderly profile in a non-deleted Family before OTP persistence or dispatch. Invalid cases share the stable 404 error. Focused tests, Bruno, docs/Postman sync, and verification remain.
- [ ] **Not started** — Add focused tests for registered active Elderly, unregistered phone (including assertion that dispatch is not called), and relevant inactive/wrong-account cases.
- [ ] **Not started** — Add and successfully run Bruno coverage against authorized local/test fixtures only; do not send real OTP/SMS.
- [ ] **Not started** — Sync API/auth documentation and Postman, review the diff, and rerun affected zero-warning build/test gates before commit consideration.

## Decisions / deferred items

- **Needs Owner Verification:** Welcome-screen placement/content-to-API mapping and Elderly-specific targeting.
- **Needs Owner Verification:** Support/Admin access to the listed profile and emergency-contact fields.
- **Deferred to Notifications/Events:** Push and email providers, outbox/job processing, retries, scheduling, and broader event inventory. In-app durable notifications are in scope where the delivered endpoint slices create them.
- **Needs Owner Verification:** SOS-specific notification preference semantics; current implementation follows the recorded help-request preference behavior pending decision.
- **Owner-approved:** Recover disposable fixture state in `SanadBrunoTestDb` only; clear not-registered OTP response; require an active Elderly account/profile; local-only OTP test with no real SMS.
- **After current goal:** Once this goal is actually complete, prepare a separate next-goal task file containing the owner's next tasks; do not begin that next goal until directed.
- No production deployment or production data operation is authorized by this checklist.

## Current status override (2026-09-28)

- Phase 2 recipient-flow Bruno and public-doc update: **Done**. Evidence: isolated 20/20 Bruno requests/assertions, gateway tests 7/7, and API build 0 warnings/0 errors.
- Phase 6 OTP scout: **Done**. Owner-approved rejection contract: HTTP 404 with the same stable "Elderly account not registered" response for all ineligible phones.
- Phase 6 OTP implementation: **In progress**. Active Elderly identity and matching non-deleted-family profile are required before persistence or SMS dispatch; invalid cases use the stable 404 code/message. Focused tests, Bruno, docs/Postman sync, and verification remain with subsequent roles. Local Bruno must run only with SMS Misr credentials blank and the Development no-op sender selected.
- Phase 6 OTP test author: **Done (authored)**; focused test gate is **Blocked** by the compile error at `RequestElderlyLoginOtpCommandHandler.cs:59` (handler returns `Error` instead of `Result`). Coverage includes active success, unknown/inactive/wrong-account/profile mismatch with no persistence/dispatch, profile query cases, and Bruno 204/404 requests. Bruno was not run; reviewer and documenter roles remain.
- Phase 6 OTP reviewer: **Done**. Confirmed eligibility/fail-closed behavior and found two actionable items: fix Error-to-Result compile failure; add a strict localhost guard and no-op transport assurance before the positive OTP Bruno request can run. Coverage gaps noted for multiple Elderly accounts and failed profile-query result; unregistered Bruno should also assert the stable detail. Documenter is next, then mastermind resolves these findings before gates.
- Phase 6 OTP documentation: **Done**. Auth guide/error catalog/overview and Postman examples now document 204/404, ineligibility and local no-op-only positive calls; Postman JSON and diff check passed. Postman collection was not run. Mastermind resolved the compile return type, added strict local Development/no-op guards to both Bruno cases, made the fixture runner clear inherited SMS Misr credentials for its API child, and added the stable-detail assertion. Next: zero-warning build/test, then guarded local Bruno; remaining reviewer coverage findings are audited against the identity invariant and fail-closed gateway.
- Phase 6 OTP focused gates: API build **Done** at 0 warnings/0 errors; focused tests **Done** 56/56 at 0 warnings after correcting Family/Caregiver test fixtures to satisfy required verified email/password activation. An earlier focused run had 2 fixture setup failures and was not counted green. OTP Bruno remains unrun until the guarded Development/no-op fixture API starts.
- Phase 6 OTP Bruno: **Done**. Isolated guarded local-fixture run passed both requests and all four assertions: registered phone 204; unknown phone 404 with exact stable code/detail. The runner cleared inherited SMS Misr credentials; no real SMS was sent. Full-solution zero-warning gates and final contract/diff/handoff checks remain.
- Final current-tree gates: `dotnet build Sanad.slnx --no-restore --verbosity minimal` **Done**, 0 warnings/0 errors; `dotnet test Sanad.slnx --no-build --no-restore --verbosity minimal` **Done**, Architecture 1/1 and Unit 2047/2047, zero warnings.

## Final task-status override (2026-09-28)

This latest checkpoint is authoritative over earlier in-progress notes above.

- Phase 1 baseline/foundations and already-landed profile/timezone, Family contact, durable inbox, CMS and fixture work: **Done**; completed slices were not repeated.
- Phase 2 notification recipient regression: **Done**; focused gateway tests 7/7 and scoped Bruno 20/20 requests/assertions.
- Phase 3 dynamic Elderly APIs, help-request builder and wellness CMS: **Done**. Reconciliation found no additional implementation gap in approved delivered routes. Welcome placement/audience/content/CTA mapping: **Blocked — Needs Owner Verification**; no shared splash contract expansion without that decision.
- Phase 4 operational Admin check-in/medication/help-request/SOS/notification views and least-privilege split/auditing: **Done**. Admin inspection of sensitive Elderly profile/contact fields and any implementation: **Blocked — Needs Owner Verification**.
- Phase 5 documentation/Postman consistency and complete sequential role passes for bounded slices: **Done**. Auth Postman JSON parses; the mapping script could not run under the current PowerShell execution policy (prior verified census remains 306 actions / 346 requests, no missing/orphan). Full build **Done** (0 warnings/errors); full tests **Done** (Architecture 1/1, Unit 2047/2047, zero warnings); required Bruno gates **Done** (recipient 20/20 and OTP 2/2, no real SMS). Public audit and private handoff updated. Exact final diff/staging/commit/push remains **In progress**; protected and unrelated paths remain excluded.
- Phase 6 Elderly OTP pre-validation: scout, owner decision, implementation, focused tests 56/56, local guarded Bruno 2/2 (4/4 assertions), auth docs/Postman and full gates: **Done**. Unknown and all ineligible phones return the same 404 before OTP state or SMS dispatch; registered active Elderly with matching usable profile retains 204.
- Owner-verification items are not implementation blockers to this authorized scope; they remain explicitly blocked in the audit. No deployment or production-data change is authorized.

## Closeout checkpoint (2026-09-28)

- Exact final diff and staging audit: **Done**. Verified `HEAD == origin/main == 4758e25114960a0a8d002f41f636172e3ec81726`; reviewed candidate source, tests, docs, Postman and fixture files, and `git diff --cached --check` passed. Only reviewed goal files are staged.
- Protected paths explicitly excluded from staging: `Sanad_Operations.md`, `Sanad_Master_Context.md`, `UI/`, `subscription-vat-tax/`, `tests/Bruno/environments/vps.bru`, and the three unrelated booking test files. `src/API/Sanad.API/appsettings.Development.json` is also excluded because it is local environment configuration and is not required to deliver the code.
- The final source review confirms the OTP eligibility check fails closed before OTP persistence/dispatch, returns the approved uniform 404, and the test-fixture API safety guard/no-op SMS setup stays opt-in and local. Recipient changes include assigned active-booking caregivers and SupportAdmin, with focused gateway/Bruno coverage.
- Current task: commit/push and final handoff: **In progress**. Remaining: commit/push the reviewed staged set, record the resulting commit, create the separate next-goal task file, and ensure protected/unrelated work remains untouched. No deployment or production data mutation.

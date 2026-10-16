# FUR-01 — Mastermind acceptance review

Date: 2026-10-16 (Africa/Cairo).
Review baseline: HEAD `4b65369d760c8af6b2242ebd2022a6cb10057580` plus
the worker's uncommitted FUR-01 delivery and preserved planning delta.
**Current verdict: accepted after R1–R5 corrections; deployed and verified.**
The initial findings below are preserved as review history; the final re-review
appendix supersedes the original changes-requested disposition.

Contract: [approved FUR-01](../contracts/fur-01-contract-accuracy.md).
Delivery: [worker report](fur-01-contract-accuracy.md).
Correction handoff: [bounded brief](../worker-prompts/fur-01-review-corrections.md).

## Confirmed sound

- All seven production controller changes match the approved metadata-only
  boundary. A comparison of HEAD versus working files after removing
  ProducesResponseType attributes and whitespace is identical for all seven.
  No handler, route, policy, DTO or runtime-body edits found.
- The 29 success annotations match current command/query result types and the
  controller/base-controller mappings, including media 201 and removal 204.
- Existing isolated host was reused; new representative HTTP cases clearly use
  deterministic dispatch fakes or the existing isolated inventory fixture.
  These are not provider/PostgreSQL/runtime-soak claims.
- Assessment formats, certificate policy, reason bounds and unread integer
  documentation corrections inspected are consistent with existing behavior.

## Required corrections

### R1 — Auth response examples still contradict runtime (blocking)

`docs/auth/email-password-login.md` lines 100/116 still show `userId` as a
plain string and line 122 shows normal `deviceSessionId` as a string.
`docs/auth/refresh-and-sessions.md` line 68 still shows the response session
ID as a string. The request at line 61 must remain a plain GUID. Correct
response examples to actual wrapper shapes and explain extracting `.value`
for refresh/header inputs without changing request or response wire contracts.

These files are already in the approved manifest. Postman examples do not
compensate for wrong canonical guides. The worker report's assertion that its
test literals match the guides is not established by the current tests.

### R2 — Activation ordering prose is inaccurate (blocking contract truth)

`docs/auth/registration-and-verification.md` line 87 says email verification
does not activate and attributes activation to phone verification. The existing
VerifyOtpCommandHandler checks both flags after either supported purpose; the
last successfully verified channel can activate a PendingVerification user.
Correct the guide to that existing rule. No runtime/policy change is requested.

### R3 — Example tests do not exercise published literals (blocking acceptance)

PortalAuthExampleContractTests serializes synthetic constructed DTOs and checks
selected fields; it never reads or binds the actual Markdown/Postman examples.
It therefore passes while R1 remains in those guides. Its summary comment and
delivery report overstate the current proof.

Within the existing two test files, add a minimal targeted bridge to the actual
relevant documented JSON/collection bodies. Bind request examples to actual DTOs
with MVC options and assert required fields; compare response example structure,
nullability, wrapper IDs and fields to actual response serialization. Synthetic
values/dates/tokens may differ; tests must prove shapes/values relevant to the
contract, not require identical sample GUIDs or send providers. Cover the three
guides and seven edited auth Postman response bodies. Demonstrate that the old
plain response-ID examples are rejected by this check. No broad fixture/framework
or host/config change is needed. Add a focused assertion or equivalent proof
for corrected activation-order prose; do not falsely claim code execution from a
documentation string check.

Strengthen nested visit schema checks in the owned metadata test file to follow
the actual kind/status refs and verify the existing enum values/names. Merely
finding components with those names does not prove a response property uses them.

### R4 — Eleven broken audience-report links (blocking handoff acceptance)

All three phase-01 reports are four directories beneath `docs/`. They currently
use `../../../internal/...`, which resolves beneath `docs/frontend-handoffs/`,
not `docs/`. Eleven Markdown file links fail Test-Path across those reports.
Correct to the appropriate four-level relative paths and validate every local
Markdown link across the audience folder, including the index. Keep phase-00
reports and the corrected Admin/App taxonomy intact. Do not delete stray/empty
directories or historical files as incidental cleanup.

### R5 — Attribution and delivery status in reports (blocking truthful handoff)

The Admin and owner reports classify genuinely absent success metadata (#34)
as a frontend contract mismatch; the frontend correctly reported those gaps.
Separate backend metadata defects fixed locally from actual incorrect client
assumptions such as an invented envelope or five-state booking enum. Distinguish
the accurate availability method correction from the general missing-schema claim.
No frontend code repository was inspected: avoid claiming proven code defects.

Replace `FUR-01 (published, local)` with `implemented/tested locally; not
published/deployed` or equivalent. Some statements refer to old documented runtime
behavior while the new schema additions are local; distinguish those explicitly.
Remove any placeholder claims that examples match a guide until R3 proves it.

## Mapping-gate limitation independently assessed

The existing script extracts controllerRoutes/postmanRoutes but never populates
postmanMissing/postmanOrphan. Its printed 0/0 is vacuous; count stability alone
does not prove route coverage. This script defect predates FUR-01 and editing it
is outside the phase's production/test manifest; no silent script change here.

Mastermind ran the script read-only and independently built case-insensitive
HashSet<string> keys from each extracted `Method + normalized Route`, then
compared both directions. Actual result:

- 556 extracted controller entries / 620 Postman requests.
- 556 unique controller keys / 556 unique Postman keys.
- Actual set difference: **0 missing / 0 orphaned**.

Record this compensating evidence, not merely the old script's static labels.
Worker may repeat the bounded read-only comparison for its revised report;
no route-parser/framework rewrite is requested. Keep the underlying gate-script
repair as an explicit independent follow-up for owner scoping, not a false
claim that it was fixed or that the current comparison was a new API test.

## Verification performed in this review

Read-only diff/source/tests/guide/report inspection; local link resolution;
independent route-set comparison; seven controller non-metadata equality checks.
No new dotnet build/test, provider, database, live API or VPS operation run.
Worker's reported 172/172 and strict build are recorded as worker evidence;
they have not been independently rerun or invalidly relabeled Mastermind passes.
New tests will invalidate their compiled-input gate and require an updated build
and focused run after corrections. No need to rerun full suite or later phases.

## Next action

Owner gives the bounded correction brief to the same worker. Worker updates
only the specified in-scope docs/tests/reports and returns corrected evidence.
No commit/push/publish/deploy or final Done until this acceptance gap is closed.

## Final re-review — R1–R5 accepted, 2026-10-16

Actual corrected guides, tests and audience reports inspected. R1 response-ID
wrappers and plain request/header GUIDs now agree; R2 describes either supported
channel completing the both-flags activation rule. R3's seven literal-file
conformance tests are in the existing approved test file, not an extra host or
out-of-scope production file. Nested visit refs now verify their exact enum
values/names. R4/R5 local links and accurate backend/frontend attribution/status
are corrected. No further in-scope correction is outstanding.

Mastermind-executed gates on the corrected working tree:

- Strict solution build `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true -v minimal`: exit 0, zero warnings/errors.
- Guarded full unit release gate `Test-SanadPortalGate.ps1 -Suite Unit`: exit 0,
  **3421 passed / 0 failed / 8 skipped / 3429 total**. Skips: five deliberately
  disabled PostgreSQL integration cases and three pre-declared Families cases.
  All nine targets dummy; integration keys removed and seeders/migrations off in
  the actual test child. No real database/provider operations authorized or run.
- Guarded architecture gate: **1/1**, exit 0. Initial restricted-runner attempts
  aborted on testhost communication timeout; discarded, not counted as product
  failures or successful gates. Valid reruns used local runner communication.
- All 20 Postman collection JSONs parse. Repeated the actual script extraction
  and independent case-insensitive route-key sets: 556 controller entries,
  620 requests, **556 unique keys each, actual zero missing/orphaned**.
  The worker's narrower 547-key extraction explains its nine apparent file-route
  orphans; it does not invalidate this exact full extraction. Script defect
  remains out of scope; not represented as repaired.
- **32 audience local file links resolve, zero broken**. This supersedes earlier
  snapshots' link counts, not their historical evidence.
- Seven controller files match HEAD after removing response annotations and
  whitespace; no runtime bodies/routes changed. Diff whitespace check exit 0.
  Git emits LF/CRLF normalization notices for some files; these are not compiler
  warnings or whitespace errors.

Local raw `Frontend_Report.md` is preserved and locally excluded from version
control; it is not staged with the sanitized reports. No feature implementation
was taken over by the Mastermind. Local acceptance authorizes the standing
complete-phase commit/push/release workflow; **final Done waits for the deployment
receipt**. FUR-02 remains Not started.

Later release update: the [deployment receipt](fur-01-deployment.md) is complete.
Accepted source `f9231fb` is pushed and deployed, 29/29 actual metadata operations
verified, nine migration heads unchanged/zero applied, service healthy and
configuration preserved. FUR-01 is Done. Direct public-path redirection from
this machine remains explicitly unproven; on-host/tunneled verification passed.

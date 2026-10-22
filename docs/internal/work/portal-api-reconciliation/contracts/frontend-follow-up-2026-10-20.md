# Frontend follow-up — approved scope and execution contract

Owner decisions: 2026-10-20. Status: **scope approved; implementation not started**.
Release: `not_requested`. No Worker dispatched. This document owns the approved
follow-up scope and execution order, not a second task ledger. Before dispatch,
the Mastermind prepares only the next bounded phase brief with its exact file
manifest, pinned revision/local delta, acceptance checks and selected model.
Do not create briefs for every future package upfront.

Source intake: Owner-supplied `BACKEND_ENDPOINTS_REQUEST.md`, dated 2026-10-20.
Read-only reconciliation used current controllers, handlers, domain rules,
consumer docs and earlier verification records; live failures were not reproduced.
Unresolved additions belong only in the [clarification note](../frontend-clarifications-2026-10-20.md).
They are not approved implementation requirements.

## Delivery order and ownership

### Owner-authorized two-worker execution amendment

**Optional review/release delegate (Owner-approved):** coordinating Mastermind
may use `openai/gpt-6-luna#high` for one bounded read-only diff/evidence review.
This is not an intermediate implementation manager and must not spawn workers.
The delegate reports findings, exact reviewed revision/diff, gates/limitations and
proposed release manifest/target, then stops. No commit/push/publish/deploy/migration
on the review instruction. Coordinating Mastermind must explicitly confirm the
report and issue a separate scoped release instruction before the delegate releases.
If the reviewed inputs change, review only the affected delta before authorization.
The release executor follows existing target/backup/provenance/verification runbooks
and returns deployed revision, migration outcome, smoke results and limitations.
Only coordinating Mastermind accepts the final report and closes the phase.

If Luna is slow/unavailable, coordinating Mastermind may omit that delegation and
review/release directly, or review directly and delegate only the approved release
execution to an available authorized Worker. Do not create duplicate reviews or
overlapping release executors; stop/confirm the old executor idle before reassignment.
Maintain one serialized release at a time. These options require no repeated Owner
approval within the existing scope/target, but do not expand product/data authority.

Owner subsequently approved up to two implementation workers after F2 closeout,
with coordinating Mastermind directly owning file boundaries, review, integration
and releases (no intermediate Sub Mastermind). Worker A:
`opencode/beam-free#high`; Worker B:
`river/deepseek-ai/DeepSeek-V4.1-Flash#high`. Current F2 replacement continues on
River; do not restart it for this amendment. Keep Mastermind work focused on
decisions/review, not duplicate implementation or repeated test execution.

Owner preauthorizes quota/unavailability fallback for these workers: prefer River
and available OpenCode free models; for Beam use an available OpenCode free model
with high support (verified candidates: `opencode/minimax-m3.1-flash-free#high`,
`opencode/nemotron-3.5-super-vl-free#high`) before the last-resort
`kilo/openrouter/auto`. Recheck availability/pricing before a switch; Kilo Auto's
zero catalog price does not prove all routing is free, so use only when free-only
routing is verified. Never substitute paid Auto Balanced or an unapproved paid
model. Record and report every actual switch; stop the prior execution before
transferring file ownership. This is explicit Owner fallback authority, not a
general repeal of the governance no-silent-switch rule.

Candidate waves: F3+F4, F5+F6, F7+E06, E05+F8, E08+E07. These are conditional on
exact disjoint file/fixture ownership and resolved product decisions, not permission
for shared-file races. E05 must precede E08 reference-aware deletion; F5 notification
integration must settle before F8. E06 may move ahead of E05 only after Mastermind
amends the old coordination dependency and checks actual shared ownership. Serialize
shared-context/migration edits, shared fixture mutations, integration and deployment.
Use concise preparation/implementation/verification handbacks and act promptly on
completion/blocker notifications; do not poll running background workers. The
following original sequential sequence remains the fallback when overlap is unsafe.

One Worker per bounded phase, sequential by default. Each owns implementation,
focused tests, affected consumer guides/Postman and local HTTP evidence.
The Mastermind reviews actual changes and results, requests bounded corrections
from the same Worker, and accepts the phase. Do not create documentation relay
workers, duplicate reports or repeat valid checks. Scope approval is not dispatch
or release authorization. No model is selected by this document.

Recommended order: L1 → F1 → F2 → F3 → F4 → F5 → F6 → F7 → F8,
then existing E05/E06 and bounded E07/E08. Independent unresolved questions block
only their dependent behavior. Prioritize ready daily-use fixes over optional work.
The package labels below are scope references, not new parallel trackers.

## L1 — Persistent local fixtures and fast verification

- Use local `SanadDb` for persistent synthetic manual/API fixtures. First verify
  effective host/database for all nine module contexts, migration state, existing
  records, storage root and non-live integrations. Never infer target from its name.
- Owner approves the local persistent-fixture approach and modification of
  designated synthetic records, not deletion/reset of existing data or writes to
  the VPS. Migration application remains separately authorized.
- Reuse existing seed/domain logic with an explicit persistent-local mode or
  bounded provisioner. Existing `TestUserSeedTargetGuard` pins to
  `SanadBrunoTestDb`; preserve that automated-test guard rather than disabling it.
- Start with two families, three dependents, two caregivers (medical/companion),
  two facilities (Draft/Approved), and only required Admin roles. Start smaller
  when the current endpoint needs fewer records. Add two or three feature rows
  only when needed; target at most ten synthetic fixture rows per feature table.
  Role links, audit/history and migrations may naturally exceed ten. Preserve
  existing geography/reference data and unrelated records.
- Stable fixture identities; reruns add missing fixtures rather than duplicating
  users or resetting manually progressed scenarios. Replenish consumed booking/
  refund/dispute cases only when required. Prefer domain flows to raw status edits.
- Reuse a few Owner-supplied or existing suitable synthetic images in persistent
  storage. No dependency on new photos to test unrelated endpoints. Keep database
  and asset references consistent. Keep credentials outside Git and reports.
- Use a lightweight script in existing repository tooling for repeatable HTTP
  checks; Postman stays the consumer/manual interface. No new framework, seed
  API, test dashboard, bulk-data project or recurring database recreation.
- Start the API once and reuse it. Preflight reachability and login; stop on
  either failure. Never silently fall back to the remote base URL.
- Test relevant success, filter/paging, unknown/invalid input, auth/role and
  ownership cases. For writes, read back the resulting state. Retain useful
  fixtures. Do not automatically retry mutations.
- Failure output: check name, method/path, expected vs actual result, safe error
  code/detail, trace ID and matching local exception/application location when
  available. Capture API logs locally and correlate traces; explicitly say when
  correlation is unavailable. Redact tokens, credentials, connection strings and
  sensitive payloads. Do not dump entire logs.
- Reuse existing error infrastructure: coded safe business errors, field errors
  for invalid input, safe unexpected 500 responses and detailed local server logs.
  Fix gaps in selected endpoints only; no global error subsystem rewrite.
- Keep cheap isolated unit tests. Use real PostgreSQL verification where query
  translation/persistence matters. Destructive/concurrent automated tests stay
  isolated; they must not reset persistent `SanadDb`.
- Acceptance: repeat setup without duplicates/data loss; run one small endpoint
  check end to end; show actionable failure diagnostics without secrets. Do not
  deliberately introduce production errors to demonstrate this.

## F1 — Confirmed failures and consumer contract accuracy (A4/A11/A12)

- Reproduce affected elderly check-in list/aggregate, SOS and help-request ID
  filters on PostgreSQL; inspect related query paths, fix confirmed translation
  defects and verify real/unknown IDs, empty results and access auditing. Do not
  claim the suspected typed-ID `.Value` cause proven before reproduction.
- Correct FAQ audience validation/error handling together with F5's approved
  owner support, or temporarily return a coded validation error while unsupported.
- Publish actual success DTOs/statuses for reported Admin/owner operations.
  Operational views, receipts, internal notes and visits are not all booking DTOs.
- Document actual bounds and missing inventory conflict behavior. Compare current
  source/OpenAPI/Postman before editing; do not assume all 77 reported gaps exist.
- Preserve current wrapper/plain-GUID response conventions and numeric enums.
  No global serialization change. Existing auth examples, numeric visit days and
  field-level library errors are not automatic new work.
- Acceptance: focused relational regressions and HTTP/schema checks; changed
  docs/JSON agree with runtime. Reuse successful checks; no automatic full suite.

## F2 — Booking/chat read completeness (A2/A16)

- Owner list: actual decision deadline while awaiting decision, resident Arabic
  name/age, responsible contact name/phone/relationship and care notes, using
  stored booking facts and existing ownership visibility. Preserve current fields.
- Admin Care Home list: nullable `extensionOfBookingId` and stored `taxAmount`.
- Chat list: existing scope and nullable booking ID; preserve enum/wire conventions.
  Stay/room-type summaries remain excluded pending the clarification note.
- Acceptance: correct state-dependent deadline/nulls, ownership denial, no
  unrelated medical data, correct extension/base booking distinction; no payment
  or booking-state changes. Publish actual response schemas and examples.

## F3 — Approved-facility revision carry-forward (A2a/A13)

- New editable revisions inherit unchanged approved documents/media through
  revision-specific records referencing existing stored assets, not duplicate
  downloads/uploads. Preserve approved revision/history and media kind/order.
- Preserve unchanged document verification attribution and expiry; replacements
  follow normal review. Never convert expired/rejected documents into valid ones.
- Return `openRevisionId`. Submission/reviewer reads consistently use the editable/
  submitted revision's effective assets; no hidden submit-time fallback that
  restores intentionally removed media. Preserve two-photos-including-cover rule.
- Handle existing incomplete drafts without overwriting new uploads or resurrecting
  intentional removals. If removal cannot be distinguished from missing inheritance,
  stop that repair and ask; do not guess or run bulk production backfill.
- Acceptance: phone-only edit can submit without re-uploading valid unchanged
  assets; replacements/removals remain effective; expired/rejected documents do
  not bypass approval; old approved reads/files remain intact under concurrency.

## F4 — Community restriction discoverability (A1)

- Add paged newest-first restriction reads, author/active filters, existing
  restriction/revocation facts and author display names; add current active
  restriction ID/expiry to post detail. Reuse existing revoke and moderation policy
  (SuperAdmin/ContentAdmin), durations and reason bounds.
- Acceptance: another moderator/session can find and revoke an active restriction;
  active filtering respects expiry/revocation; existing creation/revoke behavior stays.

## F5 — Owner help and support inbox (A4/A14)

- Owner FAQ audience supported end to end with existing support contact. Anonymous
  contact-only read for signup; do not expose the authenticated FAQ surface wholesale.
- Reuse persisted support tickets: Admin paged list/detail/reply/status and own-ticket
  list/detail for supported signed-in users. SuperAdmin/SupportAdmin manage tickets;
  ContentAdmin/FinanceAdmin do not. Users submit/read, not reply, in this scope.
- Statuses New/InProgress/Resolved/Closed; status changes are explicit, never an
  automatic effect of replying. No reopening initially; closed-ticket replies conflict.
  Pin the exact transition matrix in the active brief before implementing status writes.
- Replies are durable with actor/time; attempt email and in-app notification and
  retain readback even if delivery fails. Never equate a saved reply with delivered
  email/push. Reuse existing delivery infrastructure, not a new messaging subsystem.
- No attachments, assignment, SLA engine, live chat or user conversation workflow.
- Acceptance: saved ticket appears in Admin reads; only its submitter can read
  user-side detail; replies survive delivery failure; allowed status changes and
  role denials are verified. Publish numeric statuses and precise delivery semantics.

## F6 — Minimal operational reads (A8/A15)

- Read-only family list/detail: IDs, names, member roles/status and dependent
  associations; no medical data, impersonation or editing. Use obvious name/ID
  lookup and bounded paging; extra subscription/search enrichment is not included.
- Admin Care Home visits list/detail with facility/status/date filters and existing
  cancellation/event facts. No Admin scheduling, cancellation or decision override.
- Extend existing caregiver booking detail with missing caregiver display fields
  and available recorded lifecycle facts. Do not invent a complete historical ledger.
- New operational reads: SuperAdmin/SupportAdmin. Preserve existing legitimate
  Finance access on existing booking routes; do not broaden access to new family data.
- Acceptance: minimal fields, correct ownership associations, safe role boundary,
  known/unknown IDs and bounded reads. App-rating feedback remains deferred.

## F7 — Ratings visibility and moderation (A8/A15)

- One consistent policy for caregiver and Care Home ratings, not duplicate generic
  and Care Home workflows. Owners read their facility ratings anonymously with
  stars/text/time and aggregates; Admin can inspect/filter and hide/restore with reason.
- Hidden ratings leave visible reviews and aggregate averages/counts. Restore makes
  them eligible again. Family edits never automatically unhide a moderated rating.
- Owner may have one unresolved flag per rating; Admin hide/keep resolves it with
  attribution. SuperAdmin/SupportAdmin moderate; owners cannot moderate or identify
  reviewers. Pin route shapes, flag-resolution concurrency and existing aggregate
  read paths in the active brief; do not change rating eligibility rules.
- Acceptance: hidden/restore behavior agrees across existing aggregate consumers,
  owner isolation/anonymity holds, repeated flags do not duplicate unresolved work,
  edits cannot evade moderation, and moderation actions retain actor/time/reason.

## F8 — Readable notifications (A10)

- Correct confirmed raw-ID bodies and English-only templates through existing
  language/template mechanisms. Keep routing identifiers in structured destination
  data; preserve links and delivery behavior. No global historical notification rewrite.
- Acceptance: newly generated supported notifications are readable in the chosen
  language, contain no raw IDs in human copy and retain working destination routing.

## Remaining roadmap

- E05: retain its [approved brief](e05-family-home-cms-proposal.md), authenticated
  Family reads and bounded service navigation. Public reads, expiry/scheduling and
  arbitrary links from the new report are not added; await clarifications for those.
- E06: retain its [approved brief](e06-missed-check-ins-proposal.md): derived daily
  wellness feed, no reminders, snapshots or incident workflow. Explain existing
  approved semantics; do not invent a different deadline from the frontend wording.
- E07 approved minimum: append-only caregiver review notes with author/time,
  SuperAdmin/SupportAdmin using existing review policy. No edit/delete/workflow.
  Pin text bounds, paging and routes in its bounded brief before dispatch.
- E08 approved minimum: delete only unreferenced landing assets; references include
  retained history/revisions and Family-home assets. Reject referenced deletion.
  Pin shared-asset ownership/reference discovery before dispatch. No automatic sweeper.

## Scope exclusions and evidence reuse

- **A5 OTP removed:** Owner confirms SMS Misr sends OTP correctly. No Development
  environment, fixed OTP, verification bypass endpoint or SMS reimplementation.
- Report calls VPS dev; last verified deployment is Production. Do not enable
  Development-only test clock, fixture seeding or provider stubs there.
- A6 reactivation and A7 Draft finance: existing fixes/verification, not scheduled
  redevelopment. Reopen only on current evidence. Unready users legitimately get 409.
- A9 orphan caregivers: diagnosis only on a separately authorized target; no automatic
  delete/hide/repair. Do not use persistent fixture setup as production cleanup authority.
- Keep SuperAdmin-only user counts; server-side payment credentials; FAQ deactivate;
  archived-library new-copy workaround; Supplier postponed. No new gateway settings API.
- Growth beyond registrations, multi-account audience changes and additional frontend
  requests remain outside scope until resolved. Missing support contact can retain its
  documented first-run status; no unnecessary status-code change.
- No separate frontend test-data project. Share existing local API fixtures only if
  useful; external access/hosting is not provisioned by this contract.

## Mastermind acceptance and release

Prepare one exact file manifest and endpoint acceptance matrix for the active phase;
do not let a Worker select policy from an ambiguity. A phase is not dispatch-ready
merely because its objective is approved. Reuse existing source and consumer docs;
keep evidence concise in the active brief. Strict build/affected tests must succeed
with zero warnings; local HTTP script verifies the changed API. Validate changed
Markdown links/Postman JSON. Do not rerun gates unchanged inputs have not invalidated.
After review mark `accepted`. Commit/push/deploy and migration application require
separate Owner release authority. `completed` requires verified release or explicit
release N/A. No implementation or release is authorized by this documentation task.

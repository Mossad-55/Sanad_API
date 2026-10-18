# FUR-08 whole-phase worker brief — Owner dispute readback

## Authority and pinned baseline

The Owner explicitly approved the complete FUR-08 contract on 2026-10-18.
Implement only the contract below. The initial Kilo, OpenRouter and TopAI
attempts are paused. TopAI `topai/kimi-k2.7-code` failed at session startup with
“Not enough one-time welcome tokens remain for this request.” The Owner selected
OpenAI `openai/gpt-6-luna#medium` for the next dispatch. Verify the effective
Worker profile before dispatch and do not switch routes automatically. Pinned
repository HEAD at
original intake: `2bf92a0` (`docs: record FUR-07 production release`) plus the
pre-existing local delta. The worktree contains unrelated modified and
untracked files. Preserve them; do not reset, clean, stage, or overwrite unrelated
work.

Continuation/replacement note (2026-10-18): the first child,
`ses_eb435f1bbffeYfLV5kSJbZ7Iml`, was interrupted at the Owner's request after
repeating no-op edits while trying to correct a malformed test line. Its final
state was `cancelled`; it returned no completion report. Inspect and continue
from its persisted changes rather than restarting or overwriting them. A new
untracked root file `zz_probe.txt` containing `A` appeared during that child;
it is outside this phase manifest. Do not edit/delete it or treat it as phase
evidence. A second child, `ses_eb3fc8768ffeXj0TlXk7TZngrB`, was interrupted at
the Owner's request and confirmed `cancelled`; its diff did not change the
persisted implementation. A third child, `ses_eb3e97d67ffeLqMDtMu12FoG7D`, used
OpenRouter and was also interrupted at the Owner's request and confirmed
`cancelled`. It attempted to launch a nested `opencode run` (which timed out),
then wrote an unaccepted delivery report while leaving required tests,
documentation and Postman incomplete. A fourth child,
`ses_eb3d8dbabffeatPaQ4bHiljpK9`, failed at startup with a welcome-token quota
error and made no verified changes. A fifth child,
`ses_eb3abb0feffeJCouDAJek74uSh`, returned `BLOCKED`. Its final report says it
made no edits, but Mastermind's post-completion diff inspection found persisted
changes to the application handler/controller and three test files. Treat actual
source/diff as authoritative, not that report. No successful build/test result
was recorded. OpenAI `openai/gpt-6-luna#medium` was selected and verified for
that attempt. The child is terminal. The Owner authorized a bounded continuation
in the same session `ses_eb3abb0feffeJCouDAJek74uSh`; it returned `COMPLETE`,
replaced the inaccurate report, and reported a zero-warning Release build,
focused tests 19/19, Postman JSON/route check, and diff check. Its initial report
was contradicted by persisted edits.

Mastermind's read-only review of the current diff found these bounded corrections;
verify current files before changing anything:

- The Postman request uses `{{page}}` and `{{pageSize}}`, but neither variable is
  defined in the Owner collection. Make the example executable with safe defaults
  (or omit optional params to use server defaults); keep status optional.
- The OpenAPI contract test checks nullable fields are present but does not assert
  nullability for the contract's nullable item fields or the paging envelope.
- Pagination/order tests use distinct opening timestamps; add a same-timestamp
  case proving the case-ID tie-break direction.
- Reconcile the focused-test/build commands and counts with the final delivery
  report. Reuse valid worker evidence only after verifying it covers the current
  exact inputs; do not repeat valid checks without a reason.

The source validator, Owner guide, Postman request, and report now exist and appear
to match the approved behavior. These remaining items are bounded Mastermind review
findings, not permission to expand scope. No release is authorized.

Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
`docs/internal/Mastermind_Handoff.md`,
`docs/internal/work/portal-api-reconciliation/plan.md`, this brief,
`docs/internal/Project_Main_Goal.md`,
`docs/internal/operations/codex-workflow.md`, and
`.opencode/agents/sanad-worker.md`. Inspect the current diff before editing.

## Objective and approved behavior

Add the owner-facing, read-only check-in dispute list:

`GET /api/v1/care-homes/check-in-disputes/mine`

- Require existing `CareHomeOwnerAccess` authorization.
- Derive and enforce the facility scope server-side from the authenticated
  owner. Never accept caller-selected facility scope. Include only disputes
  associated with facilities owned by that actor.
- If the owner has no facility, return `200` with a valid empty page. Document
  and test this deliberate exception to existing owner finance reads.
- Query parameters: `page` default 1 and minimum 1; `pageSize` default 25 and
  range 1–100; optional `status` accepts only `Open` or `Resolved` (case-
  insensitive names). Invalid values use the standard API validation response.
- Return `{ page, pageSize, totalCount, items }`. Count after owner and status
  filtering and before paging. Sort by `openedOnUtc` descending, then case ID
  descending for ties.
- Each item exposes case ID, booking ID, facility ID, status, opened timestamp,
  nullable checkout timestamp, nullable Family reason, nullable resident Arabic
  and English name snapshots, nullable original booking amount/currency
  enrichment, and nullable resolved/effective-check-in timestamps.
- `checkoutOnUtc` can be null for a Family-submitted dispute before checkout;
  `familyReason` can be null for a checkout-triggered case. `effectiveCheckInOnUtc`
  and `resolvedOnUtc` are null while open.
- If the related booking is unavailable, resident name snapshots,
  `originalBookingTotalAmount`, and `originalBookingCurrency` are null. When
  available, amount is the booking's stored `TotalAmount`. The booking does not
  persist currency; report the current fixed `EGP` convention, not a stored value.
- `effectiveCheckInOnUtc` is the Admin's case outcome only; it must not rewrite
  the booking's recorded check-in or checkout timestamps.
- Do not expose Admin resolution evidence, rationale (`Reason`), or resolver
  identity in the owner response. Do not expose resident/family identifiers or
  payment/refund/payout details. Add no owner mutations or resolution powers.
- Preserve existing Admin and Family routes, DTO wire shapes, and behavior.

Owner-facing documentation describes a read-only open/resolved list, status
filter, empty/loading/error states, and that Sanad support handles cases. It
must not imply a dispute changes facility-recorded timestamps.

## Owned files / expected manifest

Own only these feature files and the phase evidence listed below. Inspect before
editing; if implementation requires a material file outside this manifest, stop
and report the exact need rather than expanding scope.

- `src/API/Sanad.API/Controllers/CareHomeBookingRequestsController.cs`
- `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeStayCommands.cs`
- Relevant existing focused tests, expected candidates:
  `tests/Sanad.UnitTests/API/CareHomeOwnerPortalTests.cs`,
  `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs`,
  `tests/Sanad.UnitTests/CareHomes/CareHomeReadbackTests.cs`, and
  `tests/Sanad.UnitTests/API/CareHomeBookingControllerTests.cs`
- `docs/care-homes/booking-lifecycle.md`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- Create `docs/internal/work/portal-api-reconciliation/reports/fur-08-worker-delivery.md`
  with changed files, checks/results, assumptions, fixture/data effects,
  limitations, and explicit no-release status.

The phase plan and Mastermind handoff are Mastermind-owned checkpoints. Do not
edit `docs/internal/work/portal-api-reconciliation/plan.md` or
`docs/internal/Mastermind_Handoff.md`; the Mastermind records the child session
ID at dispatch and updates status/evidence after your report.

Do not modify worker profile, governance, unrelated report/contract files,
unrelated Postman collections, migrations, persistence schema, or production
configuration. If a separate feature contract file is needed, stop and ask.

## Acceptance criteria

1. Endpoint follows current owner controller/policy conventions and enforces
   owner-to-facility scope in the query, not merely at the controller policy.
2. Pagination, status validation/filter, post-filter total count, deterministic
   ordering, empty owner result, and no-facility empty result match the contract.
3. DTO/OpenAPI nullability and fields match the contract, including unavailable
   booking enrichment and case outcome vs original booking timestamps.
4. Tests establish positive own-facility visibility and foreign-owner isolation;
   empty/no-facility results; open/resolved and nullable scenarios; pagination,
   filter/count/order; missing booking enrichment nulls; no Admin-only evidence,
   rationale, resolver identity; outcome timestamp does not rewrite booking
   timestamps; and no owner resolution permission.
5. Owner guide and Owner Postman collection match the verified endpoint,
   authorization, filters, paging, response, nullability, and no-mutation rules.
6. No migration, database write, production operation, provider call, Bruno run,
   commit, push, publish, or deploy.

## Verification

- Add/run focused automated tests for the changed endpoint, query/projection,
  authorization/scope, and directly relevant API response schema. Include any
  static Owner Postman JSON/route mapping checks required by actual changes.
- Run one strict solution build after final code changes; zero warnings/errors
  required. Run only relevant focused tests; full suite is not required unless
  changed risk or failures warrant it.
- Run `git diff --check` for owned changes. Do not run Bruno or stateful tests;
  no database target/fixture authorization is part of this contract.
- Reuse valid evidence where inputs remain unchanged. Report exact commands,
  counts, skips/warnings, failures, and limitations; never claim an unrun check.

## Stop conditions and report

Do not invent policy or expand the manifest. Stop and report `BLOCKED` if actual
model/query conventions make the approved fields or scope ambiguous, a required
change exceeds the manifest, or checks require an unauthorized stateful target.
Use a no-loop rule: inspect current file contents before editing; never repeat a
no-op edit or repeat an unchanged failing command. After one failed attempt,
diagnose from the returned evidence and make one materially different,
evidence-based correction. If it fails again without new evidence, stop and
report `BLOCKED` with the exact error and files changed. Otherwise complete the
entire phase and return one concise `COMPLETE` report with changed files and
evidence, then stop. Do not commit/push/release or begin another phase.

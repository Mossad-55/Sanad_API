# Portal report follow-up — locked queued plan

## Control record

- Owner approved locking this plan and preparing parallel worker tasks on 2026-10-24.
- Phase: approved, queued. All tasks pending; no workers dispatched.
- Start dependency: current media/care-records/payments/Rentals slice must first
  be released and its release receipt verified. Then Mastermind activates this
  brief and dispatches without repeating settled intake.
- This plan does not authorize release of either slice, VPS fixture writes,
  production test clocks, deletion of existing data, or real refunds.
- At activation pin the actual released SHA and residual local delta. Never use
  today's dirty workspace as an assumed clean release baseline.
- Worker model: Owner-selected River AI `river/zai-org/GLM-5.3-Flash`.
  Retain configured reasoning; no unapproved provider/model changes.
- Mastermind owns this status record, integration, final review and release coordination.

## Inputs and settled decisions

Source: Owner-supplied backend report dated 2026-10-23, items 1–11. Its live
observations are historical, not current-source proof. Compare deployed revision,
current source, and supplied report before labeling any finding fixed or reproduced.
The host called “dev” in the report is classified as Production in repository
decisions. Fixtures default to a verified local target; a remote fixture request
requires exact-target authority and must not enable a production test clock.

1. Duplicate-charge claims require **Support approval**. Submission must not
   approve a ledger award or execute a payout. Preserve the full-refund policy
   for a verified duplicate/erroneous charge, server-calculated and bounded by
   the remaining capture budget. No partial automatic approval.
2. Review state and refund execution state are separate. Approval is not proof
   that money moved. Family sees its own claim/result; authorized Support sees
   list/detail and audited approve/reject actions.
3. Replays, concurrent approvals/execution, failed responses and unconfirmed
   provider outcomes cannot cause a second payout. Reconcile uncertainty before
   retry. Existing recorded awards are not newly executable claims.
4. Preserve existing cancel-renewal and service-shutdown policies. Changes to
   those policies are excluded.
5. Confirmed caregiver bookings must be discoverable by Admin with family and
   caregiver names, booking date and refund state. Preserve the current closed
   list default and existing finance filters.
6. Prefer clarifications over duplicate features: numeric visit days and current
   auth request shapes already exist; public legal publication is a content task;
   partner optional fields and notification producers require version-aware checks.
7. Keep medical audit ID-only, gateway secrets server-configured, FAQ deactivation,
   and registration-only growth in this scope. Additional audit PII, gateway secret
   management and permanent FAQ deletion are deferred, not silently approved.
8. App-rating Admin reads and a controlled wellness-category taxonomy are optional
   follow-ups pending explicit product/role decisions. Document current wellness
   free-text categories and existing image upload behavior now.

## Dispatch topology and ownership

Parallel authoring is approved, but only on the disjoint manifests below. Workers
may read shared files. They return proposed shared-file edits to Mastermind rather
than modifying them concurrently. One serialized build/test/database/provider slot
belongs to Mastermind; workers author focused tests and hand back commands.

| Task | State/session | Wave | Owned scope | Acceptance |
|---|---|---|---|---|
| W1 Booking discovery/outcome | pending / none | A, parallel | `AdminBookingsController.cs`; Families `Bookings/AdminBookingQueries.cs`; booking-list filter definitions; dedicated booking-list/outcome tests and booking consumer guide | Confirmed filter, nullable names, stable pagination, preserved default/finance filters, invalid-combination validation; actual outcome 204/state/category/reason-limit matrix documented and tested |
| W2 Duplicate-charge claims | pending / none | A, parallel | Families subscription refund commands/claim reads; Finance new claim/review/execution types and handlers; dedicated Admin claim controller; family subscription controller; related dedicated tests and subscription guide | No submission-time award; audited Support decision; isolated family reads; provider execution/reconciliation semantics; concurrency and capture-budget proof; compatibility plan |
| W3 Contract/documentation audit | pending / none | A, parallel | Report item 11 controller annotations outside W1/W2; CMS/Help/PartnerContact schema corrections only where reproduced; auth/visits/landing/help/wellness guides; dedicated schema tests | Typed success schemas or honest 204; exact existing limits; optional partner fields correct; ordered resolved landing refs distinguished from raw JSON; already-fixed issues marked with evidence |
| W4 Identity/notification investigation | pending / none | A, parallel | Caregiver Admin projection/query files outside W1; CareHomes/Chat/Identity notification producers and dedicated tests | Reproduce missing identity cases without deleting data; propose minimal flag/fallback consistent with privacy; newly generated text human-readable/localized, IDs remain routing metadata; historical notifications distinguished |
| W5 Portal fixture pack | pending / none | B, after W1/W2 contracts accepted | New bounded local fixture tooling and fixture usage guide; dedicated fixture checks | Deterministic requested states, isolated local/double-only money operations, family login supplied privately, readback and minimal scoped cleanup, clock confined to test host |
| M Integration/review | pending / none | C, after handbacks | Shared DI, error mapper, test-host composition, project files, EF snapshots/migrations, shared Postman collections, this brief and Mastermind handoff | Actual diff reviewed, zero-warning build, affected tests and relational/concurrency gates, route/OpenAPI/Postman parity, accurate final release recommendation |

Paths in the table abbreviate existing module roots under `src/Modules` and
controller root `src/API/Sanad.API/Controllers`. At kickoff Mastermind resolves
each lane's exact file manifest and records ownership before authoring. A newly
discovered shared dependency blocks only that file until ownership is assigned.
W3 excludes W1/W2 controllers, notification producers and caregiver projections.
Workers own their feature documentation; M merges shared collections serially.
No lane may create a second status ledger or edit the current release brief.

## W1 contract

- Extend `GET /api/v1/admin/bookings` with optional `status`, keeping absent-status
  behavior backward compatible. Explicitly define status + finance interaction;
  do not silently return closed rows when Confirmed is selected.
- Add family/caregiver display names without leaking extra contact/medical data.
  Missing identities have an explicit nullable representation.
- Document `POST /admin/bookings/{id}/outcome`: 204 empty success, permitted states,
  outcome reasons including admin treatment of values 1/2, reason categories 1–5,
  maximum reason length and domain-error mappings from actual source/tests.
- Test auth, filter isolation, names, ties/paging, invalid enums/combinations and
  fresh reads. Do not broaden outcome eligibility as a documentation fix.

## W2 contract and mandatory pre-authoring design

Proposed route family (confirm existing route collisions at kickoff):

- Family: retain existing duplicate-charge POST address with an explicit migration
  contract to claim submission; add own list/detail/status reads.
- Admin: `/api/v1/admin/finance/subscription-refunds/duplicate-charge` list/detail
  and claim-specific approve/reject actions; execution/reconcile routes only if
  existing mechanisms cannot safely supply them.

Before W2 implementation, Mastermind accepts its bounded design for exact DTOs,
HTTP statuses and compatibility transition, persisted review/execution states,
existing SupportAdmin/SuperAdmin policy reuse, audit attribution, provider adapter,
and migration ownership. These are required design deliverables, not permission
to change other financial policies. Do not break clients by silently swapping the
existing refund receipt for a claim receipt without a documented transition.

- Submission checks ownership, succeeded capture, duplicate claim identity and
  evidence, but a successful capture alone is not proof of duplication.
- Reviewer records verified basis and decision reason. Full eligible capture
  amount is server-derived; already refunded/claimed funds cannot be awarded again.
- Approval authorizes execution, not completion. Persist intent/attempt claim
  before external execution and record confirmed settlement only from evidence.
- Transport timeouts become unconfirmed, not safe-to-retry failures. No blind
  retry or callback-driven second payout. Approval and payout retries are idempotent.
- Existing subscription refund ledger entries remain historical records; never
  auto-submit them to Paymob or infer provider success from an approved ledger row.
- Tests: foreign-family denial, unauthorized decision denial, rejected claim has
  no payout, repeated submission/approval, concurrent decisions, budget contention
  with shutdown refund, provider decline/timeout/reconciliation, fresh status reads,
  ledger atomicity and legacy compatibility. Real-money tests are excluded.

## W3/W4 boundaries

W3 produces an itemized response-schema/limit inventory for every item-11 route
before editing annotations. Existing 404 support-contact behavior remains documented
unless a compatible empty-state change is explicitly accepted; no blanket 204 change.
Legal content is not authored or published by the worker. Verify current enum-name
parsing, public landing image path and optional DTO schema rather than repeat old fixes.

W4 investigates caregiver list versus Identity reads: distinguish authorization,
missing identity, anonymization and deactivation. Never infer orphanhood solely from
a 404. No deleting six reported rows. Any new identity-availability flag must avoid
reviving hidden names. Local fixture proof precedes a minimal correction.
Notification tests exercise new producer output and user-language selection without
raw identifier text; no rewriting historical inbox data in this scope.

## W5 fixture inventory

Provide bounded scenarios for both caregiver and CareHome refund ambiguity/failure,
PaidAwaitingDecision accept/reject, evidenced external refund, checkout without
family confirmation/dispute, completed paid caregiver booking/payout, booking
extension, current paid subscription/shutdown and new duplicate-claim review,
complaint Received/Investigating. Test doubles must be explicit, and never portray
synthetic financial state as a verified Paymob movement. Clock control is local
test-host-only. Missing live portal test data is not solved by writing to Production.

## Review, release and estimates

1. M pins post-release baseline, resolves exact manifests and dispatches A lanes.
2. M accepts W2 design and lane handbacks; fixes return to the same sessions.
3. W5 consumes accepted contracts; M executes checks sequentially on verified
   local targets. Workers never share a runtime slot.
4. Run strict zero-warning Release build, focused API/auth tests, meaningful
   PostgreSQL transaction/race tests, and full suite for cross-cutting money changes.
   Skips are explicitly accounted for. Parse Postman and verify route parity.
5. Review exact diff, migration/compatibility impacts, fixtures and consumer handoff.
   Request separate release authority; after deployment verify the actual revision,
   migration heads and bounded smoke results. No previous receipt proves this batch.

The earlier 2–3 working-day estimate applied to the smaller discovery/docs/fixture
scope. Support-approved claim execution is a substantive financial workflow; the
broader 4–7 working-day estimate is provisional until W2 design/dependencies are
reviewed. Parallelism does not remove serialized money verification and release work.

## Next action

WAIT for current-slice release verification. Do not dispatch these workers yet.
After that gate, activate this brief and start Wave A under Owner's continuation
direction. Preserve current release scope and keep new implementation out of its package.

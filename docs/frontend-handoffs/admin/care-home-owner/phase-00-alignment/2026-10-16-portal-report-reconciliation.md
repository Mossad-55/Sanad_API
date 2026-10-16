# Admin — Care Home Owner portal — Phase 00 reconciliation

Date: 2026-10-16 (Africa/Cairo). Phase: FUR-00.
Communication state: ready for owner review/forwarding; not sent by this task.
Audience: Admin / Care Home Owner portal. This is not a Family/Caregiver/Elderly mobile
audit. No frontend repository was inspected, so these are contract corrections,
not proven line-level defects in frontend code.

Source: `4b65369d760c8af6b2242ebd2022a6cb10057580`.
API deployment in the completed audit: `9167e7111c5321b063fa9d893001e77dd42a32da`.
[Full audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md)
and [follow-up plan](../../../../internal/work/portal-api-reconciliation/plan.md#frontend-follow-up-sequence--2026-10-16)
define evidence/approval limits. Issue IDs `FR-20261016-NN` map to report #NN.

## What is correct and should stay

- Owner settlements and dispute-list requests are valid gaps (#9, #11).
- `netCollected` is not owner income: it is customer collection after completed
  refunds and includes platform fee/tax. Keep owner earnings distinct (#9).
- Numeric visit days are correct, Sunday=0 through Saturday=6; Monday=1 (#33).
- The current submission rule is at least two profile photos including a cover.
  Keep that frontend check; the older no-photo answer is outdated (#42/A6).
- Preserve optimistic concurrency with the shared facility `expectedVersion`;
  handle stale-version 409 rather than retrying writes as if they succeeded.

## Corrections and confirmed limitations

| Issue | Current truth / frontend action | Backend responsibility |
| --- | --- | --- |
| FR-20261016-03 | Draft dashboard/revenue access is intentional. Old October 11 typed-ID query bug was fixed. Do not hide all Draft reads or assume a 500 is normal before approval. | FUR-03 verifies current exact queries with safe fixtures; no remaining 500 is independently reproduced. |
| FR-20261016-06 | Imported hierarchy is 27 governorates / 349 Cities / 5714 Areas including retained legacy entries. | Existing import complete; no reset. [Receipt](../../../../internal/work/portal-api-reconciliation/reports/geography-import-preserve-add.md). Refresh lookup/cache if still showing 4/3/2. |
| FR-20261016-24 | Null `draft` after freezing/submission can be correct. The response lacks the explicit current submitted/approved private profile readback needed by settings. | FUR-04 will define the additional read projection. Do not create a draft from a GET or use public discovery for private contact fields. |
| FR-20261016-26, -27 | Operational detail lacks resident name/age; dispute reads lack requested display fields. Booking already stores resident name/age snapshots. | FUR-04 reuse booking snapshots; FUR-08 owns owner dispute list scope. |
| FR-20261016-09 | No owner settlement-list endpoint exists yet. | FUR-05 reuses existing payout/refund/commission/debt truth, not frontend arithmetic. Final DTO/filters still pending. |
| FR-20261016-11 | No owner-scoped dispute list exists yet. | FUR-08, own facility only; no admin resolution permission implied. |
| FR-20261016-01, -12 | Lost verification request state is a real recovery gap; exact OTP provider exception remains unknown. | FUR-02 secure restricted-identity recovery and resend reliability. No fixed OTP or force-verification endpoint on this host. |
| FR-20261016-30, -31 | Current auth payloads use full-name fields and request IDs; some response IDs are `{value: guid}`. | FUR-01 corrects stale guides. Preserve per-operation response wrappers and plain request GUIDs. |
| FR-20261016-32, -34 | Many enums are already published, but owner inventory/booking/visit/settings/finance response metadata is still incomplete. Visit status has eight values; do not reuse another status enum. | FUR-01 publishes actual contracts; do not implement final types from proposed shapes. [Full enum audit](../../../../internal/work/portal-api-reconciliation/frontend-report-audit-2026-10-16.md#32--enum-values-and-aggregate-shape). |
| FR-20261016-33 | Numeric Monday=1 examples are already corrected. | No string-enum wire change needed. [Visit guide](../../../../care-homes/visits.md). |
| FR-20261016-38 | A pure owner audience is unsupported by app legal audience selection (403). A supported audience with no publication can get 404 instead. Check selected account and exact code. | E02 owner legal audience/content decision; no owner legal publication promised yet. |
| FR-20261016-40, FR-20261016-41 | Pure owner self-deletion and owner personal-ID workflow are not supported here; Family/caregiver and mixed-account cases differ. | Separate policy decisions, not an incidental frontend fix. Facility documents are not the same as personal Identity documents. |

## Onboarding and document lifecycle — use these rules

For #42/#44, current backend rules are:

1. Contact name is explicit editable profile data. Identity-name prefill is not a
   backend guarantee or permanent derivation rule.
2. Arabic and English admission conditions are both required at submission;
   making English optional requires a new agreement.
3. All four required document types must be present in the current revision to
   submit: operating licence, registration, health and civil defence. They do not
   all need verification before submitting.
4. Approval checks verified/valid documents in the relevant submitted revision.
   Old frozen-revision uploads do not satisfy a new revision automatically.
5. Rejecting one document does not automatically reject or reopen the whole
   application. Application correction/rejection is a separate admin decision.
6. Uploads are blocked in PendingReview/Suspended. Use the actual allowed edit
   lifecycle; do not assume every replacement can be uploaded in every state.

[Owner onboarding guide](../../../../care-homes/owner-onboarding.md) remains a
feature reference; identified guide contradictions will be reconciled in FUR-01.
Current profile limits include names/contact name 200, descriptions/admission
conditions 4000, address 1000, phone 32 and email 256. Contact-phone E.164 is not
currently a universal backend guarantee; stronger client validation should not be
represented as an existing server invariant.

## Frontend navigation/error handling

- Build visibility from route/lifecycle capability, not a global Approved-only
  switch. Existing owner read evidence includes Draft and Approved facilities.
- A 404 may mean no owned facility/resource or unpublished content. It does not
  universally mean an undeployed endpoint. Existing empty owner views return
  successful empty data; no-facility cases use route-specific 404s.
- Keep Care Home booking status distinct from caregiver/family booking status;
  neither is a generic five-state booking enum.
- Existing visit conflict/validation behavior and shared versions remain in force.
- Keep payment/refund/extension labels resource-specific. Do not calculate owner
  settlement by subtracting a guessed fee from dashboard customer collections.

## Connection to delivery and verification

| Proposed work | Contract | Implementation/test/deployment state |
| --- | --- | --- |
| FUR-01 contract truth | Pending exact bounded brief. | No new changes delivered by this report. |
| FUR-02 verification continuity | Pending security/retry contract. | No provider sends or authenticated scenarios executed in this documentation task. |
| FUR-03 finance verification | Pending safe query/fixture brief. | Historical fix and isolated owner tests exist; no fresh PostgreSQL runtime claim. |
| FUR-04 readback | Pending exact fields/revision semantics. | Missing capabilities not yet implemented. |
| FUR-05 settlements / FUR-08 disputes | Pending scoped read contracts. | Not implemented/deployed as new owner endpoints. |
| E02 owner legal | Owner decision needed. | No new audience/content deployed. |

Future delivery reports will link exact implemented guides, source revisions,
executed tests and deployment receipts. Do not wire against suggested responses
as if they were frozen contracts.

For a current failure, send method/path, timestamp/timezone, selected account,
sanitized request/response/error code, trace ID and API revision if known. No
tokens, OTPs or personal documents. Backend tests will use verified disposable
fixtures; this Production-mode developer VPS is not disposable.

**Next action:** owner reviews/forwards this report; frontend acknowledges
relevant assumptions and supplies fresh evidence only where current behavior
still differs. No new endpoint contract or release date is promised here.

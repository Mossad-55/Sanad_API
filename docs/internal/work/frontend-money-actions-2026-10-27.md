# Backend money-action matrix — frontend handoff (2026-10-27)

Accessible standalone handoff compiled from the released investigation record
(`docs/internal/work/frontend-report-2026-10-26.md`, corrected tranche 2) and
current source at revision `b302a3fc` (refund-completion release). This
document describes what each action
does, who may call it, and what the persisted demo state allowed at capture
time. **It is not an execution authorization.** Fixture eligibility facts below
were read from the rehearsal database on **2026-10-26** (revision era
`47329bf5`); they go stale after any action and MUST be re-read from the live
target immediately before any execution.

## Provider boundary (read first)

- Every booking/claim refund reaches the provider through one seam-wired call:
  `IPaymobClient.RefundPaymentAsync(txnId, amount)` under the
  `FixtureRefundSeamPaymobClient` decorator (Families DI).
- The rehearsal seam intercepts **exactly the seven configured armed id(s)**
  recorded in the rehearsal's seam configuration (ids are held in host
  configuration; deliberately not enumerated in this document). Everything
  else passes through to the real Paymob client. **The seam is not blanket
  sandbox safety.**
- Scripted outcomes (`Rejected`, `RefundOutcomeUnknown`, …) prove application
  behavior only. They do **not** prove provider sandbox or production behavior.
- `IPaymobClient` has no status/poll methods — refund finality is never polled;
  reconciliation is operator-reported evidence.

## Caregiver-booking actions (`CaregiversAdmin` policy)

| Route | Preconditions | Demo fact at capture (2026-10-26) | Expected result if run |
|---|---|---|---|
| `POST /api/v1/admin/bookings/{bookingId}/refund` (no body) | A succeeded stored transaction; refund state machine allows a new attempt | Family demo booking `01a17542-3659-7158-9c52-83bc1325f916`: Status 8 CancelledByCaregiver, `is_refund_attempt_in_flight=false`, `is_refund_outcome_ambiguous=false`, recorded award 100 minor units; seam-armed retryable slot | Seam intercepts the armed transaction id → scripted `Rejected` released as a definitive refusal; no money moves; booking returns to failed-refund; bounded retry remains possible |
| `POST /api/v1/admin/bookings/{bookingId}/outcome` | Accepted booking only | Same demo row is CancelledByCaregiver | **NOT executable** — the state guard refuses before any provider path |
| Caregiver `POST .../provider-outcome` | Accepted booking only | Same | **NOT executable** — same guard |

## Care-home actions (`CareHomesOperationalAdmin` policy)

| Route | Preconditions | Demo facts at capture (2026-10-26) | Expected result if run |
|---|---|---|---|
| `POST /api/v1/admin/care-homes/bookings/{bookingId}/refund/retry` | Persisted claim/not-claimed gate; refund state Pending or Failed | `01a17542-2fc6-75bb-a082-ccb0b4c143f0`: Status 7 RefundPending, PaymentStatus 2, RefundStatus 1 Pending, amount 4025.00 — eligible. `01a17542-308a-73a5-b5a1-d604ffa57ad8`: same but RefundStatus 3 Failed — eligible. Both carry armed numerics as the stored transaction columns | Seam intercepts → scripted `Rejected` → HTTP 502 with `Paymob.RefundRejected`; row lands RefundStatus Failed; still retryable |
| `POST .../refund/record-completed` | **None mechanized** — manual record of a *verified real-world* refund; DB-only | n/a | Not a frontend test target: only meaningful with real verified refund evidence; owner-authorized operator action |

## Duplicate-charge claim actions (admin finance; SuperAdmin for execute)

| Route | Preconditions | Demo fact at capture (2026-10-26) | Expected result if run |
|---|---|---|---|
| `POST /api/v1/admin/finance/subscription-refunds/duplicate-charge/{claimId}/execute` | Claim approved (state 3) and execution pending | Approved claim `01a17542-3bc9-7033-a39a-1233d0ffa7af` (execution_state 2); its capture id is seam-armed | Seam intercepts → scripted `Rejected`; execution failed with `Paymob.RefundRejected` (retryable, attempt-counted) |

**Reconciliation routes are intentionally excluded from this handoff.**
`POST .../duplicate-charge/{claimId}/reconcile` and both booking-level
`refund/reconcile` routes alter money records from operator-stated truth
(`refundDidMove` + evidence + verified provider reference). Booking-level
reconciles refused on every demo row anyway (no ambiguity, no in-flight
attempt); the duplicate-charge ambiguous claim
(`01a17542-3ce7-7fc6-b3dc-a322eb2a351e`) is reconcilable in principle but is
DB-only, still alters money records, and is an owner-authorized operator action
only. Fabricating a provider reference or a "verified" verdict is falsified
evidence and is never acceptable.

## Rehearsal money-inventory notes (capture 2026-10-26 / B2 recheck 2026-10-27)

- `care_homes.bookings` holds 26 rows. One is a **possible real capture**
  (provider transaction `556970084`, booking
  `01a1559c-10e3-76b1-ba13-7753b1b7de77`, amount 11,500) from an actual
  checkpoint 2026-10-19: it is **not refund-eligible through the existing
  APIs** and must not be targeted. The other care-home money fixtures are
  synthetic seam fixtures.
- Two existing real subscription captures exist on the rehearsal from the
  Card-enrollment rehearsal. They are **valid single sales, not duplicates** —
  never label one as a duplicate-charge fixture without proven duplication,
  and never submit a duplicate-charge claim against them on an unverified
  basis.
- As of the 2026-10-27 corrected check, **no pre-existing eligible
  provider-refund capture was evidenced**. The subsequent real-test
  verification therefore created its own legitimate TEST bookings through the
  real checkout flow and is now **executed and closed** (2026-10-27, release
  `b302a3fc`; both of the two permitted records used — the first remains the
  historical `Initiated` record, intentionally not rewritten, with no
  financial action required on it; the second proved the full refund path —
  see §1.5 of [frontend-landing-response-2026-10-27.md](frontend-landing-response-2026-10-27.md)
  and the execution brief's receipt). No further refund execution is approved
  by this document.

## Execution rules (constraints, not permissions)

- The eligibility column above is **information**: running an action requires
  prior Owner authorization, the exact reviewed protocol and current-state
  re-verification. This document grants nothing.
- Total scope for real-test refund verification remains **at most two test
  refund records**, reused/reduced wherever possible; serialized after any
  parallel work; source-fix freeze before any provider call; effective
  test-mode configuration checked first; a permissible testing account only;
  **no secrets, keys, or credential paths printed, logged or archived**;
  no broad unseamed refunds; no fabricated provider references.
- Payment/checkout secrets, client secrets and hosted-checkout URLs are never
  written into docs, logs, reports or chat output.

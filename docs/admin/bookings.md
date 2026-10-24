# Admin bookings (cancellations & refunds)

Ops routes under `/api/v1/admin/bookings/...`. Policy **`CaregiversAdmin`**: Normal JWT, `account_type` SuperAdmin or ContentAdmin.

This surface is for **closed** bookings: family/caregiver cancel, caregiver decline, expiry, and Paymob refund outcome. By default (no `status` filter) live upcoming visits are not listed; an explicit `status` filter can also pull open statuses (paid/awaiting, confirmed, in progress, completed) for discovery.

## Status filter (`status`)

`GET /api/v1/admin/bookings` takes an optional `status` query value (a `BookingStatus` number, e.g. `status=3` for Confirmed):

- **No `status`** — the documented finance behavior above is unchanged (closed bookings only).
- **`status` + `finance=0 All`** — returns exactly that status, open or closed.
- **`status` + `finance=1/2/3`** — allowed only when the status belongs to that finance predicate: `1` Cancelled members status 6/7/8, `2` FailedRefund members status 6/7/8/10 (the paid-and-unrefunded condition still applies to rows), `3` Refunded member status 9.
- **Any other explicit combination** — rejected with `400 Bookings.InvalidStatusFilter` (the intersection is structurally empty; an allowed-but-empty pair still returns an honest empty `200` page, e.g. FailedRefund + an unpaid cancellation).
- **Undefined numbers** (e.g. `finance=99`, `status=99`) are also rejected with `400 Bookings.InvalidStatusFilter`.

`Bookings.InvalidStatusFilter` is returned with plain validation text naming finance or status.

## Refund state

`refundState` on every list/detail item:

| Value | Meaning |
|---|---|
| `1` `NotApplicable` | Never paid (e.g. family cancelled `PendingPayment`) or not in a refundable end state |
| `2` `Failed` | Paid, then cancelled / declined / expired, and the owed refund definitively did not settle. Status stays `CancelledByFamily` / `DeclinedByCaregiver` / `CancelledByCaregiver` / `Expired`. For a revision-2 cancellation this means the recorded award is still owed (nothing, or only part of it, has been refunded); for a revision-1 cancellation it means no refund was recorded |
| `3` `Succeeded` | Status `Refunded (9)` and `refundedOnUtc` set, **or** the full amount the cancellation fact records as owed has been settled as refunded |
| `4` `NoRefundDue` | A cancellation fact records that no refund is owed by policy. This state is not a failed refund and is never retryable. |
| `5` `Pending` | An attempt was made but its outcome is unconfirmed, **or** an attempt was claimed and is still in flight because the process died before recording its result. The money may or may not have moved, so this is neither success nor failure, and no amount is booked against the capture. Never retryable: reconcile first (see below) |

**Failed is derived from settled money, not from the entitlement.** A revision-2 cancellation that owes a 50% award reads `Failed` until that award is recorded as refunded, even though the policy decision itself is complete. A definitive provider refusal therefore stays in the ops queue (retryable); an unconfirmed attempt reads `Pending` instead.

Automatic Paymob refund can fail silently; **Failed** is the ops queue. A policy-denied refund is **NoRefundDue**, not **Failed**, and must not be retried. Admin retries with `POST /api/v1/admin/bookings/{id}/refund` only for **Failed** items (Paymob dashboard remains a fallback if the gateway still rejects). A **Pending** item is not retryable: `POST .../refund` returns `409 Bookings.RefundOutcomeAmbiguous` and the attempt must first be settled with `POST /api/v1/admin/bookings/{id}/refund/reconcile`.

## List

```http
GET /api/v1/admin/bookings?page=1&pageSize=10&finance=0
Authorization: Bearer {{accessToken}}
```

| Query | Default | Meaning |
|---|---|---|
| `page` / `pageSize` | `1` / `10` (max 100) | Paging |
| `finance` | `0` All | Filter |

`finance`:

| Value | Included |
|---|---|
| `0` All | Cancelled by family/caregiver, declined, expired, refunded |
| `1` Cancelled | `CancelledByFamily (6)`, `DeclinedByCaregiver (7)`, `CancelledByCaregiver (8)` — includes unpaid, **failed**, and **NoRefundDue** cancellations (successful refunds have status `9` and drop out of this filter) |
| `2` FailedRefund | `paidOnUtc` set + `refundedOnUtc` null + status 6/7/8/10. This is a coarse money predicate, not a reading of `refundState`: it also lists **NoRefundDue** cancellations (retry returns `409 Bookings.NoRefundDue`) and **Pending** (uncertain) attempts (retry returns `409 Bookings.RefundOutcomeAmbiguous`). Read each row's `refundState` before retrying |
| `3` Refunded | Status `Refunded (9)` |

Empty database: `200` with `totalCount: 0`.

List item fields: booking/family/caregiver/elderly ids, slot, `status`, amounts, `refundState`, `paidOnUtc`, `cancelledOnUtc`, `refundedOnUtc`, `cancellationReason`, plus display names: `familyName` and `caregiverArabicFullName` / `caregiverEnglishFullName`. Names are best-effort and nullable: a deleted family renders `null` (never the anonymized "Deleted family" sentinel), and an unknown or anonymized caregiver identity renders `null`s. Ordering is newest first (`cancelledOnUtc` ?? `refundedOnUtc` ?? `updatedOnUtc`, then a unique id tiebreaker so pages are deterministic when timestamps tie). Names resolve in fixed batched reads per page — never per row.

## Detail

```http
GET /api/v1/admin/bookings/{bookingId}
```

Same `BookingDetailResponse` as family/caregiver detail, including `refundedOnUtc` and `refundState`. Fact-backed cancellation details render `NoRefundDue` instead of the legacy failed-refund derivation, and an unconfirmed or still-in-flight attempt renders `Pending (5)`. `404 Bookings.NotFound` when unknown.

`BookingDetailResponse` also carries five additive members that are `null` on these Admin and family reads and are populated only by the caregiver booking-detail read: `cancellationActorSide` (`1` Family, `2` Caregiver), `cancellationAction` (`1` Cancel, `2` Reject), `cancellationReasonCategory` (the recorded category name as a string, matching the cancellation-history item's `reasonCategory`), `refundEntitlement` (`1` NoRefundDue, `2` FullCapturedRefund) and `refundDecisionReason` (`1` NothingCaptured, `2` FamilyCancellationBeforeAcceptance, `3` CaregiverRejectionBeforeAcceptance, `4` CaregiverCancellationAfterAcceptance, `5` FamilyCancellationWithinGraceWindow, `6` FamilyCancellationOutsideGraceWindow). See [caregiver bookings](../app/caregivers/bookings.md#detail) for when the caregiver read fills them in. No other field, amount, eligibility rule or access boundary changes, and no ledger or event timeline is added.

## Cancellation history

```http
GET /api/v1/admin/bookings/cancellations?page=1&pageSize=10&actor=1
Authorization: Bearer {{accessToken}}
```

Returns durable cancellation facts, newest first, with optional `actor` filtering (`1` Family, `2` Caregiver). The response preserves actor, action, status-at-cancellation, reason, refund entitlement, and current booking state.

This is a safe read for Super Admin or Content Admin normal JWTs. It returns
`200` with an empty page when there are no facts; `401` is unauthenticated and
`403` is any non-admin or restricted token.

## Retry refund

```http
POST /api/v1/admin/bookings/{bookingId}/refund
Authorization: Bearer {{accessToken}}
```

No body. Allowed only when `refundState` is **Failed** (paid + cancelled/declined/expired + an owed refund that definitively did not settle). A **Pending** item is refused: reconcile it first.

The retry sends the **outstanding award**, never the captured amount. When a revision-2 cancellation fact records an award, the amount sent is that award less what has already been refunded, capped by the captured balance; a revision-1 fact keeps the legacy full-captured behaviour. An attempt is committed as an in-flight claim before the Paymob call, so a crash either side of the call can never cause a silent second send.

| Result | HTTP | Meaning |
|---|---|---|
| Success | `200` | Same detail payload; `status` `Refunded (9)`, `refundState` `3` |
| `Bookings.NotFound` | 404 | Unknown id |
| `Bookings.AlreadyRefunded` | 409 | Already `Refunded`, or the recorded award has already been refunded in full |
| `Bookings.RefundOutcomeAmbiguous` | 409 | The last attempt is unconfirmed or still in flight; reconcile before retrying |
| `Bookings.RefundNotEligible` | 409 | Never paid, still an open booking, or a revision-2 fact whose award was not recorded (fails closed rather than guessing an amount) |
| `Bookings.RefundExceedsCeiling` | 409 | Nothing is left to refund within the captured balance |
| `Bookings.Cancel.AlreadyProcessed` | 409 | Another request committed the refund claim for this booking first; the gateway was not called |
| `Bookings.NoRefundDue` | 409 | Policy recorded that no refund is owed; no Paymob call is made |
| `Paymob.NotConfigured` | 503 | Gateway keys missing |
| `Paymob.GatewayError` | 502 | Paymob rejected the refund; booking stays failed — retry again or use the dashboard |

## Record a provider outcome

```http
POST /api/v1/admin/bookings/{bookingId}/outcome
Authorization: Bearer {{accessToken}}
Content-Type: application/json

{ "outcomeReason": 1, "reason": "Provider reported a shift cancellation", "reasonCategory": 1 }
```

Records a platform-asserted outcome on an **accepted** (`Confirmed`) booking. The refund is decided
by the booking's sold policy revision and settles through the same executor as the other refund
paths; the acting admin id is stored on the durable cancellation fact.

Request body:

| Field | Type | Meaning |
|---|---|---|
| `outcomeReason` | int | Required, 1–6 (below) |
| `reason` | string? | Required note, ≤ 500 characters after trim |
| `reasonCategory` | int? | Required category: `1` Emergency, `2` MedicalIssues, `3` TransportationIssues, `4` AccountDeletion, `5` Other |
| `providerLatenessMinutes` | int? | Optional context for reason `3`; the forced decision does not re-read it |
| `customerConsentedToLateness` | bool | Optional context, default `false` |
| `forceMajeure` | bool | Optional context, default `false` |

Outcome reasons — an admin may assert any of them (the caregiver endpoint is limited to 1–2):

| Value | Reason | Refund |
|---|---|---|
| `1` | ProviderCancelled | Full award, settled to Paymob |
| `2` | ProviderNoShow | Full award, settled to Paymob |
| `3` | ProviderLateBeyondThreshold | Full award (a forced finding; lateness facts are context, not re-derived) |
| `4` | RecipientInaccessible | **NoRefundDue** — money retained, no Paymob call |
| `5` | SafetyCancellation | Full award |
| `6` | ForceMajeure | Full award |

| Result | HTTP | Meaning |
|---|---|---|
| Success | `204` | Recorded; a full award settles and the booking reads `Refunded (9)` |
| `Bookings.Outcome.InvalidReason` | 400 | `outcomeReason` is not 1–6 |
| `Bookings.Cancel.ReasonCategoryRequired` / `Bookings.Cancel.ReasonCategoryInvalid` | 400 | Category missing or not 1–5 |
| `Bookings.Cancel.ReasonRequired` | 400 | `reason` is blank |
| `Bookings.Domain.InvalidOperation` | 409 | Booking not `Confirmed`, or note longer than 500 characters |
| `Bookings.NotFound` | 404 | Unknown id |
| `Bookings.RefundSettlementIncomplete` / `Paymob.NotConfigured` | 503 | Award recorded but the movement could not complete / gateway not configured |

**Known gap (documented as-is).** Two gateway answers are not yet in the shared error mapper: a
definitive `Paymob.RefundRejected` refusal (the claim is released and nothing ambiguous is marked,
so a bounded retry stays possible) and `Paymob.RefundOutcomeUnknown` (the booking is marked
**Pending** and must be reconciled before any further refund). Both currently surface as plain
`400` through the mapper fallback instead of their coded statuses. A shared-mapper correction is a
parent-owned decision; until then, drive both states by `refundState`, not by the status code alone.

## Reconcile an uncertain refund

```http
POST /api/v1/admin/bookings/{bookingId}/refund/reconcile
Authorization: Bearer {{accessToken}}
Content-Type: application/json

{ "expectedAttemptClaimId": "<refundAttemptClaimId from the admin list>", "evidence": "Paymob dashboard: refund 8812 issued at 14:02 UTC", "refundDidMove": true, "verifiedProviderReference": "8812" }
```

An attempt whose provider outcome was never confirmed — and a claim left in flight by a process that
died before it recorded its result — leaves the booking **Pending** and refuses every refund path on
it. This command is the only way to settle that state; the operator records what they actually
found, and that finding, not a guess, books the money.

`expectedAttemptClaimId` must equal the booking's current `refundAttemptClaimId` (returned by the
admin list). It binds the decision to the attempt the operator inspected: a stale action prepared for
an earlier attempt, or a request aimed at a superseded one, is refused and must be refreshed.

`refundDidMove: true` records the award the interrupted attempt was moving against the captured
balance, so the booking reads **Succeeded** when that was the full owed award. It requires
`verifiedProviderReference` — the provider's own refund reference — because an operator note alone
does not prove money left the gateway. `refundDidMove: false` books nothing and leaves the capture
intact, so the outstanding award can be retried with `POST .../refund`; a still-in-flight claim
cannot be cleared this way, because the provider call may still be moving money.

`evidence` is required and is stored with the reconciling actor id. Evidence plus the provider
reference must fit the 300-character reconciliation record.

**Known failure window.** If the process dies after the claim commits but before any provider call
is made, there is no provider refund reference to verify and the claim cannot be cleared as
non-payment: clearing it could authorize a second refund while the original call is still alive.
Such a booking stays Pending and must be escalated to platform engineering; do not reset the claim
in the database, because that bypasses the refund ceiling and can double-pay.

| Result | HTTP | Meaning |
|---|---|---|
| Success | `204` | Attempt reconciled; no body |
| `Bookings.RefundReconciliation.EvidenceRequired` | 400 | `evidence` is empty |
| `Bookings.RefundReconciliation.EvidenceTooLong` | 400 | Evidence plus reference exceeds the stored record |
| `Bookings.RefundReconciliation.ProviderReferenceRequired` | 400 | `refundDidMove: true` without a provider reference |
| `Bookings.NotFound` | 404 | Unknown id |
| `Bookings.RefundReconciliation.NoAmbiguousAttempt` | 409 | Nothing is unconfirmed or in flight for this booking |
| `Bookings.RefundReconciliation.AttemptMismatch` | 409 | `expectedAttemptClaimId` is not the current attempt |
| `Bookings.RefundReconciliation.ClaimInFlight` | 409 | `refundDidMove: false` while the claim is still in flight |
| `Bookings.RefundReconciliation.ConcurrencyConflict` | 409 | The booking changed while the reconciliation was being saved; re-read it and retry |
| `Bookings.RefundReconciliation.AwardUnknown` | 409 | `refundDidMove: true` but the attempt recorded no award to book |

Postman: `docs/postman/admins/Sanad.Admin.postman_collection.json`.

# Mobile integration answers — 2026-10-26

Answers to the four mobile/backend integration items, from the
`mobile-integration` slice (base `b22d9fa0`, released revision recorded in the
release receipt). Authoritative detail:
[mobile-integration-2026-10-26.md](mobile-integration-2026-10-26.md).

## 1. Sent invitations — new endpoint

`GET /api/v1/family/invitations/sent` is live. Authenticated (`FamilyAccess`,
family account), scoped to the caller's family, and returns invitations of **all
statuses** *sent by the family*. Each item has exactly 7 fields:

| Field |
|---|
| `id` |
| `invitedEmail` |
| `role` |
| `relationshipType` |
| `status` |
| `createdOnUtc` |
| `expiresOnUtc` |

Cancelling a pending invitation continues to use the existing
`DELETE /api/v1/family/invitations/{invitationId}` (owner-only) with the `id`
from the sent list — no second cancel endpoint was added. A caller from another
family gets `Families.Invitation.AccessDenied`.

## 2. Approved plan benefits — and a live data drift that was corrected

Approved set from source (`SubscriptionPlan.cs`, pinned by
`SubscriptionPlanValueObject`/`SubscriptionValueObject` tests):

- **`Library` (key 2) is included on every plan** — Free, Premium and
  PremiumPlus.
- **`PremiumContent` (key 8) is PremiumPlus-only** — Premium does not include
  it. Premium library/locked content is gated on `PremiumContent` alone, so a
  Premium subscription correctly computes `canAccess: false` and PremiumPlus
  computes `canAccess: true`; **PremiumPlus is required to read premium
  content**.

**Live drift found and corrected (rehearsal DB, 2026-10-26):** the persisted
premium rows did not match this definition — premium plan versions marked
`Library` (`is_included = false` on 6 of 7 versions) and one premium version
granted `PremiumContent = true`; premium subscription benefit snapshots had
copied the same drift. After the Owner's explicit authorization the drift was
corrected in place with guarded, idempotent UPDATEs (benefit keys 2 → true and
8 → false on `plan_key = 'premium'` rows only; no insert/delete, no other keys,
`gold` untouched — see the brief for executed counts, timestamps and
idempotency proof). No source change was needed: the code and the approved
definition already agree.

## 3. Plan change during a paid period — blocked, documented, no payment

- A plan change (upgrade/downgrade/re-purchase) attempted while the current
  paid period is active (`price > 0` and now < `currentPeriodEndsOnUtc`) is
  refused with **`409 Subscriptions.PlanChange.PaidPeriodActive`**.
- A duplicate/second-device retry while a plan-change payment attempt is
  already pending is refused with
  **`409 Subscriptions.PlanChange.PaymentPending`**.
- Both refusals happen **before** any provider call and before any attempt
  write — **no payment row is created** (proven by unit tests including two
  concurrent requests and a two-device retry).
- Turning off auto-renew does **not** end the paid period and does **not** lift
  the block: the request still fails (with the same paid-period 409, or
  `Subscriptions.PlanChange.CancelRenewalRequested` once cancellation has been
  requested).
- A deferred downgrade (`PUT /pending-downgrade`) applies only at the period
  boundary and creates no payment — by design it stays allowed.

## 4. CareHome-source care records — already implemented, documented

The care-home dashboard create contract exists in source and was documented,
not reimplemented:

- Create: `POST /api/v1/care-homes/residents/mine/{residentId}/care-records`
  (`CareHomeOwnerAccess` policy). Source and attribution are server-derived
  (`CareRecordSource.CareHome`) — the client cannot set them. Request/Response
  examples: [docs/care-homes/care-records.md](../../care-homes/care-records.md).
- Family read: `GET /api/v1/family/dependents/{dependentId}/care-records`
  returns all three sources (family, caregiver, CareHome) when no `source`
  filter is sent.
- Elderly read: `GET /api/v1/elderly/care-records` — same combination.

Postman collections for the owner, family-operations and elderly surfaces carry
the create + both reads.

## Limitations

- The DB correction ran on the rehearsal target only (as authorized); any other
  environment with the same seed path would need its own correction.
- The deferred-downgrade boundary behavior and live two-device HTTP flows are
  unit-proven, not live-proven (no payment-flow execution happened; no provider
  call was made).
- `gold` plan versions carry no source-defined package; they were left
  untouched (recorded as an open observation in the brief).

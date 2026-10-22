# Finance platform charge rules

This is the shared Admin configuration surface for the percentage platform fee
and tax used by caregiver quotes/bookings and Family subscription quotes and
payments. Both percentages are calculated independently on the transaction's
base amount (or discounted subscription taxable amount); tax is not calculated
on the platform fee. There is no default when an effective rule is absent.

## Access and routes

These routes require a normal authenticated JWT and the
`FinanceOperationalAdmin` policy: **SuperAdmin or SupportAdmin**. ContentAdmin,
Family, Caregiver, and anonymous callers are denied.

```text
POST /api/v1/admin/finance/platform-charge-rules
GET  /api/v1/admin/finance/platform-charge-rules/current
GET  /api/v1/admin/finance/platform-charge-rules/history
```

Create a rule with:

```json
{
  "platformFeeRatePercentage": 15.00,
  "taxRatePercentage": 14.00,
  "version": 1,
  "effectiveOnUtc": "2026-10-01T00:00:00Z"
}
```

`version` must be positive and greater than the latest stored version;
percentages are inclusive from `0` through `100`, rounded to two decimals;
and `effectiveOnUtc` must be a UTC timestamp. The create route returns `201`
with the new rule UUID. Invalid input returns `400 Finance.Charges.Invalid`;
duplicate/out-of-order versions return a conflict; concurrent version/active
changes return `409 Finance.Charges.Conflict`.

The current route returns the active effective rule, or `204` with no body when no
effective rule exists. History returns all stored rules ordered by descending
version. Rule responses contain `id`, `platformFeeRatePercentage`,
`taxRatePercentage`, `version`, `effectiveOnUtc`, `createdOnUtc`, and
`isActive`. A future `effectiveOnUtc` is retained for scheduling, but current
consumers do not use it until it is effective.

## Consumers and immutable snapshots

- `GET /api/v1/caregivers/{caregiverId}/quote` returns the base caregiver fee,
  independent platform-fee and tax rates/amounts, `platformChargeRuleVersion`,
  total, and currency. An absent effective rule makes the quote unavailable;
  it is never silently treated as zero.
- Family caregiver checkout resolves the same effective rule server-side and
  stores base, fee rate/amount, tax rate/amount, rule version, total, and
  currency in the booking price snapshot. Later rule changes do not reprice the
  booking. The booking list/detail contract currently exposes the base fee,
  platform fee, total, and currency; the stored tax fields are not currently
  returned by the booking detail response.
- Family subscription quote/payment, renewal, plan-change, and invoice flows
  use the shared rule. Subscription payment attempts, current-period renewal
  state, and invoices preserve the base/tax/fee/rule-version/total snapshot;
  historical rows are not rewritten when a new rule is configured.
- Paymob recurring callbacks continue to use the existing anonymous HMAC
  webhook contract and the `sub_` subscription reference. This feature does
  not authorize provider calls or invent new callback payloads.

When no shared rule is effective, subscription quote returns `409
Subscriptions.Quote.TaxNotConfigured`; caregiver quote and caregiver checkout
return their existing quote/charges-not-configured errors. Clients cannot
override server-calculated amounts or rates.

## Caregiver payout policy (timing only, no payout fee)

Separate Admin-managed policy controlling **when** a completed and paid
caregiver booking becomes payout-eligible. It configures timing only:
`payoutDelayHours` (integer, zero or more) after a booking is both Completed
(`CompletedOnUtc`) and Paid (`PaidOnUtc`). Eligibility is `max(completedOnUtc,
paidOnUtc) + payoutDelayHours`; a booking missing either timestamp is never
eligible. There is deliberately **no payout fee field**: payout entitlement is
always the booking snapshot `BaseCaregiverFee` in full. The snapshot
`PlatformFeeAmount` is charged to the family on top of the base fee and is
never deducted from the caregiver payout.

These routes require the `PayoutOperationalAdmin` policy: **SuperAdmin or
FinanceAdmin** (normal access). SupportAdmin and ContentAdmin are denied. This
is intentionally separate from the `FinanceOperationalAdmin`
(SuperAdmin/SupportAdmin) platform-charge routes above, which are unchanged.

```text
POST /api/v1/admin/finance/caregiver-payout-policies
GET  /api/v1/admin/finance/caregiver-payout-policies/current
GET  /api/v1/admin/finance/caregiver-payout-policies/history
```

Create with:

```json
{
  "payoutDelayHours": 72,
  "version": 1,
  "effectiveOnUtc": "2026-10-08T00:00:00Z",
  "descriptionArabic": "يُدفع بعد 72 ساعة من إتمام الحجز وسداده",
  "descriptionEnglish": "Payout is released 72 hours after the booking is completed and paid."
}
```

`version` must be positive and greater than the latest stored payout policy
version; `payoutDelayHours` must be zero or more; `effectiveOnUtc` must be a
UTC timestamp. `descriptionArabic`/`descriptionEnglish` are optional
bilingual terms (trimmed, maximum 4000 characters each, blank normalized to
`null`); they are the admin-authored text caregivers read back from
`GET /api/v1/caregiver/payment-policy`, and policies created before
descriptions existed surface them as `null`. The create route returns `201`
with the new policy UUID. Invalid input returns
`400 Finance.PayoutPolicy.Invalid`; out-of-order versions return
`409 Finance.PayoutPolicy.VersionConflict`; concurrent version/active
changes return `409 Finance.PayoutPolicy.Conflict`.

The current route returns the effective policy, or JSON `null` when no
effective policy exists — payout processing must fail closed in that case and
never assume a default delay. History returns all stored policies ordered by
descending version. A future `effectiveOnUtc` is retained for scheduling and
becomes effective automatically once reached; no transition job or stored
flag flip runs.

Active status is computed, not stored: at any moment the effective policy is
the policy with the latest `effectiveOnUtc` that has arrived; equal effective
times resolve to the highest version. History marks exactly that policy
active, so current and history always agree. Version numbers must keep
increasing when policies are created, while effective-time precedence allows
a later-scheduled policy to take over when its scheduled time arrives. The
`isActive` response field reports this computed status.

Table `finance.caregiver_payout_policies` (`id`, `payout_delay_hours`,
`version` unique, `effective_on_utc`, `created_on_utc`). Migration `AddCaregiverPayoutPolicy` is
authored only and must not be applied except on an explicitly authorized
database. No seed policy is committed; the first effective policy comes only
from the Admin create route.

## Legacy subscription-tax adapter

The existing routes remain available for compatibility:

```text
POST /api/v1/admin/subscriptions/tax-rules
GET  /api/v1/admin/subscriptions/tax-rules/current
GET  /api/v1/admin/subscriptions/tax-rules/history
```

They retain their unchanged `SubscriptionPlanAdmin` policy: **SuperAdmin only**
(normal access); SupportAdmin and ContentAdmin remain denied. The create adapter
requires shared Finance configuration and changes the shared rule's tax rate
while carrying forward its effective platform-fee rate. Use the Finance routes
for new configuration. Existing subscription-tax records and charge/invoice
records are preserved; this adapter does not retroactively rewrite snapshots.

## Reviewed complaints and subscription refunds

These routes require the `RefundWorkflowAdmin` policy: **SuperAdmin or
SupportAdmin** (normal access). FinanceAdmin is deliberately excluded, and no
app audience gains access. The two roles are kept apart inside the application
seam: SupportAdmin receives, investigates and reads; **only SuperAdmin** approves
or refuses a reviewed financial award. The verdict routes additionally carry the
`SuperAdminOnly` policy, and the handlers re-check the actor's account type, so
the split cannot be bypassed by reaching a handler directly.

### Reviewed quality complaints (clause 5)

A quality complaint is a **reviewed outcome**, never an automatic full refund.
The complaint case records the investigation and its verdict; the money movement
an approved verdict produces is appended to the case's append-only ledger under
the same `refundCaseId`, so the review and its money movement are joined by a
plain Guid correlation key with no cross-context relationship.

```text
POST /api/v1/admin/finance/complaint-reviews
GET  /api/v1/admin/finance/complaint-reviews?state=&familyId=
GET  /api/v1/admin/finance/complaint-reviews/{complaintReviewId}
GET  /api/v1/admin/finance/complaint-reviews/by-refund-case/{refundCaseId}
POST /api/v1/admin/finance/complaint-reviews/{complaintReviewId}/investigate
POST /api/v1/admin/finance/complaint-reviews/{complaintReviewId}/approve
POST /api/v1/admin/finance/complaint-reviews/{complaintReviewId}/refuse
```

Open a case with:

```json
{
  "refundCaseId": "0198e2c1-3333-7777-8888-000000000001",
  "familyId": "0198e2c1-1111-7777-8888-000000000009",
  "reportingParty": 1,
  "reportedOnUtc": "2026-10-05T09:00:00Z",
  "visitedOnUtc": "2026-10-04T08:00:00Z",
  "isSafetyConcern": false,
  "description": "Caregiver arrived two hours late."
}
```

The reporter is the authenticated administrator. `reportingParty` is `1` Family,
`2` Caregiver, `3` CareHomeFacility, or `4` Staff. The reporting party, reporter,
and timestamps are mandatory evidence. `timeliness` is `1` WithinWindow when
reported within 48 hours of `visitedOnUtc`, `2` Late when reported later (still
**accepted**, flagged for late review, with no automatic compensation promise),
or `3` Safety when `isSafetyConcern` is true (never gated by the window).
`reviewState` is `1` Received, `2` Investigating, `3` Resolved, or `4` Refused.

`investigate` moves a Received case to Investigating, recording the investigator
and time; only an Investigating case can be `approve`d or `refuse`d. Only a
SuperAdmin may `approve` or `refuse` — a SupportAdmin (or FinanceAdmin) token is
refused with `403 Finance.Complaint.SuperAdminRequired`. Approval takes an
`awardMinorUnits` (a `long`, at least `0`; zero is valid and recorded as
computed) and an optional `evidenceReference`:

```json
{
  "approvedOnUtc": "2026-10-06T10:00:00Z",
  "awardMinorUnits": 15000,
  "decisionNote": "Reviewed and approved for a partial award.",
  "evidenceReference": "support-case-000123"
}
```

Approving records the SuperAdmin verdict **and** appends the resulting award as a
single Approved **movement** — a pure movement record, never a stored balance —
through the frozen `RefundLedger`, under the case's `refundCaseId` and in the same
unit of work as the verdict, so approval is never itself completion. It starts
the seven-business-day processing clock at `approvedOnUtc`. Refusal moves no
money and appends no ledger entry. A negative award is `400
Finance.Complaint.AmountInvalid`. The wider decision-#4 ceiling check across a
family's other cases is a recorded cross-lane follow-up and is **not** enforced
here. One review exists per refund case: opening a second returns `409
Finance.Complaint.Conflict`. Other errors are `400 Finance.Complaint.Invalid` (a
bad transition or invalid input) and `404 Finance.Complaint.NotFound`.

### Subscription service-shutdown refunds

An Ahlena service shutdown is a platform decision, not a family self-service
action:

```text
POST /api/v1/admin/finance/subscription-refunds/service-shutdown
```

```json
{
  "subscriptionId": "0198e2c1-1111-7777-8888-000000000001",
  "shutdownOnUtc": "2026-10-15T00:00:00Z",
  "idempotencyKey": "shutdown-0198e2c1-1111-7777-8888-000000000001",
  "evidenceReference": "Ahlena shutdown notice 2026-10-15"
}
```

The response is the recorded outcome
`{ refundCaseId, awardedMinorUnits, currency, outcome, isReplay }`. `outcome` is
`3` ServiceShutdown. The prorated award is the unused portion of the current paid
period (the time remaining from `shutdownOnUtc` to the period end, as a ratio of
the whole period, applied to the **consideration settled for that period**). That
base is the subscription's own current-period charge snapshot, which the initial
capture and every renewal maintain and which a paid plan change updates to the
target period's gross with the prorated credit already folded in. Renewals are
therefore not summed all-time, the plan's list price is never used, and a paid
plan change is not added on top of the period it was credited against. The caller
supplies no money figures. The award is banker's-rounded (ToEven) and floored at
zero. A shutdown at or after the period end may legitimately compute exactly zero
and the zero award is recorded as computed. A non-UTC `shutdownOnUtc` is
`400 Subscriptions.Refund.MalformedInput`; a non-current subscription is
`409 Subscriptions.Refund.NotCurrent`; a missing subscription or a subscription
with no settled period consideration is `404 Subscriptions.Refund.NothingToRefund`.
Passing an `idempotencyKey` (≤200 characters) makes a retry replay the same
recorded award with `isReplay: true`; the same key with different content is
`409 Subscriptions.Refund.IdempotencyConflict`.

## Refundable budget per capture

Decision #4 ("never refund more than captured funds less previous refunds") is
enforced cumulatively across the clause-9 cases, and decision #6 scopes the
shutdown award to the unused portion of *that* paid period. Finance therefore
keeps one budget row per **capture** (`finance.subscription_refund_budgets`,
unique on `payment_attempt_id`), and every funded subscription refund — a
duplicate charge (anchored on the attempt it corrects) and a service shutdown
(anchored on the attempt that paid for the current period) — claims against that
row. The same captured money therefore cannot be refunded twice, in either order,
under different refund cases or different idempotency keys; per-key uniqueness
alone would not serialize those.

The scope is the capture, not the subscription, because the ceiling is what that
period paid. A subscription-scoped budget seeded with lifetime captures would let
money already consumed in an older period supply headroom for the current one —
refunding a current-period duplicate charge and then half of the same current
period would fit under a lifetime total while refunding the current period twice.
`subscription_id` remains as an indexed audit column so all of a subscription's
budgets stay reachable for reconciliation without widening any one ceiling.

Accounting is reservation-based. An approval is claimed once, as in-flight
exposure; a completion (`TransferConfirmed` / `CreditBalancePosted`) is the same
money finally moving, so it is deliberately not claimed again — summing approved
and completed ledger entries would count a single award twice. The cumulative
claimed total and the captured capacity are written in the same unit of work as
the ledger movement, on a single row whose `version` column is the optimistic
concurrency token: two concurrent claims cannot both commit, and the loser gets
`409 Finance.SubscriptionRefund.Conflict` (surfaced to the caller as
`Subscriptions.Refund.FinanceRejected`) and retries. The unique index on
`payment_attempt_id` covers the first-claim race. The capacity itself is a
Families fact (what that attempt/period actually paid) and only ever grows; the
claimed total is Finance's own and is never supplied by a caller, so no
caller-trusted prior-refund offset exists.

An award that would exceed the capture's remaining refundable funds is refused
with `409 Subscriptions.Refund.ExceedsCaptured` rather than silently trimmed,
because a trimmed duplicate charge would no longer be a full refund. Zero-piastre
awards — including cancel-renewal — consume no capacity and create no budget row.

Not covered by that budget: a quality-complaint award. It is scoped to its own
complaint case through the complaint-review workflow, a case accepts at most one
funded award (a second approval is `409 Finance.Complaint.Conflict`), and whether
complaint compensation outside a specific capture should be capped remains an
Owner decision.

The family-facing duplicate-charge refund
(`POST /api/v1/family/subscriptions/refunds/duplicate-charge`) and the optional
cancel-renewal idempotency key are documented in
[family subscriptions](../app/families/subscriptions.md#subscription-cancellation-refunds-and-shutdown).

## Migration and startup safety

The Finance schema is introduced by `20261002210606_AddPlatformChargeRulesEfMetadata`.
The related Family snapshot migration is:

```text
20261002210659_AddPlatformChargeSnapshotMetadata
```

The reviewed-complaint and subscription-refund tables were added by the additive
Finance migration `20261022230000_AddComplaintReviewsAndSubscriptionRefundRequests`
(tables `finance.complaint_reviews`, `finance.subscription_refund_requests` with
its unique `(family_id, idempotency_key)` index and its `subscription_id`
correlation column, and `finance.subscription_refund_budgets` with its unique
`payment_attempt_id` index and non-unique `subscription_id` audit index). It is a
files-only slot authored after `20261022210000_CreateFinanceMoneyCore` and is
**not** applied by this change.

In Development, Finance migration startup remains opt-in through
`FinanceMigrations:ApplyOnStartup` (default `false`) so local host tests can
avoid database lifecycle work. In every non-Development environment, Finance
migrations run automatically with the other registered DbContexts before API
startup completes. Production cannot disable the shared startup migration
policy; ensure the configured database role has migration permissions and
review the deployment manifest's migration identifiers before rollout.

The current implementation was authorized for disposable local migration and
test work only. Provider-backed payment success and production deployment are
separate owner-controlled actions.

## OpenAPI contract

The current-rule read publishes its `200` schema; before the first rule
exists it returns `204` with no body (verified through HTTP: the null query
result serializes to no content, not JSON `null`). The history read publishes
its `200` bare-array schema. Shared conventions are in
[api-contract-conventions](api-contract-conventions.md).

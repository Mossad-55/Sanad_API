# Caregiver payout account

The authenticated caregiver manages their own payout account for future manual bank transfers. All routes require `CaregiverAccess`. The API resolves the caregiver from the authenticated identity; the caller cannot read or change another caregiver's account. A caregiver with no profile receives `401`; requests for another caregiver are impossible by construction.

- `GET /api/v1/caregiver/payout-account` → `200`
- `PUT /api/v1/caregiver/payout-account` → `200` (create or resubmit)

## Statuses

`Pending` means the details were saved and are **awaiting manual review**. It does **not** mean bank ownership is verified: no automatic bank verification exists. A future Admin review will move the account to `Verified` or `Rejected`. Saving new details always returns the account to `Pending`, including editing a previously reviewed account (re-verification is required).

## Update body

```json
{
  "accountHolderName": "Mohamed Ahmed",
  "bankCode": "NBE",
  "iban": "EG380019000500000000263180002"
}
```

- `accountHolderName`: required, trimmed, maximum 100 characters.
- `bankCode`: required; uppercased; must match an **active** bank from `GET /api/v1/lookups/banks`. Unknown code → `400 Caregivers.PayoutAccount.UnknownBank`; inactive bank → `409 Caregivers.PayoutAccount.InactiveBank`.
- `iban`: required; spaces/dashes stripped, uppercased. It must be a registered IBAN format: known country code, exact country length, valid BBAN structure, and a passing mod-97 checksum, validated against the SWIFT IBAN Registry snapshot embedded in IbanNet 6.2.0 (see below). Invalid → `400 Caregivers.PayoutAccount.InvalidIban`.

## Supported IBAN formats

All countries registered in the SWIFT IBAN Registry snapshot embedded in **IbanNet 6.2.0** are accepted — there is no separate country allowlist, so formats stay consistent with the generic bank catalogue by construction. Examples: `EG` (29 chars), `SA` (24), `AE` (23), `GB` (22), `DE` (22). A checksum-valid value with an unregistered country code, wrong country length, or wrong BBAN structure is rejected.

Library update procedure: bump the pinned `IbanNet` version in central package management, rerun the IBAN vectors (`IbanValidationTests`), and record the new embedded registry snapshot (IbanNet release notes) here. Validation never fetches registry data at request time.

## Encryption at rest and key configuration

The full IBAN is stored only as authenticated AES-256-GCM ciphertext in `iban_ciphertext`, plus the last four characters in `iban_last4` for masked display. Keys are purpose-scoped to caregiver payout IBANs and come only from host configuration (environment), never from the database, repository, or code:

```text
Caregivers__PayoutIbanProtection__CurrentKeyId=k1
Caregivers__PayoutIbanProtection__Keys__k1=<base64 256-bit key>
```

Generate each key from 32 random bytes (base64). Missing or invalid key configuration fails closed: payout-account writes return `503 Caregivers.PayoutAccount.ProtectionUnavailable` and nothing is stored. Absent configuration does not stop API startup so unrelated features keep running.

Key rotation: add the new key under a new id, keep the old key under its existing id, then set it as `CurrentKeyId` (restart or reload configuration). Stored envelopes carry their key id, so existing ciphertext keeps decrypting while new writes use the current key. Never delete a key id that still protects stored rows.

Database/storage-level encryption of the PostgreSQL volume remains the owner's deployment responsibility on top of this application-layer protection.

## Readback and masking

Both routes return the same masked shape. The full IBAN is **never** returned and never logged:

```json
{
  "caregiverId": "0198e2c2-2222-7777-8888-000000000001",
  "accountHolderName": "Mohamed Ahmed",
  "bankCode": "NBE",
  "maskedIban": "****0002",
  "status": "Pending",
  "updatedOnUtc": "2026-10-08T10:00:00Z"
}
```

`GET` with no saved account returns `404 Caregivers.PayoutAccount.NotFound`.

## Persistence

Table `caregivers.caregiver_payout_accounts` (`id`, `caregiver_id` unique, `account_holder_name`, `bank_code`, `iban_ciphertext`, `iban_last4`, `status`, `rejection_reason`, `created_on_utc`, `updated_on_utc`). No plaintext IBAN column exists. Migration `AddCaregiverPayoutAccount` is authored only and must not be applied except on an explicitly authorized database. No seed data is committed.

## Errors

- `401` — missing/invalid token, or authenticated identity has no caregiver profile.
- `403` — valid token without a caregiver account role.
- `404 Caregivers.PayoutAccount.NotFound` — `GET` with no saved account.
- `400 Caregivers.PayoutAccount.InvalidIban` — IBAN fails structure/checksum validation.
- `400 Caregivers.PayoutAccount.UnknownBank` — bank code matches no bank.
- `409 Caregivers.PayoutAccount.InactiveBank` — bank code matches an inactive bank.

## Payout timing policy (Admin-managed)

Payout eligibility timing is controlled by the Admin-managed caregiver payout policy (payoutDelayHours after a booking is both Completed and Paid). The policy has no fee field: entitlement is always the booking snapshot `BaseCaregiverFee` in full, and the snapshot `PlatformFeeAmount` (charged to the family on top) is never deducted. Policy administration is a SuperAdmin/FinanceAdmin operation; see docs/admin/finance-platform-charges.md.

## Admin payout records and manual transfers

Admin payout operations require the `PayoutOperationalAdmin` policy (**SuperAdmin or FinanceAdmin**). SupportAdmin and ContentAdmin are denied. Transfers themselves are manual bank transfers executed outside the API; these routes only record them. No payment-provider integration exists.

```text
GET  /api/v1/admin/caregiver-payouts?status=&caregiverId=&page=1&pageSize=20
GET  /api/v1/admin/caregiver-payouts/{payoutId}
POST /api/v1/admin/caregiver-payouts/record
POST /api/v1/admin/caregiver-payouts/{payoutId}/mark-failed
```

The list returns recorded payouts (both `Paid` and `Failed`) ordered by recording time descending, with optional status/caregiver filters and `PagedResult` pagination (page size clamped 1–100). Detail returns one payout or `404 Caregivers.Payouts.NotFound`.

Record body:

```json
{
  "bookingId": "0198e2c2-2222-7777-8888-000000000003",
  "transferReference": "BANK-2026-000123",
  "evidence": "Transfer advice 000123",
  "reason": "October weekly payout"
}
```

Recording validates, in order: the booking exists and is Completed **and** Paid (missing timestamps fail closed); an effective payout policy exists (otherwise `Finance.PayoutPolicy.Missing`); the policy delay has passed since `max(completedOnUtc, paidOnUtc)`; the caregiver has a saved payout account; and no `Paid` payout already exists for the booking (`409 Caregivers.Payouts.Conflict`, backed by a partial unique index so concurrent duplicate records serialize to one success and one conflict). Success returns `201` with the payout in `Paid` status.

A `Paid` payout recorded in error (for example, a rejected bank transfer) is corrected with `POST .../mark-failed` and a reason body (`{"reason": "..."}`), moving it to `Failed` with the failing actor and time. `Failed` is terminal for that row; recording again for the same booking creates a new row. Marking a non-`Paid` payout returns `409 Caregivers.Payouts.InvalidState`.

## Amount calculation

Per payout, from the booking price snapshot and the effective payout policy version:

- `grossAmount` = snapshot `BaseCaregiverFee` (the caregiver entitlement basis).
- `platformFeeAmount` = snapshot `PlatformFeeAmount`, kept for audit visibility only. It is the fee charged to the family on top of the base fee and is **never deducted** from the caregiver payout.
- `netAmount` = `grossAmount` (no payout-level fee exists or is deducted; enforced structurally and by test).
- `currency` = snapshot currency; `policyVersion` = applied payout-policy version.

Example response (record/detail):

```json
{
  "payoutId": "0198e2c2-3333-7777-8888-000000000001",
  "bookingId": "0198e2c2-2222-7777-8888-000000000003",
  "caregiverId": "0198e2c2-2222-7777-8888-000000000001",
  "grossAmount": 150.00,
  "platformFeeAmount": 22.50,
  "netAmount": 150.00,
  "currency": "EGP",
  "status": "Paid",
  "transferReference": "BANK-2026-000123",
  "evidence": "Transfer advice 000123",
  "failureReason": null,
  "recordedOnUtc": "2026-10-08T10:00:00Z",
  "paidOnUtc": "2026-10-08T10:00:00Z",
  "failedOnUtc": null,
  "recordedBy": "0198e2c2-1111-7777-8888-000000000001",
  "policyVersion": 2,
  "bankCode": "NBE",
  "maskedIban": "****0002"
}
```

Bank details are the `bankCode` and masked IBAN snapshotted from the caregiver payout account at record time. The full IBAN is never returned by any payout route and never logged. Audit identity is the recording/failing actor (`recordedBy`, plus failure actor/time on `Failed` rows).

## Caregiver-facing payout reads (self-only)

Four authenticated reads. The caregiver is resolved from the JWT identity — there is no `caregiverId` parameter — so a caller can never read another caregiver's payouts. All routes require `CaregiverAccess`; a valid token whose identity has no caregiver profile receives `401`.

```text
GET /api/v1/caregiver/payouts/summary
GET /api/v1/caregiver/payouts?status=&page=1&pageSize=20
GET /api/v1/caregiver/payouts/{payoutId}
GET /api/v1/caregiver/payment-policy
```

### Status labels (merged T4)

Payout rows stay per-booking `Paid` or `Failed`. **`Transferred` ≡ `Paid`**: the historic T4 "Transferred" label names the same merged state and is documented as an alias, never renamed in the API. There is **no `Processing` state** on any of these surfaces (none exists in the product; nothing creates one). The `status=` query parameter accepts `Paid` or `Failed`; any other value fails model binding with `400`.

### Summary

```json
{
  "currency": "EGP",
  "unpaidAmount": 300.00,
  "transferredAmount": 150.00,
  "failedAmount": 150.00,
  "nextExpectedPayoutOnUtc": "2026-10-08T00:00:00Z"
}
```

- `unpaidAmount` — computed eligible-unrecorded amount: Σ snapshot `BaseCaregiverFee` over bookings that are Completed **and** Paid, past the effective payout-policy delay (`max(completedOnUtc, paidOnUtc) + payoutDelayHours`), with **no `Paid` payout row**. A `Failed` payout row does not clear the debt — the same booking may be recorded again after a failure, so those bookings still count as unpaid.
- `transferredAmount` — Σ `netAmount` of `Paid` rows. `failedAmount` — Σ `netAmount` of `Failed` rows.
- `nextExpectedPayoutOnUtc` — earliest eligibility time among unpaid bookings, computed **only when the payout account is `Verified`** (`null` otherwise, and `null` when no unpaid booking exists). The value can be in the past (already due, awaiting an Admin record) or in the future.
- `currency` — from the most recent payout row, else the most recent completed-booking snapshot; `null` when the caregiver has no payout and no completed booking. Rows are expected in one currency (today: EGP); mixed-currency summaries are not modelled.
- No effective payout policy exists → the summary fails closed with `400 Finance.PayoutPolicy.Missing` (eligibility is undefined without the configured delay; the same code admin recording returns — the shared problem-details mapper does not special-case it).

### List

`page` is clamped to ≥ 1, `pageSize` to 1–100 (out-of-range values are clamped, not rejected), ordered newest-recorded first, in the standard `PagedResult` envelope:

```json
{
  "items": [{
    "id": "0198e2c2-3333-7777-8888-000000000001",
    "amount": 150.00,
    "currency": "EGP",
    "status": "Paid",
    "bankNameArabic": "البنك الاهلي المصري",
    "bankNameEnglish": "National Bank of Egypt",
    "maskedIban": "****0002",
    "transferReference": "BANK-2026-000123",
    "createdOnUtc": "2026-10-08T10:00:00Z",
    "transferredOnUtc": "2026-10-08T10:00:00Z",
    "failureReason": null
  }],
  "page": 1, "pageSize": 20, "totalCount": 1, "totalPages": 1
}
```

- `amount` = `grossAmount` (`netAmount` always equals gross — no payout-level fee exists; family-paid `platformFeeAmount` is not part of these reads).
- `bankNameArabic`/`bankNameEnglish` — resolved from the payout's snapshotted `bankCode` against the bank catalogue; `null` when the code matches no catalogue row (renamed/removed bank), never a guess.
- `transferredOnUtc` — the `Paid` time (`paidOnUtc`, equal to `createdOnUtc` for immediately-paid rows) on `Paid` rows; `null` on `Failed` rows.

### Detail

Identical item fields plus a one-element `bookingItems` array (payouts are per-booking rows; the shape matches the coded mobile contract and stays forward-compatible if grouping ever arrives):

```json
"bookingItems": [{ "bookingId": "0198e2c2-2222-7777-8888-000000000003", "caregiverAmount": 150.00, "currency": "EGP" }]
```

Reading another caregiver's payout id returns `404 Caregivers.Payouts.NotFound` (ownership failures do not leak existence); an unknown id returns the same `404`.

### Payment policy

```json
{
  "descriptionArabic": "يُدفع بعد 72 ساعة من إتمام الحجز وسداده",
  "descriptionEnglish": "Payout is released 72 hours after the booking is completed and paid.",
  "requiresVerifiedBankAccount": true,
  "minimumPayoutAmount": null,
  "expectedProcessingDays": null
}
```

- `descriptionArabic`/`descriptionEnglish` — admin-authored bilingual terms carried by the effective payout policy (added to policy create; each create is a new policy version). Policies created before descriptions existed read back `null`; with no effective policy at all the endpoint still returns `200` with both descriptions `null`.
- `requiresVerifiedBankAccount` — always `true`, derived from the enforced `Verified` gate on payout recording.
- `minimumPayoutAmount` / `expectedProcessingDays` — honest `null`s: no minimum-amount or processing-time rule exists in the product and none is created.

### Errors (caregiver reads)

- `401` — missing/invalid token, or the identity has no caregiver profile.
- `403` — valid token without the caregiver account role.
- `404 Caregivers.Payouts.NotFound` — detail id not owned by the caller (or unknown).
- `400 Finance.PayoutPolicy.Missing` — summary when no effective payout policy exists (fail closed).
- `400` — `status=` value other than `Paid`/`Failed` (enum binding).

## Admin payout-account review

Payout accounts start `Pending` and pay out only while `Verified`. Any caregiver
edit returns the account to `Pending` for a fresh review. All routes below
require the `PayoutOperationalAdmin` policy (**SuperAdmin or FinanceAdmin**);
no verification role is granted to other account types.

```text
GET  /api/v1/admin/caregiver-payout-accounts?status=&page=1&pageSize=20
GET  /api/v1/admin/caregiver-payout-accounts/{caregiverId}
GET  /api/v1/admin/caregiver-payout-accounts/{caregiverId}/reviews
POST /api/v1/admin/caregiver-payout-accounts/{caregiverId}/approve
POST /api/v1/admin/caregiver-payout-accounts/{caregiverId}/reject
POST /api/v1/admin/caregiver-payout-accounts/{caregiverId}/revoke
POST /api/v1/admin/caregiver-payout-accounts/{caregiverId}/reveal
```

The queue lists accounts pending-first with masked IBANs only, plus the
caregiver identity names for matching. Detail adds verification evidence and
decision history pointers. Every decision carries the reviewer's current
`expectedRevision`; a decision against a stale revision fails with `409
Caregivers.PayoutAccount.RevisionConflict` so the reviewer reloads first.

Ownership verification is a manual external check: the reviewer confirms the
exact account belongs to the caregiver through an authorized third-party
source outside Sanad (for example the bank's own portal) and records that
source plus an optional non-sensitive reference in Sanad. A format check,
account-existence check, or name plausibility alone is not ownership
verification. Approve requires the verification source (`POST` with
`{ "expectedRevision": 3, "verificationSource": "BankPortal", "reference":
"optional" }`); reject and revoke require a reason of 1–500 characters.
Allowed transitions are `Pending → Verified`, `Pending → Rejected`, and a
direct `Verified → Revoked`; repeats against the wrong state return `409
Caregivers.PayoutAccount.InvalidState`.

The reveal route is the only operation that returns the full IBAN. It is a
`POST` (never a `GET`, so the IBAN never appears in a URL), sends
`Cache-Control: no-store`, writes an audited `Revealed` history entry, and
fails closed with `503` when decryption is unavailable. Plaintext IBANs and
provider credentials/results are never logged or persisted. Failed decryption
writes no audit row.

T4 payout recording requires a `Verified` account; `Pending`, `Rejected`, and
`Revoked` accounts are rejected with `409
Caregivers.Payouts.AccountNotVerified`. The account revision and status are
rechecked under a database row lock in the same transaction that records the
payout. A concurrent caregiver edit or Admin revocation therefore either
completes before the payout check (and blocks it) or after the payout has been
recorded; a payout cannot use a stale verified-account snapshot.

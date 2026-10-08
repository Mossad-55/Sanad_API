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

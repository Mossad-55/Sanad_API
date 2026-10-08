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
- `iban`: required; spaces/dashes stripped, uppercased, maximum 34 characters; must pass the ISO 13616 structure and mod-97 checksum. Invalid → `400 Caregivers.PayoutAccount.InvalidIban`.

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

Table `caregivers.caregiver_payout_accounts` (`id`, `caregiver_id` unique, `account_holder_name`, `bank_code`, `iban` normalized, `status`, `rejection_reason`, `created_on_utc`, `updated_on_utc`). Migration `AddCaregiverPayoutAccount` is authored only and must not be applied except on an explicitly authorized database. No seed data is committed.

## Errors

- `401` — missing/invalid token, or authenticated identity has no caregiver profile.
- `403` — valid token without a caregiver account role.
- `404 Caregivers.PayoutAccount.NotFound` — `GET` with no saved account.
- `400 Caregivers.PayoutAccount.InvalidIban` — IBAN fails structure/checksum validation.
- `400 Caregivers.PayoutAccount.UnknownBank` — bank code matches no bank.
- `409 Caregivers.PayoutAccount.InactiveBank` — bank code matches an inactive bank.

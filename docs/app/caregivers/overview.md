# Caregiver onboarding (mobile app)

Caregiver self-service routes live under `/api/v1/caregiver/...`. They drive the caregiver onboarding wizard, the post-approval profile/calendar management screens, and the booking actions (accept / decline / start / complete).

## Access

All routes require a **Normal JWT** for a caregiver account:

- Authenticated user
- Claim `access_type` = `Normal`
- Claim `account_type` = `MedicalCaregiver` or `CompanionCaregiver`

Policy: `CaregiverAccess`.

A token issued before email/phone verification has `access_type` = `RestrictedVerification` (login returns `accessType: 2`, no refresh token). Every `/caregiver/...` route then answers **403** — the app must finish OTP verification first (see `docs/auth/registration-and-verification.md`). Family/Elderly/admin accounts also receive 403.

The caregiver type (`Medical` / `Companion`) is taken from the JWT `account_type` claim at bootstrap and is fixed for the lifetime of the profile.

## Profile lifecycle

```text
Onboarding ──submit──▶ PendingReview ──approve──▶ Active
                            │                       │
                            ├─request-correction─▶ NeedsCorrection ──resubmit──▶ PendingReview
                            └─reject────────────▶ Rejected (terminal)
Active ──suspend (admin)──▶ Suspended ──reactivate──▶ Active
```

- The caregiver saves sections in any order; saves never reset and never expire.
- The only gated action is **submit**: the aggregate enforces the readiness checklist (below).
- After approval, editing the professional profile (Medical or Companion) or replacing a mandatory certificate file returns the caregiver to `PendingReview` + `Unavailable` until re-approved.
- Admins perform review/approve/reject/suspend actions — see `docs/admin/caregivers-review.md`.
- `Deactivated` (terminal) is not an admin transition: it is reached only by the caregiver deleting his own account — see [Delete my account](#delete-my-account). A `Deactivated` profile is retained forever but drops out of discovery and quotes (both filter `status = 4`).

## Readiness checklist (submit / resubmit)

| Requirement | Medical | Companion |
|---|---|---|
| Professional profile | Medical profile (title, specialization, degree, experience) | Companion profile (specialization, experience) |
| Services | ≥ 1 | ≥ 1 |
| Languages | ≥ 1 | ≥ 1 |
| Service areas | ≥ 1 (max 10) | ≥ 1 (max 10) |
| Pricing | Medical pricing (4 prices) | Companion pricing (3 prices) |
| Weekly schedule | shifts and/or home-visit windows | availability windows |
| Mandatory certificates | Practice License + Graduation Certificate present, unexpired, Pending or Verified | n/a (companions cannot add certificates) |

Activation (admin approve / reactivate / BecomeAvailable) additionally requires mandatory certificates to be **Verified** and unexpired (Medical).

## Delete my account

```text
DELETE /api/v1/account
```

Self-service deletion of the **caregiver** side of the caller's account. Success is **204 No Content** with an empty body. The route uses the `NormalAccess` policy (not `CaregiverAccess`) so that a family user reaches the handler and receives the coded `403` below instead of a bare policy reject.

Anonymize & retain — **nothing is hard-deleted**; every row is kept for audit, settlement and booking history.

Two modes (owner-locked, Q1 = Option B — the user decides which account to delete):

| Caller | What happens |
|---|---|
| **PURE caregiver** (no Family account) | Full deletion: the Identity account is blocked and PII-scrubbed (names → `محذوف` / `Deleted`, email → `null`, phone → a unique non-assignable `+200…` placeholder, avatar dropped, identity document revoked), all device sessions are revoked, and the caregiver profile becomes `Deactivated`. Re-login with the old credentials then fails with `401 Identity.Login.InvalidCredentials` (the email is gone). |
| **HYBRID** (Family + caregiver) | Caregiver side only: the profile becomes `Deactivated` and the caregiver account type is removed from the user. The login, the Family account and the family membership stay **unchanged**, and family sessions are **not** revoked. |

Because the endpoint never touches the Family side, it can never orphan a family — no family-owner guard applies here (Q2 is moot by design). Family deletion is a separate flow: `DELETE /api/v1/family` (see `docs/app/families/family.md`).

**D11 active-booking guard** — the deletion is refused with `409 Identity.Account.ActiveBookingExists` while any of the caregiver's bookings is `PendingCaregiverApproval` (2), `Confirmed` (3) or `InProgress` (4). `PendingPayment` (1) deliberately does **not** block: the slot is not committed until the family pays. The guard is fail-safe — if the booking lookup itself fails, the request fails and nothing is mutated.

Retries are safe: the operation is idempotent, and a partially completed deletion (Identity side done, profile not yet deactivated — or profile deactivated, account type not yet removed) converges on the next call.

### Errors

| Status | `code` | Cause |
|---|---|---|
| 401 | — | Missing or invalid JWT (no usable `sub` claim) |
| 403 | `Identity.Account.CaregiverOnly` | The caller has no caregiver account (e.g. Family only) |
| 404 | `Identity.Account.UserNotFound` | No Identity user for the token subject |
| 404 | `Identity.Account.CaregiverProfileNotFound` | Caregiver account type present but no caregiver profile row |
| 409 | `Identity.Account.ActiveBookingExists` | D11 guard: a booking is `PendingCaregiverApproval` / `Confirmed` / `InProgress` |

## Document map

| Screen / flow | Doc |
|---|---|
| Bootstrap + profile + address | `docs/app/caregivers/profile.md` |
| Services, languages, areas + pricing | `docs/app/caregivers/selections-and-pricing.md` |
| Weekly schedule + availability toggle | `docs/app/caregivers/schedule-and-availability.md` |
| Certificate upload/replace/remove | `docs/app/caregivers/certificates.md` |
| Submit for review / corrections | `docs/app/caregivers/submission.md` |
| Booking actions on family requests | `docs/app/caregivers/bookings.md` |
| Postman | `docs/postman/app/Sanad.App.Caregiver.postman_collection.json` |

Public app surfaces (splash screens, active lookups) that the wizard reads from are documented in `docs/app/public/` and collected in `docs/postman/app/Sanad.App.Public.postman_collection.json`.

## Conventions

- Request bodies are JSON except certificate uploads (`multipart/form-data`).
- Enum fields are sent as numbers:

  | Field | Values |
  |---|---|
  | `type` (caregiver, from token) | `1` Medical, `2` Companion |
  | `status` | `1` Onboarding, `2` PendingReview, `3` NeedsCorrection, `4` Active, `5` Suspended, `6` Rejected, `7` Deactivated |
  | `availability` | `1` Available, `2` Unavailable |
  | certificate `type` | `1` PracticeLicense, `2` GraduationCertificate, `3` AdditionalCertificate |
  | `bookingType` (companion windows) | `1` Hourly, `2` EightHourDay, `3` Overnight |
  | medical `shiftType` | `1` EightHourMorning, `2` EightHourEvening, `3` EightHourNight, `4` TwelveHourDay, `5` TwelveHourNight, `6` TwentyFourHourLiveIn |
  | `dayOfWeek` | `0` Sunday … `6` Saturday |

- Times are `HH:mm` (24-hour). Dates are `YYYY-MM-DD`.
- Every successful save returns the full `CaregiverProfileResponse`, so the screen can repaint from the response without a follow-up GET.
- Errors are `application/problem+json` with an `code`; see `docs/auth/errors.md` and the error catalog in each doc.

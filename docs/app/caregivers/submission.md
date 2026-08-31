# Submit for review and correction flow

Requires the `CaregiverAccess` policy.

## Submit (and resubmit)

One endpoint handles both the first submission (status `Onboarding`) and resubmission after the admin requests corrections (status `NeedsCorrection`):

```http
POST /api/v1/caregiver/submit
Authorization: Bearer {{caregiverToken}}
```

(no request body)

- `200` — status becomes `PendingReview`, availability `Unavailable`. Body is the full `CaregiverProfileResponse`.
- `409 Caregivers.Onboarding.InvalidState` — either the caregiver is **not ready** (a readiness check failed), or the status is neither `Onboarding` nor `NeedsCorrection` (e.g. a second submit while `PendingReview`, or after `Rejected`).
- `404 Caregivers.Onboarding.NotFound` — no profile.

## Readiness checklist

The domain enforces the checklist at submit time (see `overview.md` for the Medical/Companion matrix):

- professional profile filled;
- ≥ 1 service, ≥ 1 language, ≥ 1 service area (≤ 10 areas);
- pricing set for the caregiver type;
- weekly schedule with at least one availability entry;
- **Medical only**: Practice License and Graduation Certificate present, unexpired, and Pending or Verified.

Because the failure collapses to `InvalidState`, the app should determine *which* sections are incomplete from the `GET /caregiver/profile` response (null sections / empty arrays / certificates not compliant) and point the caregiver at them, rather than relying on the error text.

## After submit

| Admin action | Caregiver status | What the caregiver sees |
|---|---|---|
| Approve | `Active` (availability stays `Unavailable` until he toggles available) | Can start accepting bookings via `POST /caregiver/availability/available` |
| Request correction | `NeedsCorrection` | `statusReason` contains the admin's reason; fix sections, then `POST /caregiver/submit` again |
| Reject application | `Rejected` (terminal) | `statusReason` contains the reason; the submit endpoint will not resubmit from this state |
| Suspend (while Active) | `Suspended` | `statusReason` contains the reason; admin reactivates later |

The caregiver polls/reads `GET /caregiver/profile` for `status` and `statusReason`; there are no push notifications in this phase.

## Post-approval changes

While **Active**:

- updating the professional profile (Medical or Companion), or
- replacing a mandatory certificate file

returns the caregiver to `PendingReview` + `Unavailable` until an admin re-approves. A revoked/rejected mandatory certificate forces `Unavailable` (revocation while Active also suspends the caregiver).

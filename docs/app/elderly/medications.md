# Elderly medication self-service

These routes let a signed-in Elderly user read the medication schedule for the
dependent linked to that Elderly identity and record a scheduled dose as taken.
They do not allow prescription changes or dose skipping. Family prescription
management and the shared response shapes are documented in
[`../families/medications.md`](../families/medications.md).

## Access and profile scope

- Every route requires a Normal JWT for an Elderly account.
- The API resolves the linked dependent from the authenticated user ID. There
  is no caller-supplied dependent ID, and another dependent's medication data
  cannot be selected through these routes.
- Reads are read-only. Taking a dose writes the same medication dose history
  that Family members read and records the Elderly user as the actor.

## Routes

| Method | Route | Behavior |
|---|---|---|
| `GET` | `/api/v1/elderly/medications` | List prescriptions for the linked dependent. |
| `GET` | `/api/v1/elderly/medications/dashboard?date=YYYY-MM-DD` | Return the medication schedule and dashboard for a required profile-local calendar date. |
| `POST` | `/api/v1/elderly/medications/{medicationId}/doses/take` | Record an eligible scheduled dose as taken. |

`date` is required for the dashboard and must use the ISO `YYYY-MM-DD` format.
There is no default to server UTC or to the current day; the caller supplies the
date in the Elderly profile's local calendar semantics.

The dose-taking request uses the existing `RecordDoseTakenRequest` contract:

```json
{
  "scheduledDate": "2026-09-25",
  "scheduledTime": "20:00:00",
  "notes": "Taken after dinner"
}
```

The prescription must be Active, the scheduled date must be within its start
and optional end dates, and the scheduled time must appear in its daily
schedule. A successful take creates a shared dose-log entry, records the
Elderly actor, and decrements tracked stock by the prescribed dose quantity.
The unique medication/date/time dose-log constraint rejects duplicate and
racing takes, so clients should treat an already-recorded dose as a conflict
and refresh the dashboard. Dose taking is not an idempotent retry operation.

The take response is the existing `MedicationDoseResponse` shape, including
`doseLogId`, medication and scheduled-dose details, status, `takenAtUtc`, and
`loggedByUserId`. The dashboard returns the existing `MedicationDashboardResponse`
shape. See the Family medication reference for full response examples.

## Not available to Elderly callers

There is no Elderly route to add, edit, pause, resume, discontinue, or skip a
prescription, or to directly choose the dependent whose medications are read.
Those operations remain under Family management. These routes also do not
define late/missed-dose alerts or notification delivery.

## Postman and Bruno

The [Elderly Postman collection](../../postman/app/Sanad.App.Elderly.postman_collection.json)
contains read requests and an explicitly state-changing take-dose example.
The Bruno Elderly medication folder covers dashboard/list reads only; it does
not issue a take request.

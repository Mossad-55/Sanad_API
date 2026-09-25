# Admin Elderly medication operational reads

These routes provide a restricted operational view of elderly medication
prescriptions, persisted dose logs, and dose-log adherence aggregates. They are
read endpoints only: there are no Admin prescription, dose, or stock write
routes.

## Access and audit

Every route requires a JWT with `access_type = Normal` and the
`ElderlyMedicationOperationalRead` policy. Only `SuperAdmin` and
`SupportAdmin` account types are admitted. `ContentAdmin`, Family, Elderly,
and restricted-verification tokens are denied.

Each successful sensitive read first appends one immutable audit row and then
reads the requested data. The row records the actor user ID, actor account
type, action/resource, UTC occurrence time, and request correlation ID. It
contains no clinical payload. If the audit write fails, the request fails
closed and the clinical result is not returned. The correlation ID is taken
from `X-Correlation-ID`, or the request trace identifier when that header is
absent.

Because a successful GET has this database write side effect, the examples in
the Admin Postman collection and Bruno folder are manual/stateful examples.
Do not run them against shared or live environments without an approved test
fixture and owner approval.

## Endpoints

| Method | Route | Query parameters | Result |
|---|---|---|---|
| `GET` | `/api/v1/admin/elderly/medications` | `page` (default 1), `pageSize` (default 20), optional `dependentId`, `status`, `search` | Paged medication records with linked Elderly Arabic/English names and Family ID, filtered by dependent, medication status, and case-insensitive name search. |
| `GET` | `/api/v1/admin/elderly/medications/{medicationId}` | — | One medication record with linked Elderly Arabic/English names and Family ID. |
| `GET` | `/api/v1/admin/elderly/medications/{medicationId}/doses` | Required `startDate` and `endDate` (`YYYY-MM-DD`), optional `status` | Persisted dose logs for the selected medication, inclusive and ordered by scheduled date/time. The range is at most 31 calendar days. |
| `GET` | `/api/v1/admin/elderly/medications/adherence` | Optional `startDate`, `endDate`, `dependentId` | Counts and adherence percentage computed from persisted dose logs only. |

The dose timeline rejects a reversed range or an inclusive range longer than
31 days with `400 Families.AdminMedication.InvalidDateRange`. The medication
detail and dose timeline return `404 Families.AdminMedication.NotFound` when
the medication does not exist. The detail audit is still written before that
not-found result. No request body is accepted by these GET routes.

`status` uses the existing numeric `MedicationStatus` and `DoseStatus` enum
values from the medication contract. The adherence response is:

```json
{
  "totalDoses": 3,
  "takenDoses": 1,
  "skippedDoses": 1,
  "missedDoses": 0,
  "scheduledDoses": 1,
  "adherenceRate": 33.33
}
```

`adherenceRate` is the taken-dose count divided by all persisted dose-log
rows, rounded to two decimal places; it is `0` when no logs match. The service
does not synthesize scheduled rows and does not infer clinical state outside
the persisted dose logs.

## Medication and dose response shapes

List/detail items include `familyId`, `elderlyArabicName`,
`elderlyEnglishName`, and a nested `medication` resource. The nested shared
medication resource contains `id`, `dependentId`, `name`, `dosage`, `doseUnit`,
`doseQuantity`, `doseTimes`, `startDate`, `endDate`, `instructions`,
`stockQuantity`, `lowStockThreshold`, `stockStatus`, `status`, `createdOnUtc`,
and `updatedOnUtc`. This identifies whose medication is being inspected without
including the Elderly profile's address, contact, image key, or health notes.

Dose timeline rows use the shared dose-log shape: `doseLogId`, `medicationId`,
`medicationName`, `dosage`, `doseUnit`, `doseQuantity`, `instructions`,
`scheduledDate`, `scheduledTime`, `status`, `takenAtUtc`, `skippedAtUtc`,
`notes`, and `loggedByUserId`.

This surface does not expose audit rows as clinical response data. Audit
retention/export behavior and any broader operational-admin screens are not
defined by this contract.

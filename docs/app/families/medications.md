# Elderly Medications, Inventory & Daily Dose Schedule

Routes for managing an elderly dependent's medication inventory, daily recurring dose schedules, real-time stock tracking with low-stock alerts, and daily adherence dashboard.

All routes live under `/api/v1/family/dependents/{dependentId}/medications/...`.

## Access

- **Normal JWT** for a **Family** account (`access_type = Normal`, `account_type = Family`).
- The caller must be an active member of the family that owns the dependent.
- Every operation is scoped to the selected dependent's owning family. A dependent ID from another family, or a missing dependent on a dependent-scoped read/update/action, returns `403 Families.Medication.AccessDenied` without exposing whether medication data exists.
- **Write actions** (Add, Update, Stock Update, Status Toggles, Dose Take/Skip) require an **Owner** or **Editor** role (`403 Families.Medication.AccessDenied` for Viewers). The same access-denied result applies to a Viewer, a non-member, or a foreign/missing dependent on dependent-scoped operations.
- **Read actions** (List, Get by ID, Dashboard) are available to all members of the owning family (**Owner**, **Editor**, **Viewer**).
- The Add command is the boundary exception: after caller/family write authorization, a missing or foreign dependent returns `404 Families.Medication.DependentNotFound`; it never creates medication data.

---

## Data Model & Concepts

### Medication Entity

| Field | Type | Description |
|---|---|---|
| `id` | UUID | Unique medication identifier (v7 GUID). |
| `dependentId` | UUID | Foreign key to the elderly dependent. |
| `name` | string | Commercial/generic drug name (e.g., "Panadol Extra", "أوميجا 3"). Max 200 chars. |
| `dosage` | string | Strength / dosage specification (e.g., "500 mg", "1000 ملغ"). Max 100 chars. |
| `doseUnit` | string | Unit description (e.g., "قرص", "كبسولة", "مل", "قطرة", "بخاخ", "حقنة"). Max 50 chars. |
| `doseQuantity` | int | Quantity to consume per intake (e.g., `1`, `2`). Must be >= 1. |
| `doseTimes` | TimeOnly[] | Array of daily schedule times in `HH:mm` format (e.g., `["08:00", "20:00"]`). |
| `startDate` | DateOnly | Treatment start date (`YYYY-MM-DD`). |
| `endDate` | DateOnly? | Optional treatment end date (`YYYY-MM-DD`). If null, medication is ongoing. |
| `instructions` | string? | Usage instructions (e.g., "بعد الأكل", "قبل النوم مباشرة"). Max 500 chars. |
| `stockQuantity` | int? | Current remaining inventory count (e.g., `30` pills). Null if untracked. |
| `lowStockThreshold` | int? | Threshold count at which a warning badge is raised. Null if untracked. |
| `stockStatus` | enum (int) | `1` = Normal, `2` = LowStock, `3` = OutOfStock, `4` = NotTracked. |
| `status` | enum (int) | `1` = Active, `2` = Paused, `3` = Completed, `4` = Discontinued. |

### Stock Management & Auto-Decrement

1. **Inventory Tracking**: Families can record how many pills/units remain (`stockQuantity`) and set a warning threshold (`lowStockThreshold`).
2. **Auto-Decrement on Dose Taken**: When a dose is marked as **Taken**, the system automatically decrements `stockQuantity` by `doseQuantity` (flooring at 0).
3. **Low/Out of Stock Alerts**:
   - `Normal` (`1`): `stockQuantity > lowStockThreshold`
   - `LowStock` (`2`): `stockQuantity <= lowStockThreshold` and `stockQuantity > 0`
   - `OutOfStock` (`3`): `stockQuantity == 0`
   - `NotTracked` (`4`): `stockQuantity == null`

---

## Endpoints

| Method | Route | Description | Role |
|---|---|---|---|
| `POST` | `/api/v1/family/dependents/{id}/medications` | Add new medication | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/medications` | List all medications for dependent | Any member |
| `GET` | `/api/v1/family/dependents/{id}/medications/{medId}` | Get medication by ID | Any member |
| `PUT` | `/api/v1/family/dependents/{id}/medications/{medId}` | Update medication details and, optionally, stock atomically | Owner / Editor |
| `PUT` | `/api/v1/family/dependents/{id}/medications/{medId}/stock` | Update stock quantity & alert threshold | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/pause` | Pause medication schedule | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/resume` | Resume paused medication | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/discontinue` | Discontinue medication | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/medications/dashboard` | Daily dashboard (today's schedule timeline & alerts) | Any member |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/doses/take` | Record dose as taken (auto decrements stock) | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/doses/skip` | Record dose as skipped with reason | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/medications/{medId}/doses/history` | Read persisted dose history for a date range | Any member |

---

## Request & Response Payloads

### 1. Add Medication

`POST /api/v1/family/dependents/{dependentId}/medications`

#### Request Body
```json
{
  "name": "أوميجا 3 (Omega 3)",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "doseTimes": [
    "08:00",
    "20:00"
  ],
  "startDate": "2026-09-01",
  "endDate": "2026-12-31",
  "instructions": "كبسولة واحدة بعد الأكل مباشرة مع كوب ماء كبير",
  "stockQuantity": 60,
  "lowStockThreshold": 10
}
```

#### Response `201 Created`
```json
{
  "id": "0191c2fa-9481-7f03-8209-1a483e58fa20",
  "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
  "name": "أوميجا 3 (Omega 3)",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "doseTimes": [
    "08:00:00",
    "20:00:00"
  ],
  "startDate": "2026-09-01",
  "endDate": "2026-12-31",
  "instructions": "كبسولة واحدة بعد الأكل مباشرة مع كوب ماء كبير",
  "stockQuantity": 60,
  "lowStockThreshold": 10,
  "stockStatus": 1,
  "status": 1,
  "createdOnUtc": "2026-09-03T10:00:00Z",
  "updatedOnUtc": "2026-09-03T10:00:00Z"
}
```

---

### 2. Update Medication Details (and Optional Stock)

`PUT /api/v1/family/dependents/{dependentId}/medications/{medicationId}`

The medication edit screen can save its details and inventory in one request. The optional `stock` object is the atomic-save extension:

- Omit `stock` to update details while preserving the existing inventory values (legacy detail-only behavior).
- If `stock` is present, both `stockQuantity` and `lowStockThreshold` keys must be supplied. Either value may explicitly be `null`.
- `{ "stockQuantity": null, "lowStockThreshold": null }` clears inventory tracking. A value paired with an explicit `null` is also valid.
- Details and stock are validated and persisted in the same database save. If validation or authorization fails, the update is not partially applied.

#### Request Body
```json
{
  "name": "أوميجا 3 بلس",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "doseTimes": [
    "09:00",
    "21:00"
  ],
  "startDate": "2026-09-01",
  "endDate": "2026-12-31",
  "instructions": "بعد الإفطار والعشاء",
  "stock": {
    "stockQuantity": 90,
    "lowStockThreshold": 15
  }
}
```

The existing `PUT .../{medicationId}/stock` route remains supported for clients that update inventory separately. New edit screens should use the nested `stock` object when the user changes details and inventory together.

#### Response `200 OK`

The response is the updated medication resource, including `stockQuantity`, `lowStockThreshold`, `stockStatus`, and the updated timestamps.

---

### 3. Daily Schedule Dashboard

`GET /api/v1/family/dependents/{dependentId}/medications/dashboard?date=2026-09-03`

#### Query Parameters
- `date` *(optional)*: Target date (`YYYY-MM-DD`). Defaults to current UTC date.

#### Response `200 OK`
```json
{
  "activeMedicationsCount": 3,
  "lowStockMedicationsCount": 1,
  "totalDosesToday": 5,
  "takenDosesToday": 2,
  "remainingDosesToday": 3,
  "todayDoses": [
    {
      "doseLogId": "0191c2fc-aa11-7389-9a21-998811223344",
      "medicationId": "0191c2fa-9481-7f03-8209-1a483e58fa20",
      "medicationName": "أوميجا 3 (Omega 3)",
      "dosage": "1000 ملغ",
      "doseUnit": "كبسولة",
      "doseQuantity": 1,
      "instructions": "بعد الأكل",
      "scheduledDate": "2026-09-03",
      "scheduledTime": "08:00:00",
      "status": 2,
      "takenAtUtc": "2026-09-03T08:15:20Z",
      "skippedAtUtc": null,
      "notes": "تم التناول بعد الإفطار",
      "loggedByUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f"
    },
    {
      "doseLogId": null,
      "medicationId": "0191c2fa-9481-7f03-8209-1a483e58fa20",
      "medicationName": "أوميجا 3 (Omega 3)",
      "dosage": "1000 ملغ",
      "doseUnit": "كبسولة",
      "doseQuantity": 1,
      "instructions": "بعد الأكل",
      "scheduledDate": "2026-09-03",
      "scheduledTime": "20:00:00",
      "status": 1,
      "takenAtUtc": null,
      "skippedAtUtc": null,
      "notes": null,
      "loggedByUserId": null
    }
  ],
  "lowStockAlerts": [
    {
      "id": "0191c301-1122-7788-99aa-bbccddeeff00",
      "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
      "name": "Panadol Extra",
      "dosage": "500 mg",
      "doseUnit": "قرص",
      "doseQuantity": 2,
      "doseTimes": ["14:00:00"],
      "startDate": "2026-09-01",
      "endDate": null,
      "instructions": "عند اللزوم",
      "stockQuantity": 4,
      "lowStockThreshold": 10,
      "stockStatus": 2,
      "status": 1,
      "createdOnUtc": "2026-09-01T12:00:00Z",
      "updatedOnUtc": "2026-09-03T08:15:20Z"
    }
  ]
}
```

---

### 4. Record Dose as Taken

`POST /api/v1/family/dependents/{dependentId}/medications/{medicationId}/doses/take`

#### Request Body
```json
{
  "scheduledDate": "2026-09-03",
  "scheduledTime": "20:00:00",
  "notes": "تم إعطاء الجرعة بواسطة الابن"
}
```

#### Response `200 OK`
```json
{
  "doseLogId": "0191c310-9988-7766-5544-33221100aabb",
  "medicationId": "0191c2fa-9481-7f03-8209-1a483e58fa20",
  "medicationName": "أوميجا 3 (Omega 3)",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "instructions": "بعد الأكل",
  "scheduledDate": "2026-09-03",
  "scheduledTime": "20:00:00",
  "status": 2,
  "takenAtUtc": "2026-09-03T20:05:00Z",
  "skippedAtUtc": null,
  "notes": "تم إعطاء الجرعة بواسطة الابن",
  "loggedByUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f"
}
```

---

### 5. Record Dose as Skipped

`POST /api/v1/family/dependents/{dependentId}/medications/{medicationId}/doses/skip`

#### Request Body
```json
{
  "scheduledDate": "2026-09-03",
  "scheduledTime": "20:00:00",
  "reason": "المريض نائم ولم يرغب في الاستيقاظ"
}
```

#### Response `200 OK`
```json
{
  "doseLogId": "0191c310-9988-7766-5544-33221100aabb",
  "medicationId": "0191c2fa-9481-7f03-8209-1a483e58fa20",
  "medicationName": "أوميجا 3 (Omega 3)",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "instructions": "بعد الأكل",
  "scheduledDate": "2026-09-03",
  "scheduledTime": "20:00:00",
  "status": 3,
  "takenAtUtc": null,
  "skippedAtUtc": "2026-09-03T20:30:00Z",
  "notes": "المريض نائم ولم يرغب في الاستيقاظ",
  "loggedByUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f"
}
```

---

### 6. Read Dose History

`GET /api/v1/family/dependents/{dependentId}/medications/{medicationId}/doses/history?startDate=2026-09-01&endDate=2026-09-30`

Both `startDate` and `endDate` are required ISO dates (`YYYY-MM-DD`). The start date must not be after the end date, and the inclusive range may contain at most 31 days. Owner, Editor, and Viewer members of the dependent's own family may read this route; it does not change dose or medication data.

The response contains persisted dose-log rows only, ordered by scheduled date and time ascending. No rows are synthesized for scheduled doses that were never logged. The log event, status, and logged time come from the persisted dose record. Medication name, dosage, unit, quantity, and instructions are read from the current editable Medication row, so those fields can reflect later medication edits rather than a prescription snapshot from the time of the dose.

#### Query Parameters

| Name | Required | Format | Constraint |
|---|---|---|---|
| `startDate` | Yes | `YYYY-MM-DD` | Must be on or before `endDate`. |
| `endDate` | Yes | `YYYY-MM-DD` | Inclusive range from `startDate` must be no more than 31 days. |

#### Response `200 OK`

Returns an array using the dose-log response shape shown above. An empty array means there are no persisted dose logs in the requested range.

---

### 7. Update Inventory Stock & Threshold (Legacy Separate Route)

`PUT /api/v1/family/dependents/{dependentId}/medications/{medicationId}/stock`

This route remains supported for clients that intentionally update only inventory. For the edit screen's one-action Save, use the nested `stock` object on the main medication update route described above.

#### Request Body
```json
{
  "stockQuantity": 90,
  "lowStockThreshold": 15
}
```

#### Response `200 OK`
```json
{
  "id": "0191c2fa-9481-7f03-8209-1a483e58fa20",
  "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
  "name": "أوميجا 3 (Omega 3)",
  "dosage": "1000 ملغ",
  "doseUnit": "كبسولة",
  "doseQuantity": 1,
  "doseTimes": [
    "08:00:00",
    "20:00:00"
  ],
  "startDate": "2026-09-01",
  "endDate": "2026-12-31",
  "instructions": "بعد الأكل",
  "stockQuantity": 90,
  "lowStockThreshold": 15,
  "stockStatus": 1,
  "status": 1,
  "createdOnUtc": "2026-09-03T10:00:00Z",
  "updatedOnUtc": "2026-09-03T10:15:00Z"
}
```

---

## Error Catalog

| Code | HTTP Status | Description |
|---|---|---|
| `Families.Medication.DependentNotFound` | `404` | Add-medication only: the requested dependent does not exist in the caller's family. |
| `Families.Medication.NotFound` | `404` | Medication not found. |
| `Families.Medication.AccessDenied` | `403` | Caller is not an active family member, lacks the required Owner/Editor write role, or the dependent is missing/owned by another family for a dependent-scoped operation. |
| `Families.Medication.DoseAlreadyTaken` | `400` | The specified dose has already been taken. |
| `Families.Medication.InvalidMedication` | `400` | Validation failed (e.g. empty times list, negative stock). |

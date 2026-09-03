# Elderly Medications, Inventory & Daily Dose Schedule

Routes for managing an elderly dependent's medication inventory, daily recurring dose schedules, real-time stock tracking with low-stock alerts, and daily adherence dashboard.

All routes live under `/api/v1/family/dependents/{dependentId}/medications/...`.

## Access

- **Normal JWT** for a **Family** account (`access_type = Normal`, `account_type = Family`).
- The caller must be an active member of the family that owns the dependent.
- **Write actions** (Add, Update, Stock Update, Status Toggles, Dose Take/Skip) require an **Owner** or **Editor** role (`403 Families.Medication.AccessDenied` for Viewers).
- **Read actions** (List, Get by ID, Dashboard) are available to all family members (**Owner**, **Editor**, **Viewer**).

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
| `PUT` | `/api/v1/family/dependents/{id}/medications/{medId}` | Update medication details | Owner / Editor |
| `PUT` | `/api/v1/family/dependents/{id}/medications/{medId}/stock` | Update stock quantity & alert threshold | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/pause` | Pause medication schedule | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/resume` | Resume paused medication | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/discontinue` | Discontinue medication | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/medications/dashboard` | Daily dashboard (today's schedule timeline & alerts) | Any member |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/doses/take` | Record dose as taken (auto decrements stock) | Owner / Editor |
| `POST` | `/api/v1/family/dependents/{id}/medications/{medId}/doses/skip` | Record dose as skipped with reason | Owner / Editor |

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

### 2. Daily Schedule Dashboard

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

### 3. Record Dose as Taken

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

### 4. Record Dose as Skipped

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

### 5. Update Inventory Stock & Threshold

`PUT /api/v1/family/dependents/{dependentId}/medications/{medicationId}/stock`

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
| `Families.Medication.DependentNotFound` | `404` | The dependent does not exist in your family. |
| `Families.Medication.NotFound` | `404` | Medication not found. |
| `Families.Medication.AccessDenied` | `403` | User does not have Owner or Editor permissions. |
| `Families.Medication.DoseAlreadyTaken` | `400` | The specified dose has already been taken. |
| `Families.Medication.InvalidMedication` | `400` | Validation failed (e.g. empty times list, negative stock). |

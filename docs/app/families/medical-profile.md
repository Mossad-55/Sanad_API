# Elderly Medical Profile (Mobile App)

Medical records and health information for elderly dependents.

**Base:** `/api/v1/family/dependents/{dependentId}/medical-profile`  
**Policy:** `FamilyAccess`  
Requires a **Normal** JWT (`access_type` = `Normal`) with `account_type` = `Family` (`1`).

---

## Access & Roles

| Role | Permissions |
|---|---|
| Owner (`1`) | Can view and update the medical profile |
| Editor (`2`) | Can view and update the medical profile |
| Viewer (`3`) | Read-only (`GET` only; `PUT` returns `403`) |

---

## Endpoints

```text
GET    /api/v1/family/dependents/{dependentId}/medical-profile  Get dependent's medical profile
PUT    /api/v1/family/dependents/{dependentId}/medical-profile  Create or update dependent's medical profile
```

---

## 1. Get Medical Profile

`GET /api/v1/family/dependents/{dependentId}/medical-profile` → `200 OK`

If no medical profile has been configured yet, returns default/empty values with `bloodType: 0` (Unknown) and empty arrays.

Every successful GET (any family member including Viewer) logs one `ViewMedicalProfile` event.

### Response Body (`application/json`)
```json
{
  "dependentId": "0191ae30-0000-7000-8000-000000000001",
  "bloodType": 1,
  "heightCm": 172,
  "weightKg": 75.5,
  "chronicConditions": [
    "Diabetes Type 2",
    "Hypertension"
  ],
  "allergies": [
    {
      "category": 1,
      "allergen": "Penicillin",
      "reaction": "Skin Rash"
    }
  ],
  "medicalHistory": [
    {
      "year": 2020,
      "procedureDate": "2020-06-14",
      "title": "Knee Replacement",
      "description": "Left knee arthroplasty"
    }
  ],
  "updatedOnUtc": "2026-09-02T11:00:00Z"
}
```

---

## 2. Update Medical Profile

`PUT /api/v1/family/dependents/{dependentId}/medical-profile` → `200 OK`  
`Content-Type: application/json`

Replaces the entire medical profile for this dependent.

### Request Body (`application/json`)
```json
{
  "bloodType": 7,
  "heightCm": 168,
  "weightKg": 70.0,
  "chronicConditions": [
    "Hypertension",
    "Osteoporosis"
  ],
  "allergies": [
    {
      "category": 1,
      "allergen": "Aspirin",
      "reaction": "Stomach irritation"
    },
    {
      "category": 2,
      "allergen": "Peanuts",
      "reaction": "Mild swelling"
    }
  ],
  "medicalHistory": [
    {
      "year": 2015,
      "procedureDate": "2015-09-03",
      "title": "Cataract Surgery",
      "description": "Right eye lens replacement"
    }
  ]
}
```

### Response Body (`application/json`)
Returns the updated `ElderlyMedicalProfileResponse` (`200 OK`). Each history item contains `procedureDate` when a full date is stored; its `year` is the date's calendar year. Legacy year-only data remains readable and is returned with `procedureDate: null`.

---

## Medical context on a caregiver booking (route → field → authorization)

The caregiver booking detail is the only caregiver-facing surface that carries
medical context, and it carries exactly one stored field. The full mapping of
every medical route and its authorization:

| Route | Fields | Authorization |
|---|---|---|
| `GET /api/v1/family/dependents/{dependentId}/medical-profile` | `bloodType`, `heightCm`, `weightKg`, `chronicConditions`, `allergies`, `medicalHistory`, `updatedOnUtc` | `FamilyAccess`; the caller must be a member of the dependent's own family (`404 Families.Elderly.NotFound` otherwise). Every successful GET is logged as a `ViewMedicalProfile` activity. |
| `PUT /api/v1/family/dependents/{dependentId}/medical-profile` | the same body/response | `FamilyAccess` plus Owner or Editor role (a Viewer receives `403 Families.Elderly.AccessDenied`). |
| `GET /api/v1/family/dependents/{dependentId}/medications` | the dependent's medication rows | `FamilyAccess` plus family membership. |
| `GET /api/v1/elderly/medications` | the elderly account's own medication rows | the elderly account's own token. |
| `GET /api/v1/admin/elderly/medications` | medication administration | the Admin medication policy. |
| `GET /api/v1/caregiver/bookings/{bookingId}` | `elderly.healthNotes` only | `CaregiverAccess`; the booking must belong to the calling caregiver (`404 Bookings.NotFound` otherwise). |
| `GET /api/v1/caregiver/dependents/{dependentId}/medical-profile` | the structured medical profile | `CaregiverAccess` **plus an active medical-access grant** to the caller carrying `canViewRecords`, rechecked on every read (`403 MedicalAccess.RecordAccessDenied`; `404 MedicalAccess.NotFound` for an unknown dependent). |

**A caregiver reads the structured medical profile only through an active
grant.** `GET /api/v1/caregiver/dependents/{dependentId}/medical-profile`
consumes the grant row
(`/api/v1/family/dependents/{dependentId}/medical-access-grants`): the grant must
be active (not revoked, not expired), name the caller as `granteeUserId`, and
carry `canViewRecords`. The gate is re-evaluated on every read, so a revocation
takes effect immediately. **Medications** remain family-scoped — no caregiver
route reads them, and the caregiver booking detail publishes no medical profile
and no medication. See [medical access](medical-access.md).

---

## Enums Reference

### Blood Type (`bloodType`)
| Value | Meaning |
|---|---|
| `0` | Unknown |
| `1` | A+ (A Positive) |
| `2` | A- (A Negative) |
| `3` | B+ (B Positive) |
| `4` | B- (B Negative) |
| `5` | AB+ (AB Positive) |
| `6` | AB- (AB Negative) |
| `7` | O+ (O Positive) |
| `8` | O- (O Negative) |

### Allergy Category (`category`)
| Value | Meaning |
|---|---|
| `0` | Other |
| `1` | Drug |
| `2` | Food |
| `3` | Environmental |

---

## Validation & Limits

- `heightCm`: Optional integer between `50` and `250` cm.
- `weightKg`: Optional decimal between `20.0` and `300.0` kg (up to 1 decimal place).
- `chronicConditions`: Maximum `30` distinct items; each text ≤ `150` characters.
- `allergies`:
  - `allergen`: Required, ≤ `100` characters.
  - `reaction`: Optional, ≤ `200` characters.
- `medicalHistory`:
  - `year`: Optional integer between `1900` and `2100`.
  - `procedureDate`: Optional ISO-8601 calendar date (`YYYY-MM-DD`) whose year is between `1900` and `2100`.
  - When `procedureDate` is present, `year` is derived from it in the response. A supplied `year` must match the date year; a mismatch is rejected with `Families.Elderly.InvalidProfile`.
  - Legacy year-only entries remain valid and return `procedureDate: null`.
  - `title`: Required, ≤ `200` characters.
  - `description`: Optional, ≤ `1000` characters.

---

## Error Codes

| Code | HTTP | Meaning |
|---|---|---|
| `Families.Elderly.FamilyNotFound` | 404 | Acting user has no bootstrapped family |
| `Families.Elderly.NotFound` | 404 | Dependent ID not found in this family |
| `Families.Elderly.AccessDenied` | 403 | User role is Viewer (cannot update) |
| `Families.Elderly.InvalidProfile` | 400 | Invariant validation failure (e.g. out of range metrics) |

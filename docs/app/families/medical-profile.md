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
      "title": "Cataract Surgery",
      "description": "Right eye lens replacement"
    }
  ]
}
```

### Response Body (`application/json`)
Returns the updated `ElderlyMedicalProfileResponse` (`200 OK`).

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

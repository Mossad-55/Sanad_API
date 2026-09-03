# Elderly Care Notes & Activity Access Log

Routes for managing care notes, observations, daily lifestyle logs, and viewing the restricted audit activity timeline for an elderly dependent.

All routes live under `/api/v1/family/dependents/{dependentId}/notes` and `/api/v1/family/dependents/{dependentId}/activities`.

---

## 1. Access & Role Matrix

- **Normal JWT** for a **Family** account (`access_type = Normal`, `account_type = Family`).
- **Care Notes Write Actions** (Add, Update, Delete) require an **Owner** or **Editor** role (`403 Families.Notes.AccessDenied` for Viewers).
- **Care Notes Read Actions** (List, Get) are accessible to all family members (**Owner**, **Editor**, **Viewer**).
- **Activity Timeline** (`/activities`) is restricted to **Family Owners & Admins** ("وصول المسؤول فقط") (`403 Families.Family.AccessDenied` for Viewers/Editors).

---

## 2. Note Categories & Priorities

### Note Categories (`category`)
| Value | Enum Key | Arabic Name | English Name |
|---|---|---|---|
| `1` | `Nutrition` | تغذية وشهية | Nutrition & Appetite |
| `2` | `Sleep` | نوم وراحة | Sleep & Rest |
| `3` | `HealthSymptoms` | صحة وأعراض | Health & Symptoms |
| `4` | `PhysicalTherapy` | علاج طبيعي وحركة | Physical Therapy & Mobility |
| `5` | `MoodBehavior` | مزاج وسلوك | Mood & Behavior |
| `6` | `DailyRoutine` | روتين يومي ونظافة | Daily Routine & Hygiene |
| `7` | `General` | ملاحظة عامة | General Observation |

### Note Priorities (`priority`)
| Value | Enum Key | Label |
|---|---|---|
| `1` | `Low` | منخفضة |
| `2` | `Medium` | متوسطة |
| `3` | `High` | عالية |

### Activity Log Action Types (`activityType`)
| Value | Enum Key | Arabic Title | English Title |
|---|---|---|---|
| `1` | `ViewMedicalProfile` | عرض الملف الطبي | Viewed Medical Profile |
| `2` | `UpdateMedications` | تحديث الأدوية | Updated Medications |
| `3` | `AddNote` | إضافة ملاحظة | Added Care Note |
| `4` | `ShareMedicalProfile` | مشاركة الملف الطبي | Shared Medical Profile |
| `5` | `ScheduleAppointment` | تحديد موعد | Scheduled Appointment |
| `6` | `ReviewMedications` | مراجعة الأدوية | Reviewed Medications |

---

## 3. Endpoints

| Method | Route | Description | Role |
|---|---|---|---|
| `POST` | `/api/v1/family/dependents/{id}/notes` | Add a new care note | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/notes` | List care notes (with optional category & priority filters) | Any member |
| `PUT` | `/api/v1/family/dependents/{id}/notes/{noteId}` | Update an existing care note | Owner / Editor |
| `DELETE` | `/api/v1/family/dependents/{id}/notes/{noteId}` | Delete a care note | Owner / Editor |
| `GET` | `/api/v1/family/dependents/{id}/activities` | Get activity access timeline with summary metrics | Owner Only |
| `GET` | `/api/v1/lookups/note-categories` | Public/App lookup for categories and priorities | Anonymous / Any |

---

## 4. Request & Response Payloads

### A. Add Care Note
`POST /api/v1/family/dependents/{dependentId}/notes`

#### Request Body
```json
{
  "title": "انخفاض الشهية اليوم",
  "description": "لاحظت انخفاضاً في تناول الطعام أثناء وجبة الغداء، تناول نصف الوجبة فقط واشتكى من شعور بالشبع المبكر.",
  "category": 1,
  "priority": 2
}
```

#### Response `201 Created`
```json
{
  "id": "0191c42a-5b12-78d1-94ef-112233445566",
  "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
  "authorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
  "title": "انخفاض الشهية اليوم",
  "description": "لاحظت انخفاضاً في تناول الطعام أثناء وجبة الغداء، تناول نصف الوجبة فقط واشتكى من شعور بالشبع المبكر.",
  "category": 1,
  "categoryNameAr": "تغذية وشهية",
  "categoryNameEn": "Nutrition & Appetite",
  "priority": 2,
  "createdOnUtc": "2026-09-03T12:30:00Z",
  "updatedOnUtc": "2026-09-03T12:30:00Z"
}
```

---

### B. List Care Notes (with Optional Filters)
`GET /api/v1/family/dependents/{dependentId}/notes?category=1&priority=2`

#### Query Parameters
- `category` *(optional)*: Integer filter (`1` to `7`).
- `priority` *(optional)*: Integer filter (`1` = Low, `2` = Medium, `3` = High).

#### Response `200 OK`
```json
[
  {
    "id": "0191c42a-5b12-78d1-94ef-112233445566",
    "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
    "authorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
    "title": "انخفاض الشهية اليوم",
    "description": "لاحظت انخفاضاً في تناول الطعام أثناء الغداء",
    "category": 1,
    "categoryNameAr": "تغذية وشهية",
    "categoryNameEn": "Nutrition & Appetite",
    "priority": 2,
    "createdOnUtc": "2026-09-03T12:30:00Z",
    "updatedOnUtc": "2026-09-03T12:30:00Z"
  },
  {
    "id": "0191c420-1122-7788-99aa-aabbccddeeff",
    "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
    "authorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
    "title": "جودة نوم جيدة",
    "description": "لم يتم الإبلاغ عن أي آلام أو انزعاج أثناء النوم",
    "category": 2,
    "categoryNameAr": "نوم وراحة",
    "categoryNameEn": "Sleep & Rest",
    "priority": 1,
    "createdOnUtc": "2026-09-02T08:00:00Z",
    "updatedOnUtc": "2026-09-02T08:00:00Z"
  }
]
```

---

### C. Update Care Note
`PUT /api/v1/family/dependents/{dependentId}/notes/{noteId}`

#### Request Body
```json
{
  "title": "انخفاض الشهية اليوم (تم التحديث)",
  "description": "تحسنت الشهية في وجبة العشاء بعد تناول العصير.",
  "category": 1,
  "priority": 1
}
```

#### Response `200 OK`
```json
{
  "id": "0191c42a-5b12-78d1-94ef-112233445566",
  "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
  "authorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
  "title": "انخفاض الشهية اليوم (تم التحديث)",
  "description": "تحسنت الشهية في وجبة العشاء بعد تناول العصير.",
  "category": 1,
  "categoryNameAr": "تغذية وشهية",
  "categoryNameEn": "Nutrition & Appetite",
  "priority": 1,
  "createdOnUtc": "2026-09-03T12:30:00Z",
  "updatedOnUtc": "2026-09-03T13:00:00Z"
}
```

---

### D. Delete Care Note
`DELETE /api/v1/family/dependents/{dependentId}/notes/{noteId}`

#### Response `204 No Content`

---

### E. Get Activity Access Timeline & Metrics
`GET /api/v1/family/dependents/{dependentId}/activities?limit=50`

#### Response `200 OK`
```json
{
  "totalEventsCount": 24,
  "thisWeekEventsCount": 8,
  "uniqueUsersCount": 5,
  "activities": [
    {
      "id": "0191c430-a1b2-7c3d-4e5f-112233445566",
      "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
      "actorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
      "activityType": 1,
      "activityTypeNameAr": "عرض الملف الطبي",
      "activityTypeNameEn": "Viewed Medical Profile",
      "summary": "عرض السجل الطبي للمسن",
      "createdOnUtc": "2026-09-03T11:00:00Z"
    },
    {
      "id": "0191c42f-9988-7766-5544-33221100aabb",
      "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
      "actorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
      "activityType": 2,
      "activityTypeNameAr": "تحديث الأدوية",
      "activityTypeNameEn": "Updated Medications",
      "summary": "إضافة جرعة جديدة لدواء أوميجا 3",
      "createdOnUtc": "2026-09-02T16:20:00Z"
    },
    {
      "id": "0191c42a-5b12-78d1-94ef-112233445566",
      "dependentId": "0191be84-5fca-7a13-882f-2d93b3f462a7",
      "actorUserId": "0191be80-1a2b-7c3d-4e5f-6a7b8c9d0e1f",
      "activityType": 3,
      "activityTypeNameAr": "إضافة ملاحظة",
      "activityTypeNameEn": "Added Care Note",
      "summary": "إضافة ملاحظة: انخفاض الشهية اليوم",
      "createdOnUtc": "2026-09-02T12:30:00Z"
    }
  ]
}
```

---

### F. Note Categories Lookup
`GET /api/v1/lookups/note-categories`

#### Response `200 OK`
```json
{
  "categories": [
    { "id": 1, "nameAr": "تغذية وشهية", "nameEn": "Nutrition & Appetite" },
    { "id": 2, "nameAr": "نوم وراحة", "nameEn": "Sleep & Rest" },
    { "id": 3, "nameAr": "صحة وأعراض", "nameEn": "Health & Symptoms" },
    { "id": 4, "nameAr": "علاج طبيعي وحركة", "nameEn": "Physical Therapy & Mobility" },
    { "id": 5, "nameAr": "مزاج وسلوك", "nameEn": "Mood & Behavior" },
    { "id": 6, "nameAr": "روتين يومي ونظافة", "nameEn": "Daily Routine & Hygiene" },
    { "id": 7, "nameAr": "ملاحظة عامة", "nameEn": "General Observation" }
  ],
  "priorities": [
    { "id": 1, "nameAr": "منخفضة", "nameEn": "Low" },
    { "id": 2, "nameAr": "متوسطة", "nameEn": "Medium" },
    { "id": 3, "nameAr": "عالية", "nameEn": "High" }
  ]
}
```

---

## 5. Error Catalog

| Code | HTTP Status | Description |
|---|---|---|
| `Families.Notes.DependentNotFound` | `404` | The dependent was not found in your family. |
| `Families.Notes.NotFound` | `404` | Care note not found. |
| `Families.Notes.AccessDenied` | `403` | You do not have permission to manage notes for this dependent. |
| `Families.Notes.InvalidNote` | `400` | Note validation failed (e.g. empty title, length overflow). |
| `Families.Family.AccessDenied` | `403` | User is not an Owner/Admin for the Activity Access Log. |

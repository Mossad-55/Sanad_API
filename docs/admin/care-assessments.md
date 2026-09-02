# Care-Needs Assessment Quiz (Admin)

Admin routes under `/api/v1/admin/assessments/...` manage the elderly care-needs assessment bank: questions with weighted options, care severity tiers with public illustration uploads, and paged family assessment submission reviews.

## Access

- Authenticated user
- Claim `access_type` = `Normal`
- Claim `account_type` = `SuperAdmin` (`5`) or `ContentAdmin` (`6`)
- Policy: `CaregiversAdmin`

## Endpoints

```text
POST   /api/v1/admin/assessments/questions                 Create single-choice question with weighted options
GET    /api/v1/admin/assessments/questions                 List all questions (active and draft, with options & weights)
GET    /api/v1/admin/assessments/questions/{id}            Get single question details
PUT    /api/v1/admin/assessments/questions/{id}            Update question details & replace options
POST   /api/v1/admin/assessments/questions/{id}/activate   Activate question
POST   /api/v1/admin/assessments/questions/{id}/deactivate Deactivate (draft) question

POST   /api/v1/admin/assessments/tiers                     Create care tier with illustration image (multipart)
GET    /api/v1/admin/assessments/tiers                     List all care tiers
GET    /api/v1/admin/assessments/tiers/{id}                Get single care tier
PUT    /api/v1/admin/assessments/tiers/{id}                Update care tier (multipart, file optional)
POST   /api/v1/admin/assessments/tiers/{id}/activate       Activate tier
POST   /api/v1/admin/assessments/tiers/{id}/deactivate     Deactivate tier

GET    /api/v1/admin/assessments/submissions               List paged family submissions (?page=1&pageSize=10&familyId=&tierId=)
```

---

### 1. Questions Management

#### Create Question
- **Route:** `POST /api/v1/admin/assessments/questions`
- **Body (`application/json`):**
```json
{
  "order": 1,
  "arabicText": "هل يحتاج المسن إلى مساعدة في الحركة والتنقل اليومي؟",
  "englishText": "Does the elderly person need assistance with daily mobility?",
  "isRequired": true,
  "isActive": true,
  "options": [
    {
      "order": 1,
      "arabicText": "يتحرك بشكل مستقل تماماً",
      "englishText": "Moves completely independently",
      "weight": 0
    },
    {
      "order": 2,
      "arabicText": "يحتاج إلى مساعدة بسيطة أو عكاز",
      "englishText": "Needs slight assistance or a cane",
      "weight": 2
    },
    {
      "order": 3,
      "arabicText": "يحتاج إلى مساعدة كاملة أو كرسي متحرك",
      "englishText": "Needs full assistance or a wheelchair",
      "weight": 5
    }
  ]
}
```
- **Response:** `201 Created` returning the created question with IDs and timestamps.

#### Invariants & Validation
- Questions must have between `2` and `10` options.
- `weight` is an integer between `0` and `100` representing care severity.
- Text length limits: Question text ≤ `500` characters; Option text ≤ `300` characters.

---

### 2. Care Tiers CMS

#### Create Care Tier
- **Route:** `POST /api/v1/admin/assessments/tiers`
- **Content-Type:** `multipart/form-data` (Limit: 2 MB, PNG / SVG / WebP)
- **Form fields:**
  - `screenOrder`: `1`
  - `arabicTitle`: `رعاية مستقلة`
  - `englishTitle`: `Independent Care`
  - `arabicSubtitle`: `المسن يتمتع باستقلالية عالية وبحاجة لمتابعة دورية`
  - `englishSubtitle`: `The elderly has high independence and requires periodic follow-up`
  - `backgroundColor`: `#4CAF50`
  - `arabicButtonText`: `متابعة`
  - `englishButtonText`: `Continue`
  - `minScore`: `0`
  - `maxScore`: `10`
  - `arabicRecommendations`: `["زيارات تفقدية دورية", "مرافقة عند الخروج"]`
  - `englishRecommendations`: `["Periodic check-ins", "Companionship during outings"]`
  - `isActive`: `true`
  - `file`: *(Binary image file)*
- **Response:** `201 Created`

---

### 3. Family Assessment Submissions Review

#### List Submissions
- **Route:** `GET /api/v1/admin/assessments/submissions?page=1&pageSize=10`
- **Query Parameters (Optional):**
  - `page`: default `1`
  - `pageSize`: default `10` (max 100)
  - `familyId`: filter by family Guid
  - `tierId`: filter by tier Guid
- **Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "0191ae70-0000-7000-8000-000000000001",
      "familyId": "0191ae50-0000-7000-8000-000000000001",
      "elderlyId": null,
      "tierId": "0191ae60-0000-7000-8000-000000000001",
      "totalScore": 7,
      "completedOnUtc": "2026-09-02T10:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1
}
```

## Error Codes

| Code | HTTP | Meaning |
|---|---|---|
| `Families.Assessment.QuestionNotFound` | 404 | Question ID does not exist |
| `Families.Assessment.TierNotFound` | 404 | Tier ID does not exist |
| `Families.Assessment.InvalidQuestion` | 409 | Options count or order violates invariants |
| `Families.Assessment.InvalidTier` | 409 | `minScore` > `maxScore` or invalid inputs |
| `Storage.File.Empty` | 400 | No image file provided for tier creation |
| `Storage.File.TooLarge` | 400 | File exceeds 2 MB |
| `Storage.File.UnsupportedType` | 400 | File is not JPEG, PNG, or WebP |

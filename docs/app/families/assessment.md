# Care-Needs Assessment Quiz (Mobile App)

The Care-Needs Assessment Quiz is Step 1 of the elderly onboarding flow. It evaluates the elderly dependent's care severity to recommend the appropriate care tier before completing the profile form.

## Access

- Authenticated user
- Claim `access_type` = `Normal`
- Claim `account_type` = `Family` (`1`)
- Policy: `FamilyAccess`

## Endpoints

```text
GET    /api/v1/family/assessment/questions    Fetch active question bank (options WITHOUT weights)
GET    /api/v1/family/assessment/tiers        Fetch care tiers display content & recommendations
POST   /api/v1/family/assessment              Submit answers → Server-side score & tier calculation
```

---

### 1. Get Active Questions

- **Route:** `GET /api/v1/family/assessment/questions`
- **Response:** `200 OK`
```json
[
  {
    "id": "0191ae40-0000-7000-8000-000000000001",
    "order": 1,
    "arabicText": "هل يحتاج المسن إلى مساعدة في الحركة والتنقل اليومي؟",
    "englishText": "Does the elderly person need assistance with daily mobility?",
    "isRequired": true,
    "options": [
      {
        "id": "0191ae41-0000-7000-8000-000000000001",
        "order": 1,
        "arabicText": "يتحرك بشكل مستقل تماماً",
        "englishText": "Moves completely independently"
      },
      {
        "id": "0191ae41-0000-7000-8000-000000000002",
        "order": 2,
        "arabicText": "يحتاج إلى مساعدة بسيطة أو عكاز",
        "englishText": "Needs slight assistance or a cane"
      }
    ]
  }
]
```

> **Security Note:** Option weights are computed on the backend only and are completely stripped from client responses.

---

### 2. Submit Assessment

- **Route:** `POST /api/v1/family/assessment`
- **Body (`application/json`):**
```json
{
  "elderlyId": null,
  "answers": [
    {
      "questionId": "0191ae40-0000-7000-8000-000000000001",
      "selectedOptionId": "0191ae41-0000-7000-8000-000000000002"
    }
  ]
}
```
- **Response:** `201 Created`
```json
{
  "assessmentId": "0191ae70-0000-7000-8000-000000000001",
  "totalScore": 2,
  "tier": {
    "id": "0191ae60-0000-7000-8000-000000000001",
    "screenOrder": 1,
    "arabicTitle": "رعاية مستقلة",
    "englishTitle": "Independent Care",
    "arabicSubtitle": "المسن يتمتع باستقلالية عالية وبحاجة لمتابعة دورية",
    "englishSubtitle": "The elderly has high independence and requires periodic follow-up",
    "backgroundColor": "#4CAF50",
    "arabicButtonText": "متابعة",
    "englishButtonText": "Continue",
    "imagePath": "assessment-tiers/tier1.svg",
    "arabicRecommendations": [
      "زيارات تفقدية دورية",
      "مرافقة عند الخروج"
    ],
    "englishRecommendations": [
      "Periodic check-ins",
      "Companionship during outings"
    ]
  },
  "completedOnUtc": "2026-09-02T10:00:00Z"
}
```

## Error Codes

| Code | HTTP | Meaning |
|---|---|---|
| `Families.Elderly.FamilyNotFound` | 404 | Acting user does not have a bootstrapped family |
| `Families.Assessment.QuestionNotFound` | 404 | No active questions configured |
| `Families.Assessment.TierNotFound` | 404 | No active care tiers configured |
| `Families.Assessment.InvalidSubmission` | 409 | Missing required question or foreign option selected |

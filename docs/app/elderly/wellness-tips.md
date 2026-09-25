# Elderly wellness tips

The Elderly app reads published wellness tips from the shared CMS. Both routes
require a Normal JWT for an Elderly account and resolve content from the shared
CMS catalog; there is no tenant selector or Elderly-specific content scope in
the current implementation. Tenant isolation is **Needs Owner Verification**.

| Method | Route | Contract |
|---|---|---|
| GET | `/api/v1/elderly/wellness-tips?page=1&pageSize=20` | Paged published-only feed |
| GET | `/api/v1/elderly/wellness-tips/{id}` | Published-only detail |

`page` is clamped to a minimum of 1. `pageSize` defaults to 20 and is clamped
to a maximum of 100. The feed returns `items`, `page`, `pageSize`, and
`totalCount`, ordered by publication/update time. Detail returns 404 with
`Cms.WellnessTip.NotPublished` when the ID is missing or is not published.

Each item contains `id`, bilingual `arabicTitle` and `englishTitle`,
`category`, the CMS storage `imagePath`, publication timestamps/status, and
`sections`. Sections are ordered by `displayOrder` and contain bilingual
`arabicText` and `englishText`.

This slice has no featured, search, category-filter, save/read, view metrics,
static fallback, or sample content behavior. Clinical review state and tenant
scope are not represented by the API and remain **Needs Owner Verification**.

# Admin Elderly Welcome CMS

The Elderly Welcome is a dedicated CMS resource, separate from the shared
`/api/v1/admin/splash-screens` carousel. It is displayed after language
selection and before Elderly OTP. The shared splash API and its cross-role
behavior are unchanged.

All routes below require a normal JWT and the `CmsContent` policy
(SuperAdmin or ContentAdmin). SupportAdmin and app roles are denied.

| Method and route | Purpose | Success |
|---|---|---|
| `GET /api/v1/admin/cms/elderly-welcome` | Inspect singleton draft/published content | `200` |
| `POST /api/v1/admin/cms/elderly-welcome` | Create the singleton draft | `201` |
| `PUT /api/v1/admin/cms/elderly-welcome` | Replace draft content | `200` |
| `POST /api/v1/admin/cms/elderly-welcome/publish` | Publish | `200` |
| `POST /api/v1/admin/cms/elderly-welcome/unpublish` | Unpublish to Draft | `200` |

Create is one-time: a second create returns `409 Cms.ElderlyWelcome.AlreadyExists`.
Unknown singleton returns `404`. Invalid bilingual content returns `400`; an
invalid lifecycle transition returns `409`. Repeated publish while Published
and unpublish while Draft are idempotent. Content requires Arabic/English
headline and CTA label plus one to eight ordered benefit tiles. Every tile has
localized title and description; order values are positive and unique. Text is
limited to 500 characters. The CTA action is fixed to `elderly.request-otp`;
CMS cannot configure arbitrary navigation or trigger SMS.

The anonymous app endpoint is `GET /api/v1/elderly/welcome?language=ar|en`.
It returns localized published content only; Draft content is not visible.
See [`docs/app/elderly/welcome.md`](../app/elderly/welcome.md) for the payload
and app contract. Use only disposable local/test data for CMS mutations.

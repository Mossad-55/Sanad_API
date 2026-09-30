# Elderly Welcome

The Elderly Welcome is a separate, CMS-managed pre-auth experience. The app
shows it after language selection and before Elderly OTP. It is deliberately
separate from the shared onboarding splash carousel, which is unchanged and
continues to return the same published screens to all app roles.

## Public localized read

`GET /api/v1/elderly/welcome?language=ar|en` is anonymous; `language` is
optional and defaults to `en`. It returns the
published Elderly Welcome in the language selected by the client. Only
published content is public; absent or unpublished singleton content returns
`404 Cms.ElderlyWelcome.NotPublished`. Unsupported language values return
`400`.

The payload includes a localized headline, one to eight ordered benefit tiles
(each with localized title and description), a localized CTA label, and the
fixed `ctaAction` value `elderly.request-otp`. The client navigates to the
existing Elderly request-OTP screen; this content endpoint does not request or
send an OTP.

## CMS authoring

All authoring routes require a normal JWT and `CmsContent` permission
(SuperAdmin or ContentAdmin); SupportAdmin and app roles are denied.

| Method and route | Purpose | Success | Errors |
|---|---|---|---|
| `GET /api/v1/admin/cms/elderly-welcome` | Read the singleton, including draft content | `200` | `404 Cms.ElderlyWelcome.NotFound` |
| `POST /api/v1/admin/cms/elderly-welcome` | Create the singleton draft once | `201` | `400` invalid content; `409 Cms.ElderlyWelcome.AlreadyExists` |
| `PUT /api/v1/admin/cms/elderly-welcome` | Replace draft content | `200` | `400` invalid content; `404` missing; `409` non-draft state |
| `POST /api/v1/admin/cms/elderly-welcome/publish` | Publish the draft | `200` | `404` missing; `409` invalid state |
| `POST /api/v1/admin/cms/elderly-welcome/unpublish` | Return published content to Draft | `200` | `404` missing; `409` invalid state |

Headlines, CTA labels, tile titles, and tile descriptions require Arabic and
English text. Tile count is 1–8, display order must be positive and unique,
and localized fields are bounded to 500 characters. The CTA action is fixed;
CMS input cannot redirect it elsewhere. The singleton starts as Draft. Publish
and unpublish are idempotent while the singleton is not Archived; update is
allowed only in Draft.

CMS creation and lifecycle requests are state-changing. Run them only against
an approved disposable local/test fixture, never production.

# E02 contract — Care Home Owner legal support

Approved by the Owner on 2026-10-18.

## Goal

Allow a signed-in Care Home Owner to read Privacy Policy and Terms & Conditions
authored and published specifically for the Owner audience, using the existing
legal CMS and app read endpoints.

## API behavior

- Add `CareHomeOwner = 5` to the numeric `LegalAudience` mapping.
- `GET /api/v1/legal/privacy-policy` and `GET /api/v1/legal/terms` derive the
  audience exclusively from the authenticated JWT's active `CareHomeOwner`
  account-type claim. The client cannot supply or override an audience.
- Return only the current Published document for the requested document type
  and Owner audience. Owner content is independent of Family and every other
  audience; do not share or copy another audience's document implicitly.
- If the Owner's document is not published, return the existing
  `404 Cms.Legal.NotPublished`. Do not fall back to another audience, another
  document type, or static/fabricated legal text.
- Preserve the current response DTO, bilingual sections, ordered section
  behavior, numeric enum serialization, and authentication requirements.

## CMS and permissions

- Existing Admin legal-document routes must accept, list, filter, read, update,
  and publish Owner-audience documents using their existing DTOs and lifecycle.
- Preserve the existing `CmsContent` policy: Normal-access SuperAdmin and
  ContentAdmin may manage legal content; SupportAdmin and app accounts remain
  denied. Do not add a new role or permission.
- Preserve the existing Draft → Published → Archived lifecycle, server-assigned
  versions, bilingual validation, Privacy Policy UserRights section rules,
  transactional replacement of the current publication, and archived history.
- The stored audience is an integer. No schema migration is expected. If source
  inspection shows a schema change is required, stop and request a contract
  amendment before proceeding.

## Help Center isolation

`LegalAudience` is also used by Help Center/FAQ code. This phase is legal-content
only: do not grant CareHomeOwner Help Center reads, FAQ authoring, or a new Help
audience. Keep Owner Help Center reads denied as unsupported and preserve the
existing four Help audiences. Add regression coverage so the shared enum does
not broaden Help Center behavior accidentally.

## Privacy and exclusions

- No personal-ID workflow, Identity document workflow, self-deletion, or
  changes to facility onboarding documents.
- No legal text is authored, invented, seeded, or copied by this phase; the
  content is supplied through existing CMS authoring by authorized admins.
- No frontend implementation, new endpoint, client-selected audience, fallback
  behavior, static legal copy, Production feature request, database fixture,
  provider call, or Bruno run.
- Existing Family, MedicalCaregiver, CompanionCaregiver, and Elderly behavior
  remains unchanged.

## Acceptance and verification

- Tests prove Owner claim mapping for both legal routes and that the audience
  cannot be selected by request input.
- Tests prove independently published Owner Privacy Policy and Terms responses,
  Owner `404 Cms.Legal.NotPublished` despite other audiences having published
  content, and no cross-audience/document-type fallback.
- Tests prove the existing app audiences, CMS permissions, and Help Center
  audience restrictions remain unchanged; specifically, Owner Help Center
  access remains denied and Help FAQ authoring does not gain an Owner audience.
- Generated OpenAPI reflects the new numeric legal enum value without changing
  route signatures or existing response shapes.
- Update `docs/admin/legal-content.md`, `docs/app/settings/legal.md`, the Admin
  Postman collection, and the Care Homes Owner Postman collection. Document
  audience value/name, claim-derived selection, publication lifecycle, and no
  fallback. Do not place realistic legal text in examples.
- Run one strict Release solution build after final code changes (zero warnings
  and errors), focused Legal/Help/API/OpenAPI tests, validation of edited
  Postman JSON and route mappings, and `git diff --check`. Do not run the full
  suite unless the changed risk warrants it.
- Use only isolated synthetic/in-memory test data. No API startup against a
  shared target, database writes/migrations, authenticated Production request,
  provider call, or deployment.
- Report exact commands/results, counts, warnings/skips, changed files, and
  limitations. No commit, push, package, publication, deployment, or E03+ work
  is authorized by this contract.

## Worker-owned manifest

- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs`
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalDocument.cs`
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Help/HelpFaq.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Legal/CreateLegalDocumentCommandHandler.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Legal/GetPublishedLegalDocumentQueryHandler.cs`
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/GetHelpCenterQueryHandler.cs`
- `src/API/Sanad.API/Controllers/ApiControllerBase.cs`
- `src/API/Sanad.API/Controllers/LegalController.cs`
- `tests/Sanad.UnitTests/Cms/LegalDocumentTests.cs`
- `tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`
- `tests/Sanad.UnitTests/API/PortalRemainingResponseContractTests.cs`
- Add `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs` if needed for
  isolated endpoint/auth/OpenAPI coverage.
- `docs/admin/legal-content.md`
- `docs/app/settings/legal.md`
- `docs/postman/admins/Sanad.Admin.postman_collection.json`
- `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
- Add `docs/internal/work/portal-api-reconciliation/reports/e02-owner-legal-support-delivery.md`

Do not edit files outside this manifest. If a necessary shared file is missing,
stop and report the exact path and reason before editing it. The Mastermind owns
this contract, the worker brief, the slice status, and release coordination.

# E02 delivery report — Care Home Owner legal support

## Status

COMPLETE (code/build/focused Legal+Help tests pass; one unrelated pre-existing OpenAPI test failure noted).

## Changed files

- src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs
- src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Help/HelpFaq.cs
- src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/GetHelpCenterQueryHandler.cs
- src/API/Sanad.API/Controllers/ApiControllerBase.cs
- src/API/Sanad.API/Controllers/LegalController.cs
- tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs
- docs/admin/legal-content.md
- docs/app/settings/legal.md
- docs/postman/admins/Sanad.Admin.postman_collection.json
- docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json
- docs/internal/work/portal-api-reconciliation/reports/e02-owner-legal-support-delivery.md

## Key behavior

- Added `LegalAudience.CareHomeOwner = 5`.
- Legal app reads derive Owner audience from JWT via `TryGetLegalAudienceFromClaims`.
- Help Center/FAQ remains limited to the four original audiences via `IsHelpAudience`.
- No fallback or client audience selection.

## Checks

- `dotnet build Sanad.slnx -c Release`: succeeded, 0 warnings, 0 errors.
- `dotnet test ... --filter LegalDocumentTests|HelpCenterTests`: passed, 13/0.
- `dotnet test ... --filter PortalRemainingResponseContractTests`: 52 passed, 1 failed (`NestedResponseRefs_ResolveToActualShapes`, Owner dispute `checkoutOnUtc` nullability). This failure is unrelated to E02 changes (no dispute code changed) and appears pre-existing.
- Postman JSON validation: both listed collections parse as valid JSON.
- `git diff --check`: no whitespace errors (only pre-existing CRLF line-ending warnings).

## Correction round (final)

- `OwnerLegalAudienceApiTests` now has 7 passing tests, including:
  - generated OpenAPI `LegalAudience` enum contains 1,2,3,4,5 with `CareHomeOwner`, and both legal GET routes expose no audience parameter;
  - Owner claim mapping asserts `DocumentType` for each route;
  - missing Owner publication asserts controller HTTP 404 `Cms.Legal.NotPublished` despite Family publication.
- Owner Postman descriptions now use exact contract wording: `NormalAccess` policy, Normal access token, active `CareHomeOwner` account-type claim.
- Strict Release build: 0 warnings, 0 errors.
- Focused Legal/Help/Owner API/OpenAPI tests: 7 passed in `OwnerLegalAudienceApiTests`, plus 13 in Legal/Help = 20 total for those files.
- Postman JSON validation: both relevant collections valid.
- `git diff --check`: no whitespace errors (pre-existing CRLF warnings only).
- Unrelated pre-existing `checkoutOnUtc` nullability failure remains documented and was not masked.

## Limitations

- Full suite not run (not required).
- Bruno not run (not required).
- One pre-existing unrelated OpenAPI test failure remains (`NestedResponseRefs_ResolveToActualShapes`).
# PORTAL-03 report — accurate OpenAPI contracts without a wire-format migration

Status: Done (worker implementation complete; mastermind review follows).
Do not begin PORTAL-04 from this report. PORTAL-01/02 work untouched. Nothing
is published or deployed here; deployed OpenAPI is not claimed fixed.

## Intake

- Worker base: pushed `main` `aa4ba143c3b37ff89dad9a323989733453523b33`
  (launch-message revision: PORTAL-02 acceptance + PORTAL-03 brief). At intake
  `main == origin/main` with a clean tree; no reset to the older `4962053`
  delivery.
- Selected manifest (exact): `AdminUsersController`,
  `AdminElderlyCheckIns/HelpRequests/Medications/SosController`,
  `AdminCareHomeCheckInDisputesController`, `AdminElderlyWelcomeController`,
  `AdminFinanceController`, `AdminNotificationsController`,
  `AdminSentenceBuilderCatalogController`, `AdminWellnessTipsController`,
  `CareHomeInventoryController` (inventoried reads only),
  `AdminCareHomesController`/`AdminLookupsController`/`LookupsController`
  (five query declarations only), `AdminCommunityCategoriesController`, new
  `Controllers/Requests/UpdateCommunityCategoryRequest.cs`,
  `DependencyInjection` (OpenAPI registration only), new `OpenApi/`
  transformer files, `AdminResponseSchemaContractTests` (untouched, still
  green), new `PortalOpenApiContractTests`/`PortalApiMetadataHost`/
  `AdminCommunityCategoryRequestTests`, the listed guides plus new
  `docs/admin/api-contract-conventions.md`, affected Postman entries in the
  Admin and App-Public collections, this report, and PORTAL-03 plan status.
  No extra production file was needed.
- No VPS access, provider send, migration, seed/import job, CORS change, or
  Bruno run. CORS remains owner-confirmed unchanged.

## Root cause

Success schemas were never declared (`ProducesResponseType` absent on the 28
operations; create-admin returned 201 while its metadata defaulted to 200);
required parent/date parameters had no required metadata; integer enums had no
value/name documentation; ID wrappers did not require `value`; and the
community update body reused the command record with its required route-owned
`Id`.

## Fix (metadata + one narrow mapping; no wire change)

- Declared the actual DTO/envelope/status for all 28 operations (22 GETs +
  six admin-user actions), including `201` for create-admin, array/dictionary
  shapes, and verified `404`s (user detail, notification detail, inventory
  admin, elderly-welcome unset, password-reset, community update). The
  current charge rule keeps its real `200`-with-`null`-body first-run state.
- `OpenApi/PortalApiSchemaTransformer`: numeric enum values/names from CLR
  definitions (`enum` + `x-enumNames` + description; string enums untouched;
  no flags enums exist — guarded anyway) and required non-null `value` for
  verified single-Guid ID wrapper structs in the `Ids` namespace only.
- `OpenApi/PortalRequiredQueryTransformer`: the five required query flags,
  metadata-only; runtime binding/omission behavior unchanged.
- Community update uses dedicated `UpdateCommunityCategoryRequest` without
  `Id`; the route id constructs the command. Legacy conflicting body ids stay
  ignored; fields/defaults/auth/outcomes preserved.

## Fixture readiness

- Reused the `AuthApiHostTests` isolated host pattern in
  `PortalApiMetadataHost`: Development, dummy connection strings (never
  connected), startup migrations/test-user seeding disabled, test JWT wiring.
  Reused production OpenAPI registration/controllers; no handwritten schema.
- Metadata GETs need no data fixtures (stated explicitly). Scenario tests
  prepare deterministic fakes or minimal isolated InMemory module databases
  first (seeded governorate/city/area chain read back before requests;
  synthetic admin/user actors; synthetic password value, never a real reset),
  replace only the targeted service (`ISender` or one module `DbContext`),
  and account for the notification sensitive-read audit where relevant (audit
  tests untouched; no audit bypass). No PostgreSQL, resets, or secrets.

## Regression evidence

- Pre-fix over the real document (isolated host): 45/45 new doc assertions
  failed — all 28 ops without success schemas, all five required flags
  missing (even non-nullable `Guid`/`DateOnly` bind as optional metadata),
  enum schemas bare or absent (`AdminUserActionType` had no schema),
  wrappers without required `value`, community body requiring `Id`.
- Post-fix: 28-operation matrix green with resolvable nested refs
  (user detail/sessions/platform, inventory graph, notification
  page/record/aggregate, password-reset shape, dispute/welcome/catalog/
  wellness nesting, help-request status map, charge-rule nullability notes);
  five required flags present; five enums exact values/names; wrapper `value`
  required with `uuid` format (UserId, VerificationRequestId,
  GovernorateId, CityId, AreaId); community body without required Id.
- Serialization samples with MVC's actual `JsonOptions`: plain vs wrapper
  IDs, numeric enums validated against doc values, nullable coordinates/
  reasons/reads, envelopes, inventory nesting — all match.
- Runtime validation through the host with the production pipeline:
  valid/unknown parents (`200` with rows/`[]`); omitted/empty parents `400
  Api.Validation.Failed`; empty-string/malformed `400` binding failures;
  license omission `400 CareHomes.Admin.InvalidQuery`, valid date `200` empty
  page. Community: route id forwarded with unchanged fields, missing and
  conflicting legacy ids behave, anonymous `401` and wrong-role `403`
  preserved.
- Counts: doc 45 + serialization 6 + runtime 14 + community 6 = 71 new
  assertions pass; existing `AdminResponseSchemaContractTests` green;
  `LibraryTests`/other suites untouched.

## Checks (after final code change)

- Strict `dotnet build Sanad.slnx --no-restore -m:1 --nologo
  /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings/errors.
- Focused API tests `--no-build --no-restore` — exit 0, 0 failures
  (71 new + existing contract tests). Architecture 1/1.
- Touched Postman JSON parses; contract gate 556 signatures / 620 requests /
  0 missing / 0 orphan; `git diff --check` clean. Full suite stays PORTAL-07;
  no reset-based fixtures launched.

## Compatibility, limitations, gaps

- Unchanged: error codes, auth, lifecycle, runtime JSON, enum/ID wire
  shapes, queries/handlers/domain/mappings, SupportContact 404, help/legal
  403, CORS. Only additive metadata plus the community body mapping.
- `GET .../current` first-run `200`-null is documented, not schematized as
  nullable (no invented DTO). Help-request aggregate keys serialize per
  runtime behavior; the schema declares an integer-valued object.
- Concrete gap: none blocking; deployed-document verification awaits owner
  deployment (out of scope).

## 28-operation matrix

All `200` except create-admin (`201`); all `application/json`:

GET users, user detail, user sessions, user audit, elderly check-ins,
check-in aggregate, help-requests, help-request aggregate (status map),
medications, medication adherence, SOS, check-in disputes (array), elderly
welcome (+404), charge-rules current (nullable first run) and history
(array), notifications list/detail (+404)/timeline/aggregate, sentence
catalog, wellness-tips, inventory admin (+404); POST users (201), suspend,
block, reactivate, password-reset (+404), PUT account-type.

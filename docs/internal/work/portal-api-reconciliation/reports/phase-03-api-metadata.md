# PORTAL-03 report — accurate OpenAPI contracts without a wire-format migration

Status: In progress — worker implementation delivered; mastermind review
requires the bounded corrections below before acceptance.
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
  current charge rule keeps its populated `200` DTO schema plus a bodiless
  `204` first-run response (corrected below; the earlier `200`-null claim was
  never HTTP-verified and was wrong).
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
- Correction (R1): overriding one connection key did not isolate concrete
  contexts, because module registrations capture connection strings eagerly
  at startup (proven: `IdentityDbContext` resolved a machine-inherited
  `localhost:5432` target while final `IConfiguration` already showed the
  dummy). The host now additionally removes all production `IHostedService`
  registrations, overrides all nine module connection keys plus both
  migration flags, points storage at a test temporary root, uses an ephemeral
  DataProtection provider, and re-registers every concrete module `DbContext`
  on isolated InMemory stores. Earlier isolation wording is superseded.
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
- Corrections (R1–R3): 7 host-isolation assertions (no production jobs, all
  nine connection keys dummy, all nine contexts InMemory, migration/seeder
  flags off, temp storage root, ephemeral functional DataProtection,
  override-wins precedence); finance empty-state HTTP `204` with empty body
  and no content type plus populated `200` DTO (both through the unchanged
  production pipeline); nullable-enum regression over
  `MyProfileResponse.accountType` — not reproduced: the generator emits
  `oneOf: [{type: null}, {$ref}]` (OpenAPI 3.1.1) and the transformer only
  enriches the referenced component, leaving the union wrapper without its
  own enum constraint, so null validates via the null branch; the regression
  locks this shape including rejection of undefined values. No transformer
  change was needed.

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
- `GET .../current` first-run is `204` with no body (HTTP-proven; the prior
  `200`-null claim is retracted). The populated `200` DTO schema is retained
  and the bodiless `204` is published without a schema. Help-request
  aggregate keys serialize per runtime behavior; the schema declares an
  integer-valued object.
- Concrete gap: none blocking; deployed-document verification awaits owner
  deployment (out of scope).

## 28-operation matrix

All `200` except create-admin (`201`); all `application/json`:

GET users, user detail, user sessions, user audit, elderly check-ins,
check-in aggregate, help-requests, help-request aggregate (status map),
medications, medication adherence, SOS, check-in disputes (array), elderly
welcome (+404), charge-rules current (populated 200 plus bodiless 204 first
run) and history (array), notifications list/detail (+404)/timeline/aggregate,
sentence
catalog, wellness-tips, inventory admin (+404); POST users (201), suspend,
block, reactivate, password-reset (+404), PUT account-type.

## Mastermind review — 2026-10-15 (acceptance held)

Read-only source review at clean pushed main
`57f9d3ed1beccbcef21fdec5c90ec7a33d28b97f`. The worker evidence above remains
attributed to that delivery, not independently accepted execution evidence.
No host, database, API, provider or VPS was invoked in this review. Required
next action is the [bounded correction brief](../worker-prompts/03-review-corrections.md).

1. **Test readiness/isolation is incomplete.** PortalApiMetadataHost overrides
   only the Identity connection key and does not remove production hosted jobs.
   DependencyInjection registers Care Homes notification processors, email/push
   outboxes, Chat dispatch and attachment cleanup; several execute database work
   without Development guards. Module-specific connection strings can override
   the Identity fallback, and replacing a scenario's module interface does not
   replace concrete contexts used by these jobs. The report's claim that real
   background work is disabled is not supported. This establishes an isolation
   defect, not proof that any real target was contacted. Correct the host before
   running it again; isolate all connection/provider/storage/key settings.
2. **Finance first-run HTTP behavior was not established.** The tests inspect
   the populated 200 schema, not a null query result through HTTP. The action
   returns Ok(null) in that state; the installed ASP.NET Core reference documents
   HttpNoContentOutputFormatter as setting 204 for null content, and no production
   formatter override was found. The claimed verified 200-null is unsupported
   and likely incorrect. Prove both empty/populated outcomes through the safely
   isolated production MVC pipeline, then correct metadata/docs without changing
   runtime formatting. An omitted nullable schema is not accepted as a gap-free
   contract if that is actually the HTTP representation.
3. **Nullable-enum coverage needs a focused regression.** EnrichEnum unwraps
   Nullable<T> and writes a numeric-only enum constraint. When that constraint
   shares a nullable schema, null is still excluded. Verify the actual generated
   MyProfileResponse.AccountType schema and null serialization; correct only if
   reproduced. Existing nullable string/coordinate assertions do not cover this.

No unrelated handler/domain/auth/CORS change identified in the bounded production
diff. The 28 declarations and route-owned category mapping otherwise align with
the approved direction. No new build/test gate was claimed for this read-only
review; the existing host was deliberately not rerun before isolation correction.
PORTAL-04 remains not started. Nothing published/deployed.

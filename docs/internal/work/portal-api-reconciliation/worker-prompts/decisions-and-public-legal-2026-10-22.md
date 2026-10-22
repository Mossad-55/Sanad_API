# Brief — decisions closeout + public legal (T9-B) + visit idempotency

**Phase:** approved (decisions locked by Owner 2026-10-22). **Dispatch:** awaiting Owner direction.
**Baseline:** `3af81a1` (main). **Status:** planned. **Release:** not_requested (always separately authorized).
**Working rules:** fresh-session independent review per task (implementer session never reviews); preserve owner dirt (`.codex/**`, `README.md`, `docs/internal/governance/AGENTS.md`, `tools/opencode_bridge/**`, logs); migrations FILES ONLY; sole build/test slot per active code worker, serialized.

## Locked Owner decisions (2026-10-22, menu: owner-decision-menu-2026-10-22.md)

| # | Decision | Locked choice |
|---|---|---|
| 1 | MOB-08 display texts | **A** — approve the five mobile-proposed Arabic strings as proposed (client copy; backend untouched). Mobile applies them. |
| 2 | MOB-12 plan AR/EN copy | **A** — Content authors AR/EN names/descriptions/feature lists against current plan versions + existing feature keys; delivered to the CMS admin plan-version fields. Backend already released (T10). |
| 3 | MOB-13 app-rating policy | **A/A/A** — (a) new submissions allowed anytime (current behavior is the contract); (b) no admin surface now (telemetry; DB-readable); (c) attributed + retained, no anonymous option, no deletion flow — document in the consumer guide. |
| 4 | MOB-07 duplicate-visit risk | **A now + B queued as P2** — mobile fixes its auth-refresh write retry client-side; backend adds optional visit-create idempotency (task P2 below). Never implicit soft-dedup (C rejected). |
| 5 | MOB-15 per-flow payments | **A** — global `availablePaymentMethods` + `subscriptionCardEnrollmentAvailable` remain the contract; no per-flow signals. Revisit only on a concrete business requirement. |
| 6 | WEB-08 / T9-B public legal | **A** — minimal public set: Terms + Privacy readable without login via a new `Public` audience; admins author/approve as today (no new workflow). Task P1. |
| 7 | Contact reply promise | **B** — non-binding "usually within 2 business days" in consumer docs + client confirmation surfaces (wording: "usually replies within 2 business days"; AR wording to mirror it — if the Arabic wording needs Content sign-off, keep the EN line authoritative and flag the AR translation for the same Content pass as #2). Task P3. |
| 8 | Supplier card visibility | **A** — keep the current public card; no change without a concrete frontend field list (fresh evidence). |
| 9 | Q13 / WEB-15 | With the frontend team. |

## P1 — T9-B public legal content (code; DeepSeek implements, GLM reviews)

**Goal:** everyone (no token) can read the published Privacy Policy and Terms & Conditions; the authenticated audience machinery is unchanged.

**Ground truth (verified):** `LegalAudience` = Family1/MedicalCaregiver2/CompanionCaregiver3/Elderly4/CareHomeOwner5, no Public (`LegalAudience.cs`); `LegalDocumentType` = `PrivacyPolicy=1`, `TermsAndConditions=2` only; published read filters `Status==Published` and picks max `Version` (unique `(document_type, audience, version)` index; `GetPublishedLegalDocumentQueryHandler.cs:26-50`); app route `api/v1/legal` (JWT-derived audience, `LegalController.cs`); admin `api/v1/admin/legal-documents` (SuperAdmin + ContentAdmin).

**Design constraints (locked):**
- Add `Public = 0` to `LegalAudience`. **Existing values 1–5 must not change** (stored as ints; zero migration expected — confirm the model configuration stores the enum as int and that no check constraint pins values).
- `IsDefined()` includes `Public`; `IsHelpAudience()` must **exclude** `Public` (Help Center stays authenticated — out of scope by decision 6A's minimal set).
- JWT audience resolution never yields `Public`: `GetOwnedContentAudiences`/`MapAccountTypeToAudience` untouched; an authenticated caller asking for `Public` on app routes is denied as today (documented), because Public is served only by the new anonymous path.
- New anonymous endpoint on a public controller (follow the existing public-controller authorization pattern; **no auth attribute**): `GET /api/v1/public/legal/{documentType}` where `documentType` binds from numeric or name against `LegalDocumentType` (only the two types exist). Response = the same `LegalDocumentResponse` shape as the app read (sections included), latest Published version for `Audience == Public`. Errors: unknown/undefined type → the existing coded 400 style (`Cms.Content` codes, controller-constructed like the audience errors); no Published Public document → existing 404 `LegalErrors.NotPublished` semantics — pin exact codes from `ResultProblemDetailsMapper`/controller conventions and mirror the `UnsupportedAudience`/`NotPublished` handling.
- Admin create/update validators must accept `Public` (validator uses `IsDefined` — verify).
- **No migration** unless verification proves one unavoidable; if one is needed, additive Cms migration files only, never applied.

**Owned files:** `LegalAudience.cs` (+extensions), new public legal controller (+ request binding if needed), new query/handler as warranted (may reuse `GetPublishedLegalDocumentQuery` with `Audience=Public` from the anonymous path), validators touched by the enum change, `ResultProblemDetailsMapper` only if a new code is genuinely needed, `docs/app/public/legal.md` or the appropriate public legal guide (find and update — check `docs/settings/legal.md` app guide and any public-facing doc), admin legal docs, Postman: new anonymous item in the appropriate public/app collection, tests: enum routing (IsHelpAudience excludes Public), handler latest-version/Public filter, controller anonymous 200 + 404, admin validator accepts Public, authenticated denial unchanged.

**Gate (once, after final edits):** `dotnet build Sanad.slnx -c Release --no-incremental` 0/0; focused filter for the new tests (e.g. `~Legal` or the precise intersection) — report exact native counts; no whole suite.

**Acceptance:** anonymous caller reads published Terms/Privacy; unpublished/unknown → documented errors; JWT-denial behavior byte-identical to today; docs + Postman truthful.

## P2 — Visit-create idempotency hardening (code, after P1; GLM implements, DeepSeek reviews)

**Goal:** an optional `Idempotency-Key` on `POST /api/v1/family/care-home-visits` makes retries safe: same key + same content → same visit response; same key + different content → 409. Absent key → exactly today's behavior (existing callers unchanged).

**Ground truth:** `CreateCareHomeVisitCommandHandler` (`CareHomeVisitHandlers.cs:152-...`): eligibility pre-check, then `guard.ExecuteAsync` (per-facility reservation serialization) re-verifying settings/capacity; **no client-supplied idempotency today**. Partner-contact idempotency precedent: columns on the aggregate row (`idempotency_key`, `client_key`), `SameContent` over create inputs, raced+sequential equivalence, unique-index relational story documented but EF-InMemory-tested.

**Design constraints (locked):**
- OPTIONAL header: absent → current path byte-identical (no required-header migration of existing callers).
- Storage: additive nullable columns on the visits aggregate (or a justified narrower design if the worker proves columns-inappropriate — propose-and-pin, no silent invention). SameContent comparison over the full create input (facilityId, kind, elderlyId, startsAt, visitorCount, contact fields as they exist).
- Same key + same content → return the existing visit's response (200), raced or sequential (both paths, like partner-contact). Same key + any content difference → 409 `CareHomes.Visits.IdempotencyConflict` (pin the code + mapper row).
- Dedup check must be correct **inside** the reservation guard path (or equivalent race-safe design); a blank/whitespace header is treated as absent; length bound pinned from house conventions.
- Additive migration if storage needs one — FILES ONLY, never applied, predecessor = current CareHomes head (worker verifies, expected `20261020210910_AddCareHomeRatingModeration`).
- Validator/controller/docs behave additively; `docs/care-homes/visits.md` + the Family.CareHomes Postman visit item updated; legacy rows (no key) read/act unchanged.

**Tests:** same key same content raced + sequential → same response; differing single field → 409, no extra rows; absent header → legacy behavior (existing tests unmodified and green); trim/absent equivalence; validator bounds. EF InMemory level; relational claims limited to the migration's declared design.

**Gate:** same discipline as P1 (strict build 0/0; focused `~CareHomeVisit` filter, exact counts; no whole suite).

## P3 — Docs closeout (docs-only; either model; no build slot)

1. `docs/app/public/partner-contact.md` (+ admin inbox doc where relevant): add the non-binding expectation line from decision 7 ("The team usually replies within 2 business days; this is an expectation, not a guaranteed SLA.").
2. `docs/app/feedback.md` (app-rating guide): document decision 3 — repeat submissions create a new rating (no update), attributed to the signed-in account, retained, not public, no admin surface today.
3. Landing plan-copy note per decision 2: the guide already documents the bilingual plan fields; add one line that approved AR/EN plan copy is a Content deliverable, not generated.
4. Verify-set: `docs/postman` untouched by P3; no code. JSON parses if any collection is proven drifted (expected: none).

## Sequence and discipline

1. Owner dispatch → P1 starts (sole build slot; DeepSeek/high; scribe status in this brief).
2. Fresh-session GLM review of P1 → accept/fix loop.
3. P2 starts (GLM/high) → fresh-session DeepSeek review → accept/fix loop.
4. P3 docs closeout (either model) → spot review.
5. Phase `ready_for_review` → Mastermind integration check → present for release approval (release, commit/push/deploy/migration-apply strictly on a separate Owner order).
6. Mobile adoption notes (1A strings, MOB-10/11 field mappings, eligible-residents pointer, `subscriptionCardEnrollmentAvailable` read) ride with the next team communication — no backend work.

## Current result

- Decisions 1–9 **locked** by Owner (recommendations accepted verbatim; "I love your recommendations… fast and most efficient path").
- **P1 accepted** 2026-10-22: implemented by `ses_e9ce2fc87ffebzAVNMMJqC8lR7` (DeepSeek/high) + fresh-session GLM review `ses_e9cd5ddfaffe1RitE9xaNo0pdo` verdict **ACCEPT** — gates independently reproduced (build 0/0; `~Legal|~HelpCenter|~ApiControllerBase` 123/123), diff set exactly the claimed files, the Public=0-vs-`default()` sentinel deviation ruled forced by the pinned manifest and semantics-preserving (`?audience=0` → 400 `InvalidAudience` preserved for every token class), migration=none confirmed (`HasConversion<int>`, no constraint), docs/Postman truthful, 21 new tests real. Three informational notes only.
- **P2 accepted** 2026-10-22: implemented by `ses_e9cd20e13ffeqdkJn9B3Vr5CoQ` (GLM/high) + fresh-session DeepSeek review `ses_e9cc56f09ffeGLKsSGrpgh7Hq3` verdict **ACCEPT-WITH-FIXES** (F1 High — raced catch inside the Serializable guard would 25P02/snapshot-miss on real PostgreSQL; fixed with the reviewer's smallest fix: exception escapes the guard, handled once outside on the rolled-back clean connection) → **round-2 focused re-check by the same reviewer: ACCEPT** (gate reproduced 0/0 + 31/31 = 13 pre-existing unmodified + 10 new + 8 admin; F2 Postman variable fixed; F3/F4 informational), plus the reviewer's optional wording polish applied (best-effort fast path comment; visits.md clause covers same/different facility). Storage: nullable `visits.idempotency_key` varchar(200) + unique `(family_id, idempotency_key)`; migration `20261022121500_AddCareHomeVisitIdempotency` files-only under verified head `20261020210910`, never applied. 409 `CareHomes.Visits.IdempotencyConflict` via mapper row; per-family key scope; absent/blank header byte-identical legacy.
- P1/P2/P3 planned in this brief; P2 dispatch follows P1 acceptance (single build slot), P3 last.

- **P3 accepted** 2026-10-22 (docs-only, `ses_e9cb6e7e2ffe93SaCs2txy5llZ` DeepSeek/high + Mastermind spot-check of the diff and direct source verification): decision-7 reply-expectation section in `docs/app/public/partner-contact.md` ("usually within 2 business days; an expectation, not a guaranteed SLA" — AR mirror flagged to the same Content pass as decision 2); decision-3 Rating policy section in `docs/app/feedback.md` (insert-only, attributed+retained, authenticated-only, not public, no admin surface — all source-verified: `FeedbackController.cs` `[Authorize]`/POST-only/`userId` replaced, `AppRatingFeedbackCommandHandler.cs:21` + `ResultProblemDetailsMapper.cs:386`); decision-2 authored-copy line in `docs/app/families/subscriptions.md`. Verify-set no-change; no Postman edit.
- **Phase: completed and released** (`1e3d2b021ba9455483b3976a712b76b40c2fef67`, deployed 2026-10-22, verifier exit 0, one CareHomes migration applied after pre-cutover backup; receipt: [reports/decisions-public-legal-release-2026-10-22.md](../reports/decisions-public-legal-release-2026-10-22.md)). Integration diff = P1 + P2 + P3 file sets only against `3af81a1`, plus owner dirt.

## P1 result (T9-B public legal — implementer handback, 2026-10-22)

**Status:** `ready_for_review` — implementation + local gate complete; **not** reviewed, **not** `completed`. Fresh-session GLM review is the next step. No git writes; nothing committed, applied, deployed, or released.

### What shipped (route + contracts)
- **Route:** `GET /api/v1/public/legal/{documentType}` — `[AllowAnonymous]`, no auth attribute. `documentType` binds as `LegalDocumentType`: `1`/`PrivacyPolicy`, `2`/`TermsAndConditions` (numeric or enum name, case-insensitive, trimmed). A comma list, unknown name, or undefined number is refused.
- **200** → same `LegalDocumentResponse` shape as the signed-in app read (ordered bilingual sections; `Audience = Public (0)`, `Status = Published (2)`, latest Published version for the pair).
- **400** malformed/undefined `documentType` → `Cms.Content.InvalidDocumentType` (controller-constructed `BadRequestWithCode`, same style as `Cms.Content.InvalidAudience`; no `ResultProblemDetailsMapper` row needed because the helper hardcodes 400).
- **404** no current Published Public version → existing `Cms.Legal.NotPublished` (via `ResultProblemDetailsMapper`, status 404). Never a fallback to another audience and never a draft.

### Per-file outcomes
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs` — added `Public = 0`; `IsDefined()` includes Public; `IsHelpAudience()` excludes Public. Values 1–5 unchanged. Doc comments updated.
- `src/API/Sanad.API/Controllers/PublicLegalController.cs` — **new** anonymous controller. Reuses `GetPublishedLegalDocumentQuery(DocumentType, LegalAudience.Public)` and `LegalDocumentResponse` (no parallel query, no new DTO). Private `TryParseDocumentType` rejects commas/undefined; 200/400/404 `ProducesResponseType` annotations.
- `src/API/Sanad.API/Controllers/ApiControllerBase.cs` — **forced deviation (pinned)**: two Public guards (see below).
- `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Legal/ContentErrors.cs` — added `InvalidDocumentTypeCode = "Cms.Content.InvalidDocumentType"` (const string only; no mapper row).
- `src/Modules/Cms/Application/.../Legal/CreateLegalDocumentCommandValidator.cs` — message text updated to list `0 (Public)` … `5 (CareHomeOwner)`. Validator logic already accepted Public via `IsInEnum()`; no rule change.
- `tests/Sanad.UnitTests/API/PublicLegalControllerTests.cs` — **new** (21 tests): enum semantics/HelpAudience excludes Public; Help Center handler rejects Public; latest-Published Public read with no cross-audience fallback; anonymous 200 (numeric + name + trim); malformed → 400 coded (and never reaches the handler); no Published Public → 404 `NotPublished`; controller anonymous/route metadata; create validator accepts Public / rejects undefined; authenticated app read never serves Public (Family → 404, `?audience=0|Public` → 400, admin → 403).
- `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs` — **one existing assertion updated (unavoidable)**: the OpenAPI `LegalAudience` enum is now `{0,1,2,3,4,5}` named `Public`; the test now asserts that array plus `Contains("Public")`. All other assertions in the test preserved; no assertion weakened or deleted.
- **Docs (existing guides, no new file):** `docs/app/settings/legal.md` (primary consumer legal guide), `docs/admin/legal-content.md` (authoring + Public audience warning), `docs/app/settings/privacy-and-security.md` (endpoint map + public note).
- `docs/postman/app/Sanad.App.Public.postman_collection.json` — new folder `08. Public legal` → item `Get public legal document (anonymous)` → `{{baseUrl}}/api/v1/public/legal/{documentType}` with codes documented. JSON parses (verified).

### Forced deviation (pinned for reviewer)
`Public = 0` collides with the `default(LegalAudience)` sentinel returned by `MapAccountTypeToAudience` for unmapped/admin claims, and with `?audience=0` parsing. Two guards were added so no token ever yields Public and Public is never an app audience:
- `GetOwnedContentAudiences`: `audience.IsDefined() && audience is not LegalAudience.Public`.
- `TryParseContentAudience`: `parsed.IsDefined() && parsed is not LegalAudience.Public`.
`GetOwnedContentAudiences`/`MapAccountTypeToAudience`/`TryParseContentAudience` are otherwise byte-identical to baseline. Without these guards, admin tokens would resolve to Public (breaking existing `ApiControllerBaseTests`/`OwnerLegalAudienceApiTests` admin-403 assertions) and `?audience=0` would flip 400→403 (breaking `MalformedAudience_IsCodedBadRequest`). These guards are exactly what keeps the specified existing tests green.

### Migration: **No** (proved unnecessary)
`LegalDocumentConfiguration` stores `audience` via `HasConversion<int>()` with no check constraint pinning values, so adding `Public = 0` needs no schema change. No migration file created; DB untouched; nothing applied.

### Gate evidence (single pass, after final edits)
- `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- Focused filter `FullyQualifiedName~Legal|FullyQualifiedName~HelpCenter|FullyQualifiedName~ApiControllerBase` → **Passed: 123, Failed: 0, Skipped: 0** (Sanad.UnitTests). ArchitectureTests matched 0 (no filter hits).
- Breakdown: `PublicLegalControllerTests` = **21 passed**; `OwnerLegalAudienceApiTests` + `ApiControllerBaseTests` + `HelpCenter` = **85 passed**; remaining 17 are other `*Legal*` unit tests.
- Whole suite was **not** run (per brief). Pre-existing dirt (`.codex/**`, `README.md`, `docs/internal/governance/AGENTS.md`, `tools/opencode_bridge/**`, logs) untouched; no git writes.

### Limitations (reviewer must weigh)
- Tests are EF InMemory level; the relational/no-check-constraint claim rests on reading `LegalDocumentConfiguration`, not on relational execution. The route was exercised directly through the controller (no host/HTTP pipeline); `PortalApiMetadataHost` was used only for the OpenAPI enum shape.
- Migration/DB were never applied or touched; no runtime/HTTP/DB evidence exists for P1.
- The route-mapping helper `docs/internal/tools/Verify-ApiContractMapping.ps1` does not compute missing/orphan sets, so its exit 0 is not coverage evidence and was not used as such.

## P2 result (visit-create idempotency — implementer handback, 2026-10-22)

**Status:** `ready_for_review` — implementation + local gate complete; **not** reviewed, **not** `completed`. Fresh-session DeepSeek review is the next step. No git writes; nothing committed, applied, deployed, or released. P1's files were only read, never written; owner dirt untouched.

### Behavior shipped (route + contract)
- **Route:** `POST /api/v1/family/care-home-visits` (`CareHomeVisitsController.FamilyCreate`) now binds an **optional** `Idempotency-Key` header (`[FromHeader]`, never required). Absent/blank/whitespace → today's path byte-identical (no extra query, no key stored, `IdempotencyKey=null` on the command); every pre-existing test green unmodified.
- **Same key + same create content** → the stored visit's response with **200** (sequential, raced, and unique-race paths all replay; see race-safety below).
- **Same key + any create-input difference** → **409 `CareHomes.Visits.IdempotencyConflict`**, no extra row.
- **Key bound:** trimmed length 1–200; a supplied key over 200 after trim → **400 `CareHomes.Visits.InvalidRequest`** (controller-constructed `BadRequestWithCode`, mirroring the partner-contact validator's ≤200). The bound constant is `CareHomeVisit.MaximumIdempotencyKeyLength = 200` on the aggregate, mirrored by domain validation in `CareHomeVisit.Create`. Rationale: mirrors `PartnerContact.MaximumIdempotencyKeyLength = 200` (the only house convention for caller-supplied idempotency keys); the header is optional so blank→absent replaces partner-contact's required-key error.
- **SameContent** compares the full create input as it exists on the command: facilityId, kind, elderlyId, visitorName (trimmed), visitorPhone (trimmed), visitorCount, startsAtUtc. Required-null body fields are trimmed null-safely (visits have no FluentValidation validator, unlike partner-contact). Attempt timestamps (`UtcNow`/created/updated/decision clocks) are deliberately not compared. Key is **scoped per Family** (the row's own `family_id`), the authenticated analogue of partner-contact's server-derived `client_key`.
- **Replay semantics (pinned):** replay **re-reads the visit row** (preferred, per manifest) and maps it with the existing `ToResponse()` — no stored payload. The visit id is the replayed original; the response body reflects the row's *current* persisted state (a replay after approval returns the approved state) because the response is fully derived from the row. Pinned and documented in `docs/care-homes/visits.md`.

### Per-file outcomes
- `src/Modules/CareHomes/Domain/.../Bookings/CareHomeVisits.cs` — added `MaximumIdempotencyKeyLength = 200`, nullable `IdempotencyKey` property (set only on family create; `RequestReschedule` stays null), optional trailing `Request(..., string? idempotencyKey = null)`. Domain `Create` validates/normalizes (trim; empty or >200 → `DomainException`). All existing signatures/behavior unchanged (optional parameter).
- `src/Modules/CareHomes/Application/.../Visits/CareHomeVisitHandlers.cs` — command gains trailing optional `string? IdempotencyKey = null` (existing constructions unaffected); new `CareHomeVisitErrors.IdempotencyConflict`; handler follows the partner-contact structure exactly (trim/normalize → try-lookup → in-guard lookup → create → `SaveChangesAsync` wrapped in a unique-violation catch → detach → winner lookup → same/different → replay/409), plus private `SameContent`, `DetachAdded`, `IsUniqueViolation` copies in the CareHomes module. Null-key requests never touch any new code path.
- `src/Modules/CareHomes/Infrastructure/.../Configurations/CareHomeVisitConfiguration.cs` — nullable `idempotency_key` (varchar 200) + unique index `(FamilyId, IdempotencyKey)`.
- `src/API/Sanad.API/Controllers/CareHomeVisitsController.cs` — header parameter + normalization (blank→absent, trim) + over-long→`BadRequestWithCode(CareHomeVisitErrors.InvalidRequest.Code)`; forwards the key as the command's last argument. `ProducesResponseType` for 200/409 added.
- `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs` — one row: `["CareHomes.Visits.IdempotencyConflict"] = 409`. That is the exact 409 mechanism (handler `Error` → `ToActionResult` → mapper); no controller-constructed path used for the 409 itself.
- **Migration (FILES ONLY, never applied):** `src/Modules/CareHomes/.../Migrations/20261022121500_AddCareHomeVisitIdempotency.cs` + `.Designer.cs`, chained under the **verified head predecessor `20261020210910_AddCareHomeRatingModeration`** (confirmed highest CareHomes timestamp on the tree). Up: add nullable `care_homes.visits.idempotency_key` (character varying(200)) + unique `IX_visits_family_id_idempotency_key`; Down: symmetric DropIndex/DropColumn. Snapshot `CareHomesDbContextModelSnapshot.cs` updated with the property + unique index.
- `docs/care-homes/visits.md` — new "Optional retry header (`Idempotency-Key`)" section (absent/blank==legacy, same→same-visit 200, different→409 code, per-family scope, 200-char bound, reschedule/cancel/decision routes unaffected, race note) + the migration row in the persistence section (files-only, never applied).
- `docs/postman/app/Sanad.App.Family.CareHomes.postman_collection.json` — only the "Request a prospective visit" item updated: optional `Idempotency-Key: {{careHomeVisitIdempotencyKey}}` header + one extended description of the three outcomes; all other items untouched. JSON parses (ConvertFrom-Json verified).
- `tests/Sanad.UnitTests/CareHomes/CareHomeVisitTests.cs` — **10 new real facts** (no placeholders): same-key-same-content sequential replay (incl. untrimmed retry), same-key-differing-field ×5 cases → 409 with no extra rows (incl. mapper 409 status assertion), absent-key legacy duplicates, blank/whitespace==absent, per-family key scoping, over-bound rejection + 200-char boundary, raced winner replay / raced different-content 409 / unique-violation propagation with no winner (race simulated exactly like the partner-contact `RacingIdentityDbContext` pattern), and controller `FromHeader` metadata (`Idempotency-Key`). One non-owned helper (`InlineVisitGuard`) reused; no existing test modified.

### Race-safety reasoning (documented, per manifest)
The create path runs inside `ICareHomeVisitReservationGuard.ExecuteAsync` — a **per-facility** `pg_advisory_xact_lock` + `Serializable` transaction. Placement (after the F1 correction):
1. **Try-lookup first thing in `Handle`** (before eligibility pre-checks, so a retry replays the original outcome instead of re-evaluating today's facility/settings state).
2. **In-guard lookup at the top of the callback, before the capacity re-check** — a **best-effort fast path** for the sequential (and lucky same-facility) case; because a raced same-facility retry's Serializable snapshot is frozen before the advisory lock is granted, it may miss the committed winner, and same-facility raced retries usually resolve via the outside catch on the unique (family, key) index replays/409 on the clean connection.
3. **Unique-index violation handled ONCE, OUTSIDE the guard** (F1, reviewed-found, fixed with the reviewer's smallest fix): the earlier implementation caught the `DbUpdateException` inside the guard callback, which could not work on the relational provider — after the failed statement the Serializable transaction is aborted (Npgsql `25P02`) and its snapshot was frozen by the advisory-lock SELECT before the lock was granted, so an in-guard read of the winner would itself throw and could miss a committed winner. The `DbUpdateException` now escapes `guard.ExecuteAsync` (its `await using` rolls the aborted transaction back) and is handled on the clean connection: detach the failed row, re-read the winner, same-content → replay 200, different-content → 409 `CareHomes.Visits.IdempotencyConflict`, no winner visible → documented propagation. This covers same-facility **and** cross-facility raced pairs (the same key raced against a different facility takes different advisory locks). Partner-contact/SOS do not hit this and keep their in-handler catch — they run without an ambient transaction; only the visits handler differs.
Relational story: documented, not claimed — the unique index is declared in the migration/snapshot; **EF InMemory does not enforce it**, so the raced tests exercise the escaped-exception path with the store double that throws on the guarded save, exactly as the partner-contact tests simulate the store race.

### Review corrections (round 1, 2026-10-22 — fresh-session review ACCEPT-WITH-FIXES)
- **F1 (High, fixed):** the unique-violation catch was inside the guard callback and could not work on the relational provider (aborted Serializable transaction, snapshot predating the advisory lock); moved OUTSIDE `guard.ExecuteAsync` per the reviewer's smallest fix — see the corrected race-safety reasoning above. No existing test assertion weakened; the three race facts exercise the escaped-exception path unchanged.
- **F2 (Low, fixed):** `careHomeVisitIdempotencyKey` is now declared in the collection's `variable` array (`{"key":"careHomeVisitIdempotencyKey","value":"","type":"string","description":"…empty = legacy create."}`); collection JSON re-validated (parses).
- **F3 (informational):** no action taken.
- **F4 (per-Family key scope):** documented in `docs/care-homes/visits.md` ("keys are scoped per Family") and pinned in the SameContent design above; no code change.
- **Round 2 (micro-polish, 2026-10-22):** reviewer verdict **ACCEPT** with this optional wording polish applied (in-guard fast-path retitled best-effort, visits.md clause extended to same/different-facility raced retries); no re-review needed per the reviewer's non-blocking label.

### Gate evidence (round 1 fixes frozen, single pass)
- `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- Focused filter `FullyQualifiedName~CareHomeVisit` → **Passed: 31, Failed: 0, Skipped: 0** (`Sanad.UnitTests`). Breakdown: `CareHomeVisitTests` **23** (13 pre-existing + 10 new) + `AdminCareHomeVisitQueryTests` **8**. ArchitectureTests matched 0. Whole suite **not** run (per brief).
- (Round-0 history: one earlier run failed 1/31 on a test-ordering bug in the new bound test (`Assert.Empty` after the successful at-bound create); fixed by reordering the test's own assertions — product code unchanged. Nothing was ever fewer green.)

### Limitations (reviewer must weigh)
- EF InMemory level throughout: the unique-index behavior (NULLS-DISTINCT keeping legacy/reschedule null-key rows outside uniqueness) is documented from PostgreSQL semantics, not exercised; the raced path is exercised through the store double that throws on the guarded save (the escaped-exception handler), not by real concurrency, and the real `Serializable`-transaction guard is not exercised in tests (its interface-level `InlineVisitGuard` predecessor is). The relational claim that the winner becomes readable only after the guard rolls back the aborted transaction rests on Npgsql/PostgreSQL semantics (`25P02`), not on a relational test run.
- Migration files were never applied; no DB/runtime/HTTP evidence exists for P2.
- The replay response reflects the visit's current row state (see pinned replay semantics) — a repeated retry after the visit was approved returns the approved state, not a frozen creation snapshot; the id and content-binding stay stable.
- No reschedule/idempotency interaction: the header is accepted on create only; reschedule rows always carry a null key by design.

## P3 result (docs closeout — scribe, 2026-10-22)

**Status:** `ready_for_review` — docs-only edits complete; **not** reviewed, **not** `completed`. No git writes; nothing committed, pushed, deployed or released. No code, migration, DB, runtime or HTTP touched; no build/tests run (docs-only per standing rule). P1/P2-owned files were read only, never written; owner dirt untouched.

### Files changed (3 docs)
1. `docs/app/public/partner-contact.md` — **Decision 7.** New short `## Response expectation` section between Errors and “What the support team reads”: “The team **usually replies within 2 business days**. This is an expectation, not a guaranteed SLA.” Locked wording, house style, no SLA/enforcement claim. The doc has **no Arabic-parallel text**, so no AR mirror line was added in-file; per decision 7 the AR wording (“mirror the EN line”) is **flagged for the same Content pass as #2** (see Content sign-off below).
2. `docs/app/feedback.md` — **Decision 3.** Added a `## Rating policy` section documenting locked A/A/A: (a) each submission creates a **new** rating (new id, insert-only, no update-in-place — repeat submissions never replace); (b) **attributed and retained** to the signed-in account, no update/delete route; (c) **authenticated only, no anonymous option** (no token → `401`); (d) **not public** (no read route returns ratings); (e) **no admin surface today** (telemetry, readable only from storage; an admin surface is a new product decision). Also sharpened the existing error sentence: out-of-range `rating` is `400 Feedback.InvalidRating` (was generic “standard validation/problem-details”).
3. `docs/app/families/subscriptions.md` — **Decision 2.** One added line to the “Bilingual plan copy” section: the approved AR/EN names, descriptions and feature lists are an **authored Content deliverable entered through the admin plan-version fields**; the API never generates the copy (or prices, discounts, benefits). Consistent with the existing “copy is display-only” line and with the admin authoring guide `docs/admin/subscriptions.md:36`.

### Evidence (source-read, decision 3 — docs-only turn, no runtime)
- Route/auth: `src/API/Sanad.API/Controllers/FeedbackController.cs:10` `[Authorize]`, `:11` `[Route("api/v1/feedback")]`, `:21` `[HttpPost("app-rating")]`, `:27-30` missing identity → `401`, `:32` request `UserId` replaced by the authenticated id.
- Insert-only: `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Feedback/AppRatingFeedbackCommandHandler.cs:18-22` rating range → `Feedback.InvalidRating`; `:25-32` new entity with `Guid.NewGuid()`; `:34` `Feedbacks.Add`; `:35` save. No update/delete path anywhere.
- Error mapping: `src/API/Sanad.API/ProblemDetail/ResultProblemDetailsMapper.cs:386` `["Feedback.InvalidRating"] = 400`.
- Storage/retention: `src/Modules/Identity/Infrastructure/.../Configurations/FeedbackConfiguration.cs:12` table `identity."Feedbacks"`, `:22` index on `UserId`. Command shape `AppRatingFeedbackCommand.cs:7-12`. Domain record `Feedback.cs:5-12`.
- No admin/public read: repo grep over `src/API` finds only this one POST controller and the mapper row; the only `Feedbacks` consumer is the handler’s `Add` (no GET/list/detail endpoint).

### Verify-set (no-change expected)
- `docs/admin/support-inbox.md` — **no change.** Admin doc; its `## Reply` section (`:94-115`) documents admin reply mechanics and best-effort delivery, not a promise to the anonymous submitter. No user-facing reply-promise section exists to update (matches the brief’s “likely no change”).
- Partner-contact Postman item — **untouched.** `docs/postman/app/Sanad.App.Public.postman_collection.json` item “Submit partner contact (anonymous)” (item description at `:131`) states route/fields/limits and the 200/400/409/429 outcomes; it does **not** mention reply behavior, so no wording to align. No Postman file changed by P3 → **no JSON parse gate run** (nothing drifted).
- `docs/app/public/landing.md` — **untouched, consistent.** No plan-copy or app-rating statements exist there; its partner-contact subsection (`:132-152`) states only route/fields/limits and carries no reply promise, consistent with decision 7. No drift proven. (Note: task 4 phrased this as “consistent with decision 3”; landing.md has no decision-3 content — interpreted as the public contact/plan consistency check.)

### Content sign-off flag
- Decision 7 AR wording for the client confirmation/contact surfaces still needs the **same Content pass as decision 2** (AR/EN plan copy). EN remains authoritative; no AR text was mirrored in-file because `partner-contact.md` carries no Arabic-parallel text.

### Evidence limits / limitations
- Docs-only turn: **no** `dotnet build`/`test`, no EF/DB, no runtime/HTTP, no migration apply. Decision-3 claims rest on **source-read** (file:line above), not on executed checks.
- The feedback doc’s “readable only from storage” is a negative claim from grep + the absence of any read route/DbSet consumer other than the handler; no runtime read was exercised.
- `partner-contact.md` and `subscriptions.md` edits are pure copy; nothing here asserts deployment.
- Not marked `completed`; Mastermind owns acceptance and any release action.

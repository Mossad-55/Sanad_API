# F5 — Owner help, support inbox and ticket replies (A4/A14)

Phase: `accepted` (Owner authorized); task: `completed`; release: authorized for the
delegated release executor. The Worker never commits, publishes, deploys or applies a
migration to any shared/remote database. Base: pushed `main` HEAD recorded at dispatch.
Worker A: `opencode/beam-free#high`. The dispatch worker delivered round 1 and its correction
round; the tests, docs, Postman deliverable, runtime adapter and the final controller/mapper
corrections were authored by the coordinating Mastermind — see Current result.

**Mastermind acceptance (2026-10-20):** phase `accepted`; task `completed`; release
`authorized`. Accepted against the actual diff and the evidence below: strict solution build
0 warnings/0 errors, 81/81 focused tests, 32/32 real local HTTP checks — including the three
real defects that gate caught and the Mastermind then corrected. Release is delegated to the
release executor, which takes a Production backup and applies the single additive Identity
migration before restarting the service.

Read root `AGENTS.md`, [governance](../../../governance/AGENTS.md), the
[handoff](../../../Mastermind_Handoff.md) and the approved
[F5 section](../contracts/frontend-follow-up-2026-10-20.md#f5--owner-help-and-support-inbox-a4a14).
F2/F3/F4 are released or in release; F6 runs concurrently and owns the Families,
CareHomes and Caregivers read files listed in its own brief. Preserve every unrelated
dirty edit (`.codex/*`, `README.md`, `tools/opencode_bridge/*`, `.opencode*`,
`workflow-audit/`, contracts/clarification docs, E03 prompts, `docs/internal/operators`
notes) and never stage them. No subagents, no new framework, no OTP work.

## Anchors already verified (do not re-derive)

- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/SupportTicket.cs:7`
  is the only support aggregate; `SupportTicketStatus.cs:3` currently has only `New = 1`;
  the only mutation is `MarkNotified` (`SupportTicket.cs:90`). No reply entity, no status
  history. Table config `.../Infrastructure/.../Persistence/Configurations/SupportTicketConfiguration.cs:14`
  (`support_tickets`, status `HasConversion<int>`), `DbSet` at `IdentityDbContext.cs:35`,
  sole migration `20260911195850_AddSupportTickets`.
- Application `.../Application/Support/`: `SubmitSupportRequestCommand(.Handler/.Validator)`,
  `SupportRequestResponse`. The handler persists first, then attempts
  `IEmailSender.SendSupportRequestAsync` inside a swallowing try/catch
  (`SubmitSupportRequestCommandHandler.cs:66-83`) — this is the approved
  "readback survives delivery failure" pattern to reuse.
- HTTP: `src/API/Sanad.API/Controllers/SupportController.cs:11` (`api/v1/support`) has
  only `POST /contact`, action-level `[Authorize(NormalAccess)]`; there is **no** admin
  ticket controller today. Policies live in
  `src/API/Sanad.API/Authorization/AuthorizationPolicies.cs` and are registered in
  `src/API/Sanad.API/DependencyInjection.cs:322` (SuperAdmin+SupportAdmin pairs exist,
  e.g. `CareHomesOperationalAdmin` at `:391`).
- Help Center/FAQ is in the **Cms** module: `HelpFaq.cs:11` with per-row `LegalAudience`
  `Audience` (`:43`), validated by `LegalAudience.IsHelpAudience()`
  (`LegalAudience.cs:34`, currently Family/MedicalCaregiver/CompanionCaregiver/Elderly —
  `CareHomeOwner = 5` exists but is deliberately denied), singleton
  `SupportContact.cs:12` (`SingletonId=1`, phone/email), admin routes in
  `AdminHelpCenterController.cs:18-19` (`CmsContent`, `api/v1/admin/help-center`), signed-in
  read `HelpCenterController.cs:18-19` (`NormalAccess`, `api/v1/help-center`).
  `ApiControllerBase.TryGetCmsAudienceFromClaims:107` maps only the four app audiences and
  is used **only** by `HelpCenterController`; `TryGetLegalAudienceFromClaims:138` already
  maps `CareHomeOwner`.
- In-app notification: `CreateInAppNotificationCommand` (`.../Notifications/NotificationCommands.cs:10`),
  producer/idempotency pattern in `CreateChatMessageNotificationCommand.cs:38-107`,
  preference/template mapping in `PushFanoutPolicy.cs:7-27`. Identity's application
  project references no other module, so a reply notification needs an **API-layer
  integration gateway** (`src/API/Sanad.API/NotificationsIntegration/`), not a new module
  reference. Email: `.../Identity/Application/.../Abstractions/Messaging/IEmailSender.cs:6`
  with `SmtpEmailSender.cs:15` and `DevelopmentEmailSender.cs:8`.
- Paging convention: default 25 / max 100, page shape `Items/Page/PageSize/TotalCount`
  (see `.../Community/Application/.../Posts/AdminCommunityRestrictionQueries.cs:61-93`).

## Pinned behavior (Mastermind decision — implement exactly; do not extend)

1. **Statuses.** Extend the persisted enum additively: `New = 1, InProgress = 2,
   Resolved = 3, Closed = 4`. Keep the existing int conversion.
2. **Transition matrix** (explicit Admin action only; a reply never changes status):
   `New → InProgress`, `New → Resolved`, `New → Closed`, `InProgress → Resolved`,
   `InProgress → Closed`, `Resolved → Closed`. Every other transition — including any
   transition out of `Closed` and any reopen — is refused with a conflict (HTTP 409)
   using the existing error/status convention (find how `Community.RestrictionAlreadyActive`
   becomes 409 and reuse that mechanism); refuse before any write. An idempotent
   re-submission of the current status is **not** silently accepted: report it as the
   same conflict. Record `StatusChangedByUserId` + `StatusChangedOnUtc` on the row.
3. **Replies.** A reply is durable (body + actor user id + UTC time) and belongs to one
   ticket. Replies are allowed while the ticket is `New`, `InProgress` or `Resolved`;
   a reply to a `Closed` ticket is refused with the same conflict and creates nothing.
   Only Admin (SuperAdmin/SupportAdmin) replies. Users submit and read only.
   An Admin reply does **not** change status.
4. **Roles.** Both policies already exist in the base — the coordinating Mastermind
   added `SupportInboxAdmin` and `OperationalReadAdmin` (each SuperAdmin + SupportAdmin)
   to `AuthorizationPolicies.cs` and registered both in `DependencyInjection.cs`; those
   two files are therefore **read-only for you** (do not rename, re-add, duplicate or
   remove a registration — if one looks wrong, report it instead of editing it). Use
   `SupportInboxAdmin` on every new ticket route. The concurrent F6 worker uses
   `OperationalReadAdmin` for its admin family read. ContentAdmin/FinanceAdmin are denied
   on every new admin ticket route; users may read only their own tickets (never another
   user's, never an admin view). Anonymous callers get 401 everywhere except the new
   contact read.
5. **Owner FAQ audience.** Include `CareHomeOwner` in `IsHelpAudience()` and map
   `AccountType.CareHomeOwner → LegalAudience.CareHomeOwner` in
   `TryGetCmsAudienceFromClaims` so the existing signed-in Help Center read serves the
   owner's own FAQs. This is an intentional behavior change: the current assertion
   `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs:163`
   (`Assert.False(LegalAudience.CareHomeOwner.IsHelpAudience())`) and any sibling
   expectation must be updated to the new approved behavior, with a test proving an owner
   now reads owner FAQs and still cannot read another audience's FAQs.
6. **Anonymous contact-only read.** Add an `[AllowAnonymous]` read returning only the
   support phone/email (a null contact is a valid 200). It must not expose FAQs, ticket
   data or any other Help Center field.
7. **Delivery.** On reply: persist the reply first, then attempt the user's email and an
   in-app notification, both inside the existing swallowing pattern, and keep the reply
   readable even when both deliveries fail. Never report a saved reply as delivered.
   No attachments, assignment, SLA engine, live chat, reopen flow or user reply thread.

## Exact writable manifest

Both authorization files were already prepared **and committed to `main`** by the
coordinating Mastermind before your dispatch (the `SupportInboxAdmin` and
`OperationalReadAdmin` constants plus their two registrations, with a verified strict API
build), so they are read-only for you, are not in your manifest below, and need no action
at release time.

- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/SupportTicketStatus.cs`
- `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/SupportTicket.cs`
- New: `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/SupportTicketReply.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Configurations/SupportTicketConfiguration.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs`
- New (generated): `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/*_AddSupportTicketWorkflow.cs` + its `.Designer.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/IdentityDbContextModelSnapshot.cs`
- New: `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminSupportTicketQueries.cs`
- New: `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminSupportTicketCommands.cs`
- New: `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/MySupportTicketQueries.cs`
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/SupportRequestResponse.cs` (only if the response shape must grow)
- `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Abstractions/Messaging/IEmailSender.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Messaging/SmtpEmailSender.cs`
- `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Messaging/DevelopmentEmailSender.cs`
- `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs`
- New: `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Help/PublicSupportContactQuery.cs` (query + handler; may live in one file)
- New: `src/API/Sanad.API/NotificationsIntegration/SupportReplyNotificationGateway.cs`
- `src/API/Sanad.API/Controllers/ApiControllerBase.cs`
- `src/API/Sanad.API/Controllers/SupportController.cs`
- `src/API/Sanad.API/Controllers/HelpCenterController.cs` (only if needed for the owner audience)
- New: `src/API/Sanad.API/Controllers/AdminSupportTicketsController.cs`
- New: `src/API/Sanad.API/Controllers/Requests/SupportTicketRequests.cs`
- New: `tests/Sanad.UnitTests/Identity/Support/AdminSupportTicketTests.cs`
- New: `tests/Sanad.UnitTests/Identity/Support/MySupportTicketTests.cs`
- `tests/Sanad.UnitTests/Identity/Support/SupportTicketTests.cs`
- `tests/Sanad.UnitTests/Identity/Support/SubmitSupportRequestCommandHandlerTests.cs` (only if the handler changes)
- `tests/Sanad.UnitTests/Identity/Registration/IdentityTestDbContext.cs`
- `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs`
- New: `tests/Sanad.UnitTests/API/SupportInboxApiTests.cs`
- `docs/auth/support-contact.md`, `docs/app/settings/help-support.md`, `docs/admin/help-center.md`
- New: `docs/admin/support-inbox.md`
- `docs/internal/operations/migrations.md` (add the new Identity migration row)
- `docs/postman/Sanad.Auth.postman_collection.json`, `docs/postman/admins/Sanad.Admin.postman_collection.json`
- New: `tools/Invoke-F5LocalHttpVerification.ps1`
- This brief.

Anything else — including `docs/internal/operations/endpoint-coverage-matrix.md`,
`endpoint-coverage-tracker.md`, `docs/internal/Mastermind_Handoff.md`, other modules'
files and the F6 manifest — is read-only. Request a precise manifest amendment if the
scope truly needs one; never expand silently.

### Manifest amendment (Mastermind, after reviewing round 1)

Additionally writable, because round 1 proved them necessary and they are minimal:
`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Abstractions/Data/IIdentityDbContext.cs`,
`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/SupportTicketErrors.cs`,
`src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Configurations/SupportTicketReplyConfiguration.cs`,
`src/BuildingBlocks/Domain/Sanad.BuildingBlocks.Domain/Primitives/Ids/SupportTicketReplyId.cs`,
`tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`, a new notifier abstraction under
`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Abstractions/Messaging/`,
and exactly one registration line inside `src/API/Sanad.API/DependencyInjection.cs`
(the `SupportInboxAdmin`/`OperationalReadAdmin` policy blocks in that file are already
committed by the Mastermind and must not be touched). Nothing further may be added
without reporting it first.

### Mastermind review of round 1 (what must be fixed)

Round 1 delivered the migration, domain/app/controller surface and the strict build, but:
1. `ReplyToSupportTicketCommandHandler` never attempts the approved in-app notification
   (a comment says "skip"), and `AdminSupportTicketsController.Reply` contains dead
   placeholder `try { } catch { }` code; delivery logic does not belong in a controller.
2. The conflict cannot surface as 409 through `ToActionResult` alone: the shared
   `ResultProblemDetailsMapper` has no `Identity.Support.*` entries, so the pinned 409
   must be produced explicitly the way `AdminCommunityPostsController` does it.
3. **No new tests were authored** even though the manifest names three new test files;
   `AdminSupportTicketTests.cs`, `MySupportTicketTests.cs` and
   `SupportInboxApiTests.cs` do not exist, so no transition, reply, role or isolation
   behaviour is covered.
4. The whole docs/Postman deliverable is missing, and the runtime HTTP gate never ran.
5. `src/API/Sanad.API/Controllers/SupportController.cs` lost its trailing newline.


## Migration (authorized, narrow)

The reply entity plus the two status-change columns require exactly **one** additive
Identity migration generated with `dotnet ef migrations add` (design-time factory reads
`ConnectionStrings__IdentityDatabase`; use a dummy connection string, never a real one).
Never edit an applied migration. Applying it is allowed **only** to the local
`localhost:5432/SanadDb` for your own HTTP proof; do not apply it to any remote, Bruno,
staging or Production database. State in your handback exactly how the API applies
Identity migrations at startup on the deployed target (read the startup code; do not
assume) so the release step is unambiguous.

## Verification gate (you own the first runtime slot)

F6 holds at implementation handback until you finish; do not poll it. Run each gate once:
strict `dotnet build Sanad.slnx -c Release --nologo /p:TreatWarningsAsErrors=true`
(0 warnings/0 errors); focused `dotnet test` over the support/API/Cms filters you name;
and `tools/Invoke-F5LocalHttpVerification.ps1` over real localhost HTTP with minimal
INSERT-only synthetic records, own-session logout, no reset/delete/bulk seed and no
production/provider call. The adapter must dot-source `tools/Invoke-LocalFixtureVerification.ps1`
`-LibraryOnly` and reuse its helpers (as `tools/Invoke-F4LocalHttpVerification.ps1:94` does),
use a loopback-only URL on a free port, and fail closed on the all-context/non-live
preflight. Prove end to end: anonymous contact read is 200 and leaks nothing else;
anonymous ticket reads are 401; a real submit appears in the Admin list/detail; only the
submitter reads their own detail (another user gets 404/403, never data); role denial for
ContentAdmin/FinanceAdmin; each allowed transition succeeds once and each refused
transition/reply-to-closed returns the conflict with nothing written; a reply with email
and notification deliberately unavailable still reads back; the owner Help Center read
returns owner FAQs. Report trace correlation honestly if unavailable.

## Current result

Task: `completed` — implementation, tests, docs, Postman and the runtime gate are all complete
and green, and the Mastermind accepted the phase on 2026-10-20 (see the acceptance note at the
top). Sessions: the implementation
worker (round 1 + correction round) and, after that correction round under-delivered, the
coordinating Mastermind (tests, docs, Postman, runtime adapter, final corrections). Base HEAD:
607d33fcb651bed25239050989cb94a8cb36de41; tree HEAD observed 0ab1f3fd.

Migration: `20261020191733_AddSupportTicketWorkflow` (Identity, one additive migration,
generated with a design-time dummy string). It adds the nullable `status_changed_by_user_id`
and `status_changed_on_utc` columns to `identity.support_tickets` and the new
`identity.support_ticket_replies` (id, ticket_id, actor_user_id, body, created_on_utc).
Applied **only** to the local `localhost:5432/SanadDb` and verified there (history row present,
both nullable columns present, reply table present with exactly those columns, 0 tickets
before the first run). Applied to no remote, Bruno, staging or Production database.

Identity startup migration answer: API applies Identity (and all module) migrations at startup via `dbContext.Database.Migrate()` in `ApplyMigrations<TContext>` (`ApplicationBuilderExtensions.cs`), invoked for every context when `StartupMigrationPolicy.ShouldApplyModuleMigrations` returns true (Development/normal host). Finance only if `ShouldApplyFinanceMigrations`. L1 fixture workflow disables startup migrations.

Changed files (manifest): SupportTicketStatus.cs, SupportTicket.cs, SupportTicketReply.cs, SupportTicketConfiguration.cs, IdentityDbContext.cs, migration + snapshot, AdminSupportTicketQueries.cs, AdminSupportTicketCommands.cs, MySupportTicketQueries.cs, IEmailSender.cs, SmtpEmailSender.cs, DevelopmentEmailSender.cs, LegalAudience.cs, PublicSupportContactQuery.cs, SupportReplyNotificationGateway.cs, ApiControllerBase.cs, SupportController.cs, AdminSupportTicketsController.cs, SupportTicketRequests.cs, IdentityTestDbContext.cs, OwnerLegalAudienceApiTests.cs, HelpCenterTests.cs, tools/Invoke-F5LocalHttpVerification.ps1, this brief. Extra: BuildingBlocks/Ids/SupportTicketReplyId.cs, and `ResultProblemDetailsMapper.cs` (two additive `Identity.Support.*` code→status entries — see the corrections below).

### Gate evidence (green at this revision)

- strict `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true` -> **0 warnings, 0 errors**.
- focused `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --filter "FullyQualifiedName~SupportTicketTests|FullyQualifiedName~SubmitSupportRequest|FullyQualifiedName~SupportInboxApiTests|FullyQualifiedName~MySupportTicketTests|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~OwnerLegalAudienceApiTests|FullyQualifiedName~LegalDocumentTests"` -> **81 passed, 0 failed**.
- `tools/Invoke-F5LocalHttpVerification.ps1` (loopback `http://localhost:5245`, dot-sources the L1 library `-LibraryOnly`, fail-closed all-context/non-live preflight, INSERT-only idempotent fixtures, own-session logouts, no reset/delete/bulk seed, no provider call) -> **exit 0, 32/32 checks passed, `passed=true`** (12 stage-one + 16 stage-two + 4 logout).

### Authored by the Mastermind (the worker's correction round under-delivered)

- `tests/Sanad.UnitTests/Identity/Support/AdminSupportTicketTests.cs`,
  `tests/Sanad.UnitTests/Identity/Support/MySupportTicketTests.cs`,
  `tests/Sanad.UnitTests/API/SupportInboxApiTests.cs` — the round-2 "new" files were
  `Assert.True(true)` placeholders (which is what made the claimed 86-passed count hollow).
  Rewritten with real assertions: numeric status contract, the exhaustive transition matrix,
  allowed/refused theories, reply durability, delivery-failure readback against a real seeded
  owner `User` row, own-ticket isolation and paging clamps, and the
  policy/route/verb/`AllowAnonymous`/DTO-shape reflection checks.
- `tools/Invoke-F5LocalHttpVerification.ps1` — the round-1/2 "adapter" was a 7-line
  placeholder. The real adapter (1681 lines) is self-contained, dot-sources only the L1
  library and uses F5-prefixed helpers (the F6 convention), port 5245, four dedicated
  INSERT-only fixture identities (`support.admin.f5@`, `family.owner.f5@`,
  `family.other.f5@`, `carehome.owner.f5@test.sanad.local`; free phone range
  `+201000000051`-`54`), copying the shared password hash and never touching shared fixtures.
- Docs: new `docs/admin/support-inbox.md`; `docs/auth/support-contact.md` (anonymous contact
  read, `GET /tickets`, `GET /tickets/{id}`); `docs/app/settings/help-support.md`
  (`CareHomeOwner` audience + ticket routes); `docs/admin/help-center.md` (five audiences,
  the old `CareHomeOwner = 5 -> 400` claim replaced); `docs/internal/operations/migrations.md`
  (migration row + worktree inventory line).
- Postman: `Sanad.Auth.postman_collection.json` "11. Support" +3 requests;
  `docs/postman/admins/Sanad.Admin.postman_collection.json` + folder "21. Support inbox
  (SuperAdmin/SupportAdmin)" (4 requests, `supportTicketId`/`supportTicketStatus` variables).
  Both re-validated as parseable JSON.

### Defects the runtime gate caught, corrected by the Mastermind

1. `AdminSupportTicketsController.ChangeStatus` declared
   `[ProducesResponseType(StatusCodes.Status204NoContent)]` but fell through to
   `ToActionResult(Result<bool>)`, which answers **200 `true`** for a `Result<bool>` success —
   contradicting its own attribute, the repo convention and the docs. Now
   `return result.IsSuccess ? NoContent() : ToActionResult(result);`.
2. `SupportController.GetMyTicketById` answered **400** for another user's ticket instead of
   the pinned 404: `Identity.Support.NotFound` had no `ResultProblemDetailsMapper` entry, so
   the mapper's unknown-code default (400, title "Bad Request") applied. The two F5 codes are
   now registered (`Identity.Support.NotFound = 404`, `Identity.Support.Conflict = 409`) —
   that dictionary is the code→status mechanism every other module uses, i.e. exactly the
   "existing 409 mechanism" this brief pointed at. The hand-rolled controller-local
   `ForcedProblem` copies were removed as redundant.
3. An intermediate regression from (2) — `ChangeStatus` returning `NoContent()`
   unconditionally, so a refused transition answered 204 while still writing nothing — was
   caught by the next adapter run and fixed before the green run. This is why the runtime gate
   is run to green rather than reported once.

Round-2 review item 2 above ("the conflict cannot surface as 409 through `ToActionResult`
alone") is therefore resolved inside the mapper rather than by a controller-local helper.

### Limitations (explicit)

- Every adapter run appends exactly one support ticket and one reply to the persistent local
  database. Both are retained history and are never deleted; the report states
  `ownerTicketCount` (0 -> 1 -> 2 across the runs).
- Trace correlation is reported `unavailable`: no captured local log line carries the trace id
  of a failed check at Warning-level logging.
- Delivery (email + in-app notification) is best effort and swallowed; the adapter proves the
  reply stays readable, never that a channel delivered. Development uses the no-op senders.
- Migrations stay disabled for every process the adapter starts, so
  `20261020191733_AddSupportTicketWorkflow` must already be applied — the read-only preflight
  fails closed on pending migrations.
- The owner read routes return plain `Guid` ticket ids while the older submit response keeps
  its typed `{ "value": ... }` id shape; both are documented as-is.

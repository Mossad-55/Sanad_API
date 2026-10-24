# Mobile report follow-up — locked next slice

## Control record

- Owner approved and locked this scope on 2026-10-24 and directed work to start
  immediately after the current portal-report release is completed and verified.
- Phase: approved; implementation has not started. Release: not_requested.
- Activation dependency: verify the release receipt for
  [the current slice](portal-report-follow-up-2026-10-24.md), then pin the actual
  released revision and remaining local delta before implementation.
- Mastermind must resolve the exact file manifest and Worker model at activation.
  Reuse these settled requirements; do not repeat product intake.
- Scope approval does not authorize commit/push/deploy, Production configuration
  or database writes, real charges/refunds, or changes in the Paymob merchant
  account. Obtain exact-target authority before those operations.

## 1. Recurring subscription Card enrollment

Existing enrollment code selects Card 3DS and sends `subscription_plan_id` when
the selected plan has a provider subscription plan mapping. The public
`subscriptionCardEnrollmentAvailable` flag currently checks Card enablement plus
the presence of SecretKey, CardIntegrationId and Card3dsIntegrationId. It is not
proof of valid provider configuration or per-plan availability. The reported live
false value has not yet been independently diagnosed.

Required work:

- Inspect effective configuration on the exact reported deployment without
  disclosing credentials; identify the actual reason for unavailable enrollment.
- Verify merchant recurring capability and the correct Card 3DS integration.
- Audit offered plan mappings against environment, price, currency and billing
  interval. Respect published-plan immutability/versioning; do not silently
  rewrite existing sold terms or historical subscriptions.
- Verify webhook configuration and HMAC validation.
- Prove checkout, 3DS, verified callback, persisted provider subscription identity
  and paid subscription. Separately prove renewal handling and duplicate-callback
  protection; initial payment alone does not establish recurrence.
- Record evidence and environment precisely. Sandbox evidence is not Production
  recurrence evidence. Configure the real prerequisites under separate target
  authority; never force the public flag to true.

No new endpoint is presently required. Any newly discovered behavioral change
outside this contract returns to the Owner. Wallet remains deferred/unverified.

## 2. Viewer-specific post capabilities

Add `isOwn`, `canEdit`, and `canDelete` boolean properties to the mobile
`CommunityPostResponse`, consistently across:

- `GET /api/v1/community/posts`
- `GET /api/v1/community/posts/{postId}`
- `GET /api/v1/community/recommendations`
- `GET /api/v1/community/favorites`
- Create/edit responses using the same DTO.

Locked semantics matching existing app mutation contracts:

- `isOwn`: authenticated user matches the stored author.
- `canEdit`: own post in PendingReview or Published.
- `canDelete`: own post that is not Deleted.
- Another viewer receives false for all three. Admin moderation is governed by
  its separate Admin routes; these flags do not grant app moderation powers.
- Compute using server-authenticated identity and stored ownership. Anonymous
  authors may see their own capabilities, but author identity, name and avatar
  remain hidden under the existing anonymity rules.
- Keep mutation authorization authoritative; flags describe the current snapshot
  and do not bypass validation or concurrent-state checks.
- Editing a published post returns it to pending review; document the resulting
  disappearance from published reads. Do not broaden unpublished-post visibility.

Acceptance: owner/other-viewer and anonymous/non-anonymous coverage on every read
surface, consistent create/edit responses, lifecycle/auth alignment, and updated
OpenAPI, consumer docs and Postman examples. No schema migration is expected.

### 2a. Viewer-specific post capabilities — task result

- Task status: `ready_for_review` (worker-owned). Phase and release states
  unchanged; Mastermind owns those.
- Base: `dd4eb19b` on `main` (docs-only commit after released `c1081620`).
  Parent-owned `Mastermind_Handoff.md` and repo debris untouched.
- Manifest (actual):
  - `src/API/Sanad.API/Controllers/CommunityPostsController.cs` — adds boolean
    `IsOwn`/`CanEdit`/`CanDelete` to `CommunityPostResponse`, one
    `ResolveCapabilities` rule, and threads the authenticated viewer id through
    every `ToResponse` call (feed, detail, recommendations, favorites, create,
    update).
  - `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs` — four tests: owner
    vs other viewer on feed + detail (incl. wire names), anonymous author keeps
    own flags with identity hidden, recommendations + favorites parity,
    ownership/lifecycle resolver table.
  - `tests/Sanad.UnitTests/API/CommunityPostManagementApiTests.cs` — three
    tests: create response on the `PendingReview` post, foreign-viewer read
    flags, generated OpenAPI schema; the existing update test additionally pins
    the Published→PendingReview edit response flags. Sender gained
    `CreatePostCommand`/`GetPostQuery` dispatch for the test context.
  - `docs/app/community/writes.md` + `docs/app/community/README.md` — the flag
    table, ownership/anonymity interaction, read-snapshot rule, and the
    edited-post disappearance from published reads.
  - `docs/postman/app/Sanad.App.Community.postman_collection.json` — feed,
    favorites, detail, create, update and recommendations descriptions.
- Semantics (identical on every surface that returns the DTO): `isOwn` =
  authenticated caller is the stored author; `canEdit` = own AND
  `PendingReview`/`Published`; `canDelete` = own AND not `Deleted`; another
  viewer and an empty viewer id get `false` for all three; anonymity changes no
  flag; moderation powers stay on the separate Admin routes; flags authorize
  nothing.
- Checks run in this session:
  - `dotnet build Sanad.slnx -c Release -p:TreatWarningsAsErrors=true` — PASS,
    0 warnings / 0 errors.
  - `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release
    --no-build --filter "FullyQualifiedName~Community"` — PASS, 267/267.
  - The seven new tests re-run by name — PASS individually.
  - `dotnet test tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj
    -c Release --no-build` — PASS, 1/1.
  - `git diff --check` — exit 0 (only `core.autocrlf` LF→CRLF notices).
- NOT RUN: the full ~5,523-test suite (focused filter plus the community/posts
  namespaces only); no live-host/API smoke beyond the in-process OpenAPI
  metadata test; no database write, provider call, migration, commit, push or
  deploy.
- Deliberately unchanged: mutation handlers, validation and concurrent-state
  checks; Admin moderation DTOs/routes and the Admin post response shapes; all
  other response shapes; no schema migration, no new endpoint, no configuration
  flag.
- Next action: Mastermind review of the actual diff against this contract;
  release remains separately unauthorized.

## 3. Completion notes contract

The accepted request is `POST /api/v1/caregiver/bookings/{bookingId}/complete`
with `{ "notes": "..." }`; notes are optional and success is 204 No Content.
The mobile app already sends the correct name.

- Correct the stale `caregiverNotes` request in
  `docs/postman/app/Sanad.App.Caregiver.postman_collection.json` to `notes`.
- Clarify request `notes` versus booking-detail response `caregiverNotes`; retain
  the existing response field. Do not introduce a needless API rename or alias.
- Verify the submitted notes persist and are returned through booking readback;
  a success status alone is insufficient because notes are optional.

### 3a. Completion notes contract — task result

- Task status: `ready_for_review` (worker-owned). Phase and release states
  unchanged; Mastermind owns those.
- Base: `dd4eb19b` on `main`, same pinned base as section 2a; the parent-owned
  `Mastermind_Handoff.md`, the community/M1 files and all sibling worker edits
  were left untouched (verified via `git status`).
- Manifest (actual):
  - `docs/postman/app/Sanad.App.Caregiver.postman_collection.json` — the
    "Complete visit" body now sends `{ "notes": ... }` (same Arabic example
    text); its description states the optional `notes` request key (body may be
    omitted/`{}`), 204 success, and the `caregiverNotes` readback.
  - `docs/app/caregivers/bookings.md` — new "Completion notes: request `notes`,
    readback `caregiverNotes`" subsection after the status rules: optional
    request key `notes` (≤ 2000), success `204`, and the text reads back as
    `caregiverNotes` on both booking-detail projections; one field, two names
    by contract, no rename/alias, `caregiverNotes` in the request binds nothing.
  - `docs/app/families/bookings.md` — one clarifying sentence on the booking
    detail paragraph (the only place the family doc surfaces `caregiverNotes`;
    it does not show the complete call itself).
  - `tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs` — two tests:
    completing via `CaregiverStartBookingCommandHandler` +
    `CaregiverCompleteBookingCommandHandler` with notes, then asserting the
    caregiver detail projection and the family booking detail projection both
    carry the same submitted text; and completing with omitted notes reading
    back `null` through both projections. Existing in-memory patterns reused.
  - `tests/Sanad.UnitTests/API/AdminOperationalReadAuthorizationTests.cs` — one
    OpenAPI assertion on the real served document: the complete route is
    POST-only, its request schema is `CompleteBookingRequest` with `notes` and
    no `caregiverNotes`, while `BookingDetailResponse` keeps `caregiverNotes`
    and publishes no top-level `notes`.
- No product/source change: controller binding, command, entity
  (`Booking.CaregiverNotes`) and both read projections are untouched; no
  response field was renamed, no alias, no schema migration, no new endpoint,
  no configuration flag.
- Checks run in this session:
  - `dotnet build Sanad.slnx -c Release -p:TreatWarningsAsErrors=true` — PASS,
    0 warnings / 0 errors.
  - `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release
    --no-build --filter "FullyQualifiedName~BookingDetail|FullyQualifiedName~OpenApi_CompletionNotes"`
    — PASS, 13/13.
  - The three new tests re-run by name — PASS 3/3
    (`Completion_notes_persist_and_read_back_through_both_booking_detail_projections`,
    `Completion_without_notes_is_allowed_and_reads_back_null_through_both_projections`,
    `OpenApi_CompletionNotes_BindNotesOnTheRequestAndKeepCaregiverNotesOnTheResponse`).
  - `git diff --check` — exit 0 (only `core.autocrlf` LF→CRLF notices).
  - Postman collection re-validated as JSON after the edit — OK.
- NOT RUN: the full suite (focused booking-detail + new-test filters only, per
  contract); no live-host/API smoke beyond the in-process OpenAPI metadata
  host; no database write, provider call, migration, commit, push or deploy.
- Next action: Mastermind review of the actual diff against this contract
  (together with section 2a); release remains separately unauthorized.

## Mastermind acceptance (2026-10-24)

Status: **locally accepted; release NOT authorized.** Activation pins the slice
to released revision `c1081620` (docs commit `dd4eb19b`); the owner-approved
scope is unchanged.

| Phase | Worker | Result |
|---|---|---|
| M1 viewer capabilities | River DeepSeek V4.1 Flash `#high` | accepted |
| M3 Card enrollment diagnosis | River GLM 5.3 Flash `#high` (read-only) | accepted at evidence level |
| M2 completion notes | River GLM 5.3 Flash `#high` | accepted |

M1 manifest: `src/API/Sanad.API/Controllers/CommunityPostsController.cs` (single
`ResolveCapabilities` rule threaded through feed, detail, recommendations,
favorites, create and update), `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs`,
`tests/Sanad.UnitTests/API/CommunityPostManagementApiTests.cs`,
`docs/app/community/README.md`, `docs/app/community/writes.md`,
`docs/postman/app/Sanad.App.Community.postman_collection.json`.

M2 manifest: `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`,
`docs/app/caregivers/bookings.md`, `docs/app/families/bookings.md`,
`tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs`,
`tests/Sanad.UnitTests/API/AdminOperationalReadAuthorizationTests.cs`.

Mastermind-run gates (whole solution, after every worker finished):

- `dotnet build Sanad.slnx -c Release -p:TreatWarningsAsErrors=true` — 0 warnings / 0 errors.
- `dotnet test tests/Sanad.UnitTests -c Release --no-build` — **5,533 passed, 26 skipped, 0 failed** (4 m 17 s), i.e. +10 tests over the pre-slice 5,523.
- `dotnet test tests/Sanad.ArchitectureTests -c Release --no-build` — 1/1 passed.
- `git diff --check` — exit 0; both touched Postman collections re-validated as JSON.
- Exact-diff review: one capability rule with no duplicated logic, mutation
  authorization untouched, Admin DTOs and routes untouched, anonymity path
  unchanged, completion request stays `notes` while the readback stays
  `caregiverNotes` (no alias), no migration, no new endpoint, no flags.

## M3 evidence summary and gated decisions

Accepted evidence (worker read-only; production SELECT-only; no writes, no
provider calls, no secret values): `subscriptionCardEnrollmentAvailable` is
false because `Paymob__Card3dsIntegrationId` is absent from the live
environment. The flag is `IsCardAvailable && CardSubscriptionEnrollmentConfigured`
(`src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs:156`,
`src/API/Sanad.API/DependencyInjection.cs:849-851`), and the Card intent gate
correctly refuses instead of failing silently. Mastermind spot-checked both code
points and the live env key list.

Recorded, not acted on: provider plan id `6755` is shared by two monthly plans
and one annual plan; `gold` and the 2000-EGP annual plan carry no provider
mapping; the configured webhook URL is `http` with no TLS and
`Paymob__RedirectionUrl` is absent; no renewal scheduler exists, so recurrence
depends on Paymob charging the provider subscription and is unproven with zero
production subscription rows. Acting on any of these needs separate authority
(merchant account, Production configuration plus restart, plan-version
decisions).

### Owner-approved live probes (after release)

The owner then authorized committing to the slice and M3 remediation. Read-only
probes were run on the live host with the configured credentials — no secret
printed, no charge, no merchant write, no config change:

| Probe | Result |
|---|---|
| Key shape | `egy_sk_test_…` — the Production host holds a **test-mode** key |
| Plain Card intention (integration from `Paymob__CardIntegrationId`) | **201**, `client_secret` returned, `payment_keys[].integration = 5897206` |
| Intention with `subscription_plan_id = 6755` | **404** `invalid subscription plan id` |
| Intention with `subscription_plan_id = 6205` | **404** `invalid subscription plan id` |
| Intention with `subscription_plan_id = 999999` (control) | **404** `invalid subscription plan id` |
| Subscription/plan admin surfaces with the host secret key | **401** with both `Token` and `Bearer` auth |

Interpretation: the Card integration itself is valid, but **no mapped provider
subscription plan exists for the account the host uses** — the mapping is
fiction. `Paymob__Card3dsIntegrationId` therefore stayed unset: flipping it
would advertise enrollment that fails at Paymob. Failing closed is the correct
behaviour, and forcing the flag is forbidden by this contract.

Remediation is blocked on **merchant-account access** (dashboard, or an
`api_key` the subscription API accepts), not on code:

1. Create one provider subscription plan per sold term in the merchant account
   and capture its id.
2. Repoint the local plan mappings through new immutable plan versions — never
   rewrite sold terms or historical subscriptions.
3. Set `Paymob__Card3dsIntegrationId` and restart; the public flag should flip
   on its own.
4. Decide test-vs-live Production keys and `https` webhook + `RedirectionUrl`
   (needs a domain and certificate).
5. Then run the sandbox end-to-end proof (checkout → 3DS → verified callback →
   persisted provider subscription → renewal → duplicate callback). Production
   recurrence still needs one real charge under separate authority.

## Completion and next action

Released as `f790ff57` on 2026-10-24 and verified on Production (see
[the release receipt](../reports/mobile-report-follow-up-release-2026-10-24.md)).
Implementation, tests and consumer contracts are complete and accepted. The
remaining M3 items are owner-gated merchant-account and infrastructure
decisions, not pending implementation work.

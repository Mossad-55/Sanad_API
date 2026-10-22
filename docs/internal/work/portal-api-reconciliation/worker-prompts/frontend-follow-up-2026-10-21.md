# Frontend / mobile follow-up 2026-10-21 — implementation brief

**PLAN LOCKED — scope approved 2026-10-21. Planning complete; every task is
`pending`; release `not_requested`. NOTHING IS IMPLEMENTED OR DEPLOYED.** No code,
test, config, migration, database, provider or deploy action has been taken, and
none is authorized by this turn. The Owner decisions below are final (recorded,
not reopened); dispatch occurs only when the parent dispatches a wave and names a
model. The Mastermind coordinates and edits nothing.

This single active brief owns the consolidated follow-up scope. It does not
duplicate the handoff or the contracts; it links them. **This brief is the shared
status ledger**: no two workers write it concurrently — each phase worker hands
back its result, and **one assigned worker applies a single serial update** to
this brief after the handbacks. Product/consumer docs remain the **phase worker's
own responsibility** (no separate documentation relay). The parent does not edit
it.

## Environment premise (owner direction, 2026-10-21)

- **The existing VPS is Production.** There is no development/staging target and
  no domain/HTTPS provisioning in scope. **Ignore domain provisioning and
  staging entirely** — remove them from scope, prerequisites and decisions.
- Runtime verification for these tasks runs against the **persistent local
  `SanadDb`** under the existing L1 fixture rules (no reset, ≤10 synthetic rows
  per feature table). No task requires a new host, domain, certificate or deploy.
- **Preview and revalidation-webhook work are separate *frontend feature
  requests*, dispositioned explicitly below — they are neither backend tasks,
  prerequisites nor decisions, and carry no invented deadline.**
- Confirming whether a Paymob integration id is present is a **separately
  authorized runtime check** (presence only, no values). It is not a blocker to
  designing or writing the T1 tests.

## Read first

1. Repository `AGENTS.md` and `docs/internal/governance/AGENTS.md`.
2. `docs/internal/Mastermind_Handoff.md` (parent-owned; **not** edited by this task).
3. Scope references (do not re-approve): `contracts/frontend-follow-up-2026-10-20.md`,
   `frontend-clarifications-2026-10-20.md`, and `frontend-report-audit-2026-10-16.md`.
4. This brief. Inspect the pinned revision and worktree before editing; preserve
   every pre-existing/unrelated edit.

## Pinned revision, local delta, and manifest validation

- HEAD (this planning pass): `4fa7c77e3544abb0650aa5baf6c24358a1b48654`.
- Worktree is dirty. **Preserve, do not stage/touch:** `.codex/**`, `README.md`,
  `docs/internal/governance/AGENTS.md`, `tools/opencode_bridge/opencode_bridge.py`,
  `.opencode-audit-tmp/`, `.opencode/agents/`, `buildout*.txt`, `testout*.txt`,
  `migout.txt`, `e07-library-smoke.ps1`, `workflow-audit/`,
  `contracts/frontend-follow-up-2026-10-20.md`, `frontend-clarifications-2026-10-20.md`,
  the `e03-*.md` prompts, and `worker-prompts/frontend-follow-up-2026-10-21.md`.
- **Writable scope of this planning task:** this file only. No other file was
  created or modified.
- **Honest validation of the manifests:** every *existing* path below was
  existence-checked against the pinned revision. Paths marked **NEW** are
  **planned additions that do not exist yet** and are asserted only as intended
  locations from the surrounding module convention. EF **migration filenames
  cannot be pinned** in advance: each migration task must (a) inspect the target
  context's current latest migration (predecessor check) before generating,
  (b) reserve a unique migration identifier, and (c) own the generated
  `<Name>.cs`, its `<Name>.Designer.cs`, and the context `*ModelSnapshot.cs` edit.
  Claims of "all references verified" are not made for planned additions.

## Approved scope

Payment availability truthfulness; the resident-visit eligibility read;
deterministic multi-account legal/help audience; admin operational enrichment;
mobile read completeness (all reported items, including the caregiver booking
detail projection); community **one-way hide-posts-only** blocking; Landing
bilingual shape, live stats, testimonial preservation and **SVG upload/serving
(approved)**; the new public surfaces the Owner requested — **partner contact,
public legal, navbar/SEO site settings (approved)**; subscription bilingual
display copy; documentation restorations; and the F1/F2/D1 corrections.

## Excluded / deferred (settled — do not reopen)

- **Community block = hide posts only, one-way (viewer blocks author).** The
  block is a **Community-owned relationship**. Chat `Block` code patterns may be
  mirrored, but **no write to Chat tables, no Chat semantics change, no chat
  data read**. No mutual hide, no comment/interaction denial.
- **Anonymous identity stays hidden.** No block-by-anonymous-author addressing,
  no linkability, no new ability to discover an anonymous author.
- **Bilingual nested CMS** (Arabic AND English, including nested content) is
  approved. Raw-vs-typed `contentJson` is a settled implementation choice.
- **Partner contact destination is settled: the existing admin support inbox**,
  with rate-limit + idempotency + minimal anti-spam on a proper application-layer
  command (no fake identity, no API-layer persistence shortcut).
- **Testimonials preserve genuine ratings and privacy** — no fabricated partner
  role/endorsement. Partner *editorial* testimonials are a separate optional
  content decision, only if the Owner explicitly wants them (not a blocker).
- **Optional Landing SVG is answered YES** and **navbar/SEO inclusion is
  approved** — both are design tasks with concrete manifests below, not Owner
  approvals.
- Growth beyond registration is settled (E01 registration growth exists). No
  existing-row deletion, no data repair, no policy relaxation; existing enum
  values accepted as-is; T2 `startsAt` required (not "now"); no OTP/reactivation
  or Draft-finance reopening; no payment-provider call.
- **Preview and revalidation** (see "Frontend feature requests" below).
- No build/test/migration/provider call in the planning pass.

## Frontend feature requests to disposition explicitly (not infrastructure)

| Request | Current backend truth | Disposition |
|---|---|---|
| Preview (draft Landing/family-home preview) | Content is Draft/Published/Archived; public reads serve `Published && IsVisible` only (`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetPublishedLandingSectionsQueryHandler.cs`). No preview read exists. | **Separate frontend feature request.** Needs an Owner-approved authenticated preview surface; **no deadline**, not a backend prerequisite for T1–T11. |
| Revalidation webhook (Next ISR) | No revalidation endpoint exists. Landing `/sections` and `/stats` are computed per request (`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetLandingStatsQueryHandler.cs`). | **Separate frontend feature request.** A 60-second ISR freshness window is an accepted frontend approximation (`T8`); no webhook, **no deadline** here. |

## Verified source findings (this pass)

All paths below are existing and were read/checked now. `[ABSENT]` = no source
match found, recorded as a gap, never invented.

1. **Payment.** `GET /api/v1/platform/status` `availablePaymentMethods` =
   `CardEnabled && CardConfigured` / `WalletEnabled && WalletConfigured`
   (`src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs`).
   Subscription intent uses the same generic pair
   (`src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingPaymentCommands.cs`)
   then adds a path-specific gate → `400 Platform.PaymentMethodUnavailable`
   (`src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`).
   `CardConfigured` = SecretKey + `CardIntegrationId`; `CardSubscriptionEnrollmentConfigured`
   = SecretKey + `Card3dsIntegrationId`
   (`src/API/Sanad.API/DependencyInjection.cs`,
   `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/PaymobOptions.cs`,
   `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Payments/PaymobClient.cs`).
   **Code-contract gap** (advertised vs per-flow enforcement), not a cache/enum bug.
2. **Resident visit.** `POST /api/v1/family/care-home-visits` kind `Resident=2`
   requires Accepted + Paid + `StartDate <= cairoLocalDate(startsAt)` <
   `EndDate` + not checked out, else `404 CareHomes.Visits.NotFound`
   (`src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/CareHomeVisitHandlers.cs`).
   The family-facing care-home booking list `CareHomeFamilyBookingListItem` omits
   `ElderlyId` (`src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Bookings/CareHomeBookingCommands.cs`),
   so no current read lets a client pick an eligible resident. This is the
   **resident lookup** and fully answers mobile item 9 (see map).
3. **Audience.** Multi-account supported
   (`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Users/AccountChoices.cs`);
   a Normal JWT carries **one `account_type` claim per owned account** in
   `user.Accounts` order
   (`src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Security/JwtAuthTokenService.cs`).
   Readers use `User.FindFirst`
   (`src/API/Sanad.API/Controllers/ApiControllerBase.cs`) → order-dependent.
   `SwitchMyAccountCommand` does not reissue a JWT. `LegalAudience.CareHomeOwner = 5`
   vs `AccountType.CareHomeOwner = 8`.
4. **Admin.** Ticket DTOs expose `UserId` only
   (`src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminSupportTicketQueries.cs`).
   `F1` = `src/API/Sanad.API/Controllers/AdminSupportTicketsController.cs` returns
   404 through the mapper but declares no `404 [ProducesResponseType]`.
5. **Community.** `CommunityPostResponse` has no author fields
   (`src/API/Sanad.API/Controllers/CommunityPostsController.cs`); comment/reply
   responses carry `AuthorId` only
   (`src/API/Sanad.API/Controllers/CommentsController.cs`);
   `GetCommunityAuthorNamesAsync` has **no avatar**
   (`src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`).
   Comments/replies have **no anonymity flag**. Chat `Block` exists
   (`src/Modules/Chat/Domain/Sanad.Modules.Chat.Domain/Blocks/Block.cs`,
   `src/Modules/Chat/Infrastructure/Sanad.Modules.Chat.Infrastructure/Persistence/Configurations/BlockConfiguration.cs`).
6. **Landing + uploads.** 11 stored types + virtual `Stats`
   (`src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Landing/LandingSectionType.cs`).
   Public item = bilingual title/body + **raw** `contentJson` + `imagePath` +
   resolved `testimonials`/`faqs`
   (`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingSectionResponses.cs`;
   `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingRefResolver.cs`).
   Anonymous `GET /api/v1/landing/stats` → `approvedFacilitiesCount`
   (`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetLandingStatsQueryHandler.cs`)
   — the **live facility count location**. Landing media upload is
   `POST /api/v1/admin/landing/media` → `SaveImageAsync` → `HasMatchingImageSignature`
   allowlist **jpeg/png/webp only**
   (`src/API/Sanad.API/Controllers/AdminLandingController.cs`). Storage allowlist
   is the same three types
   (`src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs`).
   Public files are served by `UseStaticFiles` at `/files` with a `private/` guard
   (`src/API/Sanad.API/ApplicationBuilderExtensions.cs`).
7. **Subscription copy.** Plan versions carry `key/version/price/cycle/currency`
   + numeric benefit keys + limits + `rollover` — **no bilingual
   name/description/benefit text**
   (`src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionPlanVersion.cs`;
   authoring `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionCommands.cs`
   `CreateSubscriptionPlanVersionCommand`; product read
   `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionQueries.cs`;
   admin read
   `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/AdminSubscriptionQueries.cs`).
8. **Financial/payout.** `CaregiverPaymentPolicyResponse.MinimumPayoutAmount`/
   `ExpectedProcessingDays` are always null (honest-null)
   (`src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Payouts/CaregiverPayoutReadQueries.cs`);
   family booking detail carries `BaseCaregiverFee`/`PlatformFeeAmount`/
   `TotalPayableAmount`/`Currency`
   (`src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingQueries.cs`).
9. **Caregiver booking detail + visits/reviews.** `GET /api/v1/caregiver/bookings/{bookingId:guid}`
   → `BookingDetailResponse` via `GetCaregiverBookingDetailQuery` /
   `GetCaregiverBookingDetailQueryHandler` / `BookingDetailMapper`
   (`src/API/Sanad.API/Controllers/CaregiverBookingsController.cs`,
   `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/CaregiverBookingQueries.cs`).
   Real records exist for the three requested members: `VisitReport`
   (`src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Reports/VisitReport.cs`,
   read by `src/Modules/Families/Application/Sanad.Modules.Families.Application/Reports/VisitReportQueries.cs`),
   `MedicalReport`
   (`src/Modules/Families/Application/Sanad.Modules.Families.Application/Reports/MedicalReportQueries.cs`),
   and `BookingReview`
   (`src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Bookings/BookingReview.cs`).
   There is **no scheduled-visit table for home bookings**. The source/mapping for
   `upcomingVisits` is **undecided** (pending mobile MOB-09): **no promise** is made
   to map other/future bookings as visits, and no synthetic schedule is invented.
10. **Support ticket persistence.** `SupportTicket.Create` **requires a non-empty
    `UserId`** and the current command resolves an authenticated `User`
    (`src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/SupportTicket.cs`,
    `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/SubmitSupportRequestCommandHandler.cs`,
    `src/API/Sanad.API/Controllers/SupportController.cs`) — so anonymous partner
    contact needs its own persisted aggregate (design below).
11. **[ABSENT]** navbar/SEO/site-settings surface; **`/choose-type`** route/spec.
    Recorded as gaps, not invented.

## Mobile report item map (items 1–10)

Every item maps to a real endpoint/field or to a fully-sourced projection. Field
names are the actual C# members.

| # | Item | Endpoint(s) / fields | Source status | Task |
|---|---|---|---|---|
| 1 | Author fields on posts/detail/recommendations/comments/replies, incl. anonymity | `GET /api/v1/community/posts` (`CommunityPostResponse`, `IsAnonymous`), comments/replies responses, `GET /api/v1/community/recommendations` | `authorId` on comments/replies only; **no `displayName`/`avatarUrl`**; comments/replies lack an anonymity flag | T5 |
| 2 | Caregiver list/detail images | `GET /api/v1/caregivers` (`CaregiverSearchCardResponse.AvatarUrl`), `GET /api/v1/caregivers/{id}` (`CaregiverPublicProfileResponse.AvatarUrl` + `VerifiedCertificates`) | Present; confirm certificate-file serving is public-safe | T5 |
| 3 | Library four reads + real playable sample + cover + auth | `GET /api/v1/library/categories`, `/content`, `/content/{id}`, `/recommendations` (+ `/saved`) | Covers under `library/` via `GET /files/{key}`; `VideoUrl` external + `DurationSeconds`; premium gating (`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryPremiumAccess.cs`) | T5 |
| 4 | Subscription plan copy | `GET /api/v1/family/subscriptions/plans` | **Resolved by T10** — version-owned `arabicName`/`englishName`/`arabicDescription`/`englishDescription` plus per-benefit `arabicLabel`/`englishLabel`; all nullable (absent when not authored) | T10 |
| 5 | Booking list/detail: elderly DOB/gender/avatar, family names/avatar, emergency contact w/ access | `GET /api/v1/family/bookings[/{id}]` (`BookingDetailResponse.Elderly`), caregiver detail, `GET /api/v1/family/dependents/{id}/emergency-contact` | DOB/gender exist; avatar/photo only on own-elderly profile; family name/avatar only on family profile; emergency contact only on family route (no access/status model) | T5 |
| 6 | Medical context (caregiver booking) | Elderly/family medical profile, medications, `src/API/Sanad.API/Controllers/DependentMedicalAccessGrantsController.cs`, `src/API/Sanad.API/Controllers/AdminCaregiverMedicalAccessAuditController.cs` | Routes exist; map exact route→field and authorization | T5 |
| 7 | `visitHistory` / `upcomingVisits` / `familyReview` (nullable) | `GET /api/v1/caregiver/bookings/{bookingId}` detail | `visitHistory`/`familyReview` from real `VisitReport`/`MedicalReport`/`BookingReview` (verified at build time); **`upcomingVisits` source undecided pending MOB-09** — no promise to map future/other bookings as visits | T5 |
| 8 | Financial amounts + unavailable-payout null | Payout reads, booking `TotalPayableAmount`/`PlatformFeeAmount` | Present; "unavailable" = honest-null policy | T5 |
| 9 | Residents | `GET /api/v1/family/care-home-visits/eligible-residents` (T2) | **Resolved by T2** (same predicate, required `startsAt`); not duplicated | T2 |
| 10 | Payments | `GET /api/v1/platform/status`, booking/subscription intents, `POST /api/v1/payments/webhooks/paymob` | Present; the availability gap is T1 | T1 |

## Documentation-consistency restorations (task T6)

Doc restorations against source plus the E07/E08 verification-boundary text
(T6 also changes the E07 guard tool, so it is **not** docs-only); the docs listed
here do **not** overlap any code-wave doc:
`docs/admin/ratings-moderation.md` (care-home rating enum + owner anonymity/flag
rules), `docs/admin/family-home.md` + `docs/app/families/home.md` (kinds/targets),
`docs/app/chat/conversations.md` (chat scope/booking ID),
`docs/app/elderly/check-in.md` (profile-local date),
`docs/auth/registration-and-verification.md` (registration/verification),
`docs/app/caregivers/schedule-and-availability.md` (integer `dayOfWeek`), and the
SVG assessment-illustration prose defect (audit item #46; storage accepts
JPEG/PNG/WebP). The community writes doc and the caregiver Postman are owned by
their code task (T5), not here, to remove the Wave-3 collision.

## Dependency-ordered tasks

Paths are **exact**; **NEW** = planned addition (does not exist yet). No ellipsis
or glob appears in a manifest. Generated migration/designer/snapshot files are
owned by their task (see "Manifest validation").

### T1 — Payment availability truthfulness (FIRST EXECUTABLE) — `accepted`
- **Objective:** truthful **per-flow** availability using the **same evaluator**
  as the intent gate; preserve the generic `/platform/status` list; disabled
  precedence; **no internal provider-config field** exposed.
- **Design:** extract one evaluator (e.g. `PaymentMethodAvailability`) used by
  both `PlatformControlCommands` and `SubscriptionPaymentCommands`. Status keeps
  `availablePaymentMethods` and gains **only** a public-safe per-flow signal;
  never names integration ids. Docs must state the **mobile consumption
  contract** in words (a lone boolean/caveat is insufficient). **No claim the
  current failure is fixed.**
- **Manifest:** `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`;
  `src/API/Sanad.API/Controllers/PlatformStatusController.cs`;
  `src/API/Sanad.API/DependencyInjection.cs` (only if exposure needs it);
  `docs/app/public/platform-status.md`; `docs/app/families/subscriptions.md`;
  `docs/postman/app/Sanad.App.Public.postman_collection.json`;
  `docs/postman/app/Sanad.App.Family.postman_collection.json`;
  tests `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs`,
  `tests/Sanad.UnitTests/API/PlatformControlsControllerTests.cs`,
  `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`,
  `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentTests.cs`.
- **Acceptance:** a unit regression proves status and intent cannot contradict for
  the enrollment branch; disabled precedence holds; no provider call; docs state
  the consumption contract.
- **T1 result (Worker A):** status `accepted`. Changed files:
  `src/Modules/Finance/Application/Sanad.Modules.Finance.Application/PlatformControlCommands.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingPaymentCommands.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionPaymentCommands.cs`;
  `docs/app/public/platform-status.md`;
  `docs/app/families/subscriptions.md`;
  `docs/postman/app/Sanad.App.Public.postman_collection.json`;
  `docs/postman/app/Sanad.App.Family.postman_collection.json` (payment-scoped description only; B also edited this file for legal/help);
  `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs`;
  `tests/Sanad.UnitTests/API/PlatformControlsControllerTests.cs`;
  `tests/Sanad.UnitTests/API/PortalOpenApiContractTests.cs`;
  `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPaymentTests.cs`.
  Build: `dotnet build Sanad.slnx -c Release` → exit 0, 0 warnings, 0 errors.
  Tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~PlatformControlsTests|FullyQualifiedName~PortalOpenApiContractTests|FullyQualifiedName~SubscriptionPaymentTests"` → exit 0, Total 157, Passed 157, Failed 0, Skipped 0.
  New tests: `Public_status_subscription_card_enrollment_availability_is_card_and_enrollment_conjunction` (8 cases, asserts enrollment bool = cardEnabled && cardConfigured && enrollmentConfigured, generic Card availability iff cardEnabled && cardConfigured, Wallet independently from walletConfigured); plus five parity tests (`Discovery_and_initial_intent_agree_for_mapped_plan_with_enrollment_enabled`, `_for_mapped_plan_with_enrollment_disabled`, `_for_unmapped_plan_with_enrollment_disabled`, `_when_generic_card_is_unavailable`, `_wallet_unaffected_by_enrollment_flag`) that call both `GetPublicPlatformStatusQueryHandler` and `CreateSubscriptionPaymentIntentCommandHandler` with the same `FixedPlatformControls` and assert status/intent agreement, provider call counts, and null/non-null `SubscriptionPlanId`.
  Limitations: unit/in-memory proof only with a faked Paymob client; no provider call or external call; exact Paymob integration presence in the reporting environment remains UNVERIFIED (separately authorized runtime check); status field is advisory for UI branching only while intent still enforces `400 Platform.PaymentMethodUnavailable`; historical Oct-20 defect is NOT claimed fixed. Session ses_ea0e2df07ffeAF0PQaocPTdRBI, HEAD 4fa7c77e3544abb0650aa5baf6c24358a1b48654.
  Next action: independent acceptance review.
- **T1 verification (independent reviewer ses_ea0d1a7f5ffel8R7a4GwCYdX4c):** `dotnet build Sanad.slnx -c Release` → 0 warnings, 0 errors; focused filter (PlatformControlsTests|PortalOpenApiContractTests|SubscriptionPaymentTests) → 157 passed / 0 failed / 0 skipped; both prior test defects confirmed fixed; accepted source unchanged; historical Oct-20 defect still NOT claimed fixed and Paymob integration presence still UNVERIFIED.

### T2 — Resident visit eligibility (required `startsAt`) — `accepted` (Wave 2 closeout: "T2 and T4 accepted"; the delivery bullet above is the handback wording)
- **Objective:** one authorized read returning the caller's residents eligible for
  the **requested** facility/window via the **same** predicate as create.
  **`startsAt` required** (no "now" fallback).
- **Design:** `GET /api/v1/family/care-home-visits/eligible-residents?facilityId=&startsAt=`
  → `{ elderlyId, arabicName, englishName, careHomeBookingId? }` for the caller's
  `FamilyId`; membership miss returns the safe existing diagnostic; no policy
  relaxation. This is also mobile item 9.
- **Manifest:** `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/CareHomeVisitHandlers.cs`;
  `src/API/Sanad.API/Controllers/CareHomeVisitsController.cs`;
  `docs/care-homes/visits.md`;
  `docs/postman/app/Sanad.App.Family.CareHomes.postman_collection.json`;
  `tests/Sanad.UnitTests/CareHomes/CareHomeVisitTests.cs`.
- **T2 result (Worker A):** ready_for_review, not completed. Route GET /api/v1/family/care-home-visits/eligible-residents?facilityId=&startsAt= (FamilyAccess), response { elderlyId, arabicName, englishName, careHomeBookingId } (careHomeBookingId non-nullable; one deterministic booking per resident, chosen by Arabic name, English name, elderly id, then booking id). Shared predicate CareHomeVisitResidentEligibility.Eligible used by both create and read. A missing or malformed value is rejected by ASP.NET model binding as a framework 400; only an all-zero facilityId or default startsAt reaches the handler and returns 400 CareHomes.Visits.InvalidRequest. Membership miss returns controller NotFound before handler. The read filters by requested facility only and does not re-assert facility approval (documented divergence; no cross-family disclosure). Build: dotnet build Sanad.slnx -c Release -> exit 0, 0 warnings, 0 errors. Tests: dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter FullyQualifiedName~CareHomeVisitTests -> exit 0, Total 13, Passed 13, Failed 0, Skipped 0. New tests: Eligible_residents_returns_only_accepted_paid_unchecked_out_bookings_for_caller_family; Eligible_residents_excludes_other_facility_not_accepted_not_paid_bad_window_and_checked_out; Eligible_residents_returns_deterministic_order_for_multiple_residents; Eligible_residents_invalid_request_for_empty_facility; Eligible_residents_invalid_request_for_default_startsAt; Eligible_residents_parity_create_accepts_exactly_the_read_result; Eligible_residents_route_uses_FamilyAccess_policy. Limitations: in-memory unit proof only; no HTTP/runtime evidence; membership-miss 404 and framework model-binding 400s remain untested; Paymob/DB/runtime not involved. Next action: independent acceptance review. Locked-design deviation: the brief annotated the field careHomeBookingId?, while the implementation declares non-nullable Guid because the authoritative predicate guarantees a matching booking; the wire value is unchanged for every returned resident. Warning scope note: the T2 revision builds 0 warnings/0 errors; the current worktree build is also 0 warnings/0 errors (reproduced twice), so the earlier note naming T4-owned warnings is superseded.


- **Wave 2 closeout:** T2 and T4 accepted. Verification sessions: T2 ses_ea0d1a7f5ffel8R7a4GwCYdX4c (build 0 warnings/0 errors, 13/13 CareHomeVisitTests); T4 ses_ea0d872efffe33sfnrQXqZ8Teg (build 0 warnings/0 errors, 56/56 tests). Both gates reproduced; nothing committed or deployed.

Carried-forward OPEN items (not implemented):
1. src/API/Sanad.API/Controllers/SupportController.cs GetMyTicketById (~line 116) can return Identity.Support.NotFound (MySupportTicketQueries.cs:87) but declares no 404 metadata - same class as F1, outside T4's mandate.
2. Build-configuration hygiene quirk: the three Sanad.Modules.Chat.* projects are absent from Sanad.slnx and can emit into in\Debug while the rest of the graph emits Release, so a Release output tree can carry Debug-compiled Chat assemblies - out of T4's manifest, untouched, and it did not affect the 0/0 gate.
3. Optional test-gap items: no exact OpenAPI property pin for the admin visit detail schema, and the empty-page zero-query property is pinned hard only for the family half.


### T3 — Deterministic multi-account audience — `accepted`
- **Objective:** remove claim-order dependence; optional `audience` accepted only
  when owned (`400` malformed, `403` not-owned).
- **Default (recommended):** omitted `audience` with **multiple eligible** → coded
  `400 AudienceRequired`; **exactly one** eligible → current behavior (compat
  confirmation is Owner item O-1).
- **Manifest:** `src/API/Sanad.API/Controllers/ApiControllerBase.cs`;
  `src/API/Sanad.API/Controllers/LegalController.cs`;
  `src/API/Sanad.API/Controllers/HelpCenterController.cs`;
  `docs/app/settings/legal.md`; `docs/app/settings/help-support.md`;
  tests `tests/Sanad.UnitTests/API/OwnerLegalAudienceApiTests.cs`,
  `tests/Sanad.UnitTests/API/ApiControllerBaseTests.cs`,
  `tests/Sanad.UnitTests/Cms/HelpCenterTests.cs`.
- **T3 result (Worker B — session `ses_ea0e27e04ffeY1E14YEcikIRSc`; independent
  reviewer `ses_ea0d872efffe33sfnrQXqZ8Teg`): `accepted`, not completed.
  Core scope was independently reviewed as acceptable (no production caller
  outside legal/help of the modified helper); this turn completed the bounded
  corrections, the Owner-approved consumer-doc amendment and the first real
  build/test execution now that Worker B holds the sole runtime/build slot.**
  - **Changed paths (16).** Source: `ApiControllerBase.cs` (owned-audience set,
    `TryResolveContentAudience`, `TryParseContentAudience`,
    `MapAccountTypeToAudience` by **name** — `AccountType.CareHomeOwner = 8` maps
    to `LegalAudience.CareHomeOwner = 5`, never cast; removed the duplicate
    `TryGetLegalAudienceFromClaims`), `LegalController.cs`,
    `HelpCenterController.cs` (optional `[FromQuery] string? audience`, resolver,
    400 `ProducesResponseType`, XML `<param name="audience">` naming `1`–`5` incl.
    CareHomeOwner). Tests: `ApiControllerBaseTests.cs`,
    `OwnerLegalAudienceApiTests.cs`, `HelpCenterTests.cs`. Docs (owned):
    `docs/app/settings/legal.md`, `docs/app/settings/help-support.md`.
    Consumer-doc amendment (Owner-approved; **only** legal/Help cross-references):
    `docs/postman/app/Sanad.App.Family.postman_collection.json` (three legal/help
    descriptions; T1's payment description preserved), the Caregiver collection,
    `docs/postman/care-homes/Sanad.CareHomes.Owner.postman_collection.json`
    (folder + privacy/terms descriptions; added an authenticated
    `GET /api/v1/help-center?audience=5` example), `docs/admin/legal-content.md`,
    `docs/admin/help-center.md`, `docs/app/settings/privacy-and-security.md`,
    `docs/app/public/splash-screens.md` (legal cross-reference only — the anonymous
    splash carousel semantics are unchanged), `docs/auth/support-contact.md`
    (Help Center cross-reference only — the support contact pair is unchanged).
    Public Postman untouched; the frontend/mobile intake reports remain historical
    pre-implementation records, not live status.
  - **Contract.** supplied + owned → served; malformed/undefined →
    `400 Cms.Content.InvalidAudience` checked **first, for every account class**
    (an admin token sending a malformed value gets 400, not 403); well-formed but
    unowned → `403 Cms.Content.UnsupportedAudience`; omitted — or empty/whitespace,
    which is treated exactly as omitted — with exactly one owned → auto-selected;
    omitted with more than one owned → `400 Cms.Content.AudienceRequired`; zero
    owned (admin) → `403 Cms.Content.UnsupportedAudience`. A repeated `?audience=`
    keeps the first value (framework scalar query binding) and cannot escalate
    because the value is always re-validated against the token's owned audiences.
    Reuses the existing mapped `Cms.Content.UnsupportedAudience` (403) — no new
    custom code/row.
  - **Executed evidence (this turn).** Strict Release build
    `dotnet build Sanad.slnx -c Release` → **exit 0, 0 errors, 1 warning**:
    `tests/Sanad.UnitTests/Finance/PlatformControlsTests.cs(552,9)` `xUnit2017`,
    which is **T1's** file (added hunk `@@ -524,0 +525,30 @@`) — reported, not
    repaired, because it is outside T3 scope (no T1 compile defect seen; the
    solution builds). Focused tests
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~ApiControllerBaseTests|FullyQualifiedName~OwnerLegalAudienceApiTests|FullyQualifiedName~HelpCenterTests|FullyQualifiedName~LegalDocumentTests"`
    → **exit 0, 90/90 passed, 0 warnings, 12.7 s** (ApiControllerBaseTests 38,
    OwnerLegalAudienceApiTests 24 + nested OpenAPI 1, HelpCenterTests 21,
    pre-existing Cms LegalDocumentTests 6 — the last covers the pre-existing
    single-account controller/helper callers). New assertions actually executed:
    empty/whitespace = omitted (unit and both routes), admin + malformed → 400
    precedence, owner single-audience Help Center served 200. The pre-T3 authored
    cases that were never run are now part of this 90/90 run. A final re-run after
    the last documentation-only edits reproduced the same **90/90, exit 0** — the
    compiled inputs were unchanged, so this evidence matches the final tree.
  - **Owner Help Center correction (recorded).** The Help Center read serves the
    approved Care Home Owner audience on the same terms as legal. `HEAD` already
    mapped `CareHomeOwner` in the shared helper (`d5f287d` "feat(f5): owner help")
    and the domain `IsHelpAudience` already permitted it, so the **pre-existing
    inconsistency was the controller helper's declared contract** ("does not grant
    an Owner audience") plus the duplicated legal helper — not a reachable wire
    403. T3 removes both, resolves owner deterministically and asserts the owner
    read explicitly. No request-level status change is claimed for the owner claim;
    the owner Help Center audience was already permitted by the domain and docs.
  - **Consumer coverage validation.** The three amended Postman collections parse
    as JSON (Family 19 / Caregiver 9 / CareHomes.Owner 12 top-level folders, names
    intact); `git diff --check` over all 16 changed paths → exit 0; the Family
    Postman diff is exactly three legal/help description pairs plus T1's preserved
    payment description line (no payment/null-mapping rule touched).
  - **Limitations.** No runtime/API host, adapter, DB, migration, provider, commit,
    push or deploy action. No XML documentation file is generated
    (`GenerateDocumentationFile` is off for `Sanad.API`), so the
    `<param name="audience">` descriptions are the source of the value-space text
    but do **not** yet surface in the generated OpenAPI document; enabling that is
    out-of-manifest setup work and was not done. Repeated-query first-value
    behavior is framework binding, documented rather than unit-tested (no custom
    binder/policy added). Out-of-scope finding left unfixed and reported:
    `docs/app/public/splash-screens.md` has a pre-existing unclosed code fence
    around lines 40–43.
  - **Next.** Independent acceptance review (`ses_ea0d872efffe33sfnrQXqZ8Teg`) of
    the corrected docs/amendments and the executed build/test evidence. T3 remains
    `accepted` (not completed).
- **T3 verification (independent reviewer ses_ea0d872efffe33sfnrQXqZ8Teg):** counts reconcile exactly (ApiControllerBaseTests 38, OwnerLegalAudienceApiTests 24 + nested OpenAPI 1, HelpCenterTests 21, LegalDocumentTests 6 = 90, no skips); scope integrity confirmed (16 paths, exactly the authorized amendment; Public collection untouched; T1 payment description preserved; splash-carousel and support-contact semantics unchanged; all four collections parse as JSON); Requirement-1 docs match the resolver's check order and the admin+malformed regression exists on both routes; the reviewer's earlier F2 finding is RETRACTED because HEAD already mapped CareHomeOwner and the domain already permitted it, so there was no reachable wire 403 — T3's recording was accurate. Residuals recorded as open but non-blocking: F5 (repeated-query first-value is documented from framework `ValueProviderResult.FirstValue`, not tested; no HTTP-pipeline binding test exists) and F6 (`TryGetCmsAudienceFromClaims` retained with no production caller, only the unmodified probe at tests/Sanad.UnitTests/Cms/LegalDocumentTests.cs — cosmetic). Stale snapshot corrected: the T3 block's "1 warning xUnit2017" figures were captured before T1's later test correction; that warning no longer exists and the final tree builds 0 warnings/0 errors (reproduced independently).

- **Wave 2 kickoff:** T2 = Worker A (this session, model opencode/beam-free#high), T4 = Worker B (session ses_ea0e27e04ffeY1E14YEcikIRSc, model river/deepseek-ai/DeepSeek-V4.1-Flash#high), both `in_progress`, HEAD pinned at 4fa7c77e3544abb0650aa5baf6c24358a1b48654, Worker A holds the first build/test slot, Worker B must not build until the slot is handed back.

### T4 — Admin enrichment + F1 — `accepted`
- **Objective:** ticket list/detail gain sender `displayName` + `accountType`
  (**email/phone default omitted**); admin visit list/detail gain facility/family
  names; add the missing `404 [ProducesResponseType]` (F1).
- **Manifest:** `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminSupportTicketQueries.cs`;
  `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/AdminCareHomeVisitQueries.cs`;
  `src/API/Sanad.API/Controllers/AdminSupportTicketsController.cs`;
  `src/API/Sanad.API/Controllers/AdminCareHomeVisitsController.cs`;
  `docs/admin/support-inbox.md`; `docs/admin/care-homes.md`;
  `docs/postman/admins/Sanad.Admin.postman_collection.json`;
  `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json`;
  tests `tests/Sanad.UnitTests/CareHomes/AdminCareHomeVisitQueryTests.cs`,
  `tests/Sanad.UnitTests/Identity/Support/AdminSupportTicketTests.cs`,
  `tests/Sanad.UnitTests/API/SupportInboxApiTests.cs`.

- **T4 result (Worker B, session `ses_ea0e27e04ffeY1E14YEcikIRSc`, model river/deepseek-ai/DeepSeek-V4.1-Flash#high; verification session `ses_ea0d872efffe33sfnrQXqZ8Teg`):** accepted, verified. Changed paths (11 manifest + 1 reported out-of-manifest test): `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminSupportTicketQueries.cs`; `src/Modules/CareHomes/Application/Sanad.Modules.CareHomes.Application/Visits/AdminCareHomeVisitQueries.cs`; `src/API/Sanad.API/Controllers/AdminSupportTicketsController.cs`; `src/API/Sanad.API/Controllers/AdminCareHomeVisitsController.cs`; `docs/admin/support-inbox.md`; `docs/admin/care-homes.md`; `docs/postman/admins/Sanad.Admin.postman_collection.json`; `docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json`; `tests/Sanad.UnitTests/CareHomes/AdminCareHomeVisitQueryTests.cs`; `tests/Sanad.UnitTests/Identity/Support/AdminSupportTicketTests.cs`; `tests/Sanad.UnitTests/API/SupportInboxApiTests.cs`; plus the authorized out-of-manifest amendment in `tests/Sanad.UnitTests/API/AdminOperationalReadAuthorizationTests.cs`. Contract: ticket list/detail gain `displayName` + `accountType` (email/phone intentionally not returned); display name is English-preferred/Arabic-fallback from one batched lookup per page (list) or one lookup (detail), degrading to null without throwing; `accountType` is the first registered account as a documented display convention because no account association is persisted on the ticket. Admin visit list/detail gain `facilityName` (approved revision, nullable) + `familyName` (stored family name, nullable). F1 adds `404 [ProducesResponseType]` to exactly the three ticket routes that can return not-found; the list route is deliberately unchanged. Batching/degrade-to-null behavior as above. Build: `dotnet build Sanad.slnx -c Release` -> exit 0, 0 warnings, 0 errors. Tests: `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~AdminSupportTicketTests|FullyQualifiedName~AdminCareHomeVisitQueryTests|FullyQualifiedName~SupportInboxApiTests|FullyQualifiedName~AdminOperationalReadAuthorizationTests"` -> exit 0, Total 56, Passed 56, Failed 0, Skipped 0 (AdminSupportTicketTests 30, AdminCareHomeVisitQueryTests 8, SupportInboxApiTests 4, AdminOperationalReadAuthorizationTests 14). Independent verification by session `ses_ea0d872efffe33sfnrQXqZ8Teg` reproduced the gate exactly. Limitations: unit/in-memory only; no runtime/HTTP/DB evidence; no OpenAPI document exercised; out-of-manifest amendment disclosed; recorded coverage gaps: no exact OpenAPI property pin for the admin visit detail schema and the empty-page zero-query property pinned hard only for the family half. Next action: independent acceptance review.


- **Wave 3 kickoff (corrected):** T5 = Worker A — **delivered by session `ses_ea06d8d1affej5KJyDwi77uu2W` on model `opencode/minimax-m3.1-flash-free#high`**. The model was switched from `opencode/beam-free#high` (session `ses_ea0e2df07ffeAF0PQaocPTdRBI`) after the previous worker on this same assignment handed the task back three times as too large **without making a single edit**; the Owner-approved model fallback was invoked and the delivery followed in one turn. T6 = Worker B (session `ses_ea0e27e04ffeY1E14YEcikIRSc`, model `river/deepseek-ai/DeepSeek-V4.1-Flash#high`). HEAD pinned at `4fa7c77e3544abb0650aa5baf6c24358a1b48654`; Worker A holds the first build/test slot, Worker B must not run dotnet.


### T5 — Mobile read completeness — completed (**part 1 (items 1, 2, 3) + part 2 (items 5, 6, 7, 8)**; item 9 owned by T2)
- **Objective:** close items 1,2,3,5,6,7,8,10 with real sources; item 7 adds
  nullable `visitHistory`/`upcomingVisits`/`familyReview` to the caregiver booking
  detail, each sourced from real implemented records verified at build time. The
  `upcomingVisits` source is **undecided** pending mobile MOB-09 — **no promise** is
  made to map future/other bookings as visits.
- **Manifest (source):** `src/API/Sanad.API/Controllers/CommunityPostsController.cs`;
  `src/API/Sanad.API/Controllers/CommentsController.cs`;
  `src/API/Sanad.API/Controllers/FamilyController.cs`;
  `src/API/Sanad.API/Controllers/CaregiverBookingsController.cs`;
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostsQuery.cs`;
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostQuery.cs`;
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetCommunityRecommendationsQuery.cs`;
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/GetCommentsQuery.cs`;
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/GetCommentRepliesQuery.cs`;
  `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`;
  `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Discovery/CaregiverDiscovery.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryContentResponses.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Library/LibraryContentQueries.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingQueries.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/CaregiverBookingQueries.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Elderlies/ElderlyOwnProfile.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Elderlies/EmergencyContacts.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Families/FamilyProfiles.cs`;
  `src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Payouts/CaregiverPayoutReadQueries.cs`.
  (Read-only sources reused, not edited: `src/Modules/Families/Application/Sanad.Modules.Families.Application/Reports/VisitReportQueries.cs`,
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Reports/MedicalReportQueries.cs`.)
- **Manifest (docs):** `docs/app/community/README.md`, `docs/app/community/writes.md`,
  `docs/app/caregivers/profile.md`, `docs/app/caregivers/bookings.md`,
  `docs/app/library/README.md`, `docs/app/families/bookings.md`,
  `docs/app/families/medical-profile.md`, `docs/app/families/medical-access.md`,
  `docs/app/caregivers/payouts.md`.
- **Manifest (Postman):** `docs/postman/app/Sanad.App.Community.postman_collection.json`,
  `docs/postman/app/Sanad.App.Library.postman_collection.json`,
  `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`,
  `docs/postman/app/Sanad.App.Family.postman_collection.json`.
- **Manifest (tests):** `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs`,
  `tests/Sanad.UnitTests/Community/CommunityInteractionTests.cs`,
  `tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs`,
  `tests/Sanad.UnitTests/Cms/LibraryTests.cs`.
- **Acceptance:** item-1 anonymity never exposes identity; item-7 uses only real
  implemented rows and does **not** map future/other bookings as visits
  (`upcomingVisits` semantics resolved with mobile MOB-09 before any mapping); no
  synthetic schedule; item-8 keeps honest-null.
- **T5 result (Worker A — part 1 of 2 — items 1, 2, 3): `ready_for_review`, NOT
  `completed`.** Part 2 (items 5, 6, 7, 8) is outstanding and is not started.
  - **Model note.** Worker A's model was switched to
    `opencode/minimax-m3.1-flash-free#high` after the previous worker on this
    same assignment (`opencode/beam-free#high`, session
    `ses_ea0e2df07ffeAF0PQaocPTdRBI`) handed the task back three times as too
    large **without making a single edit**. The Owner-approved model fallback
    was invoked and this delivery was produced in one turn. HEAD pinned at
    `4fa7c77e3544abb0650aa5baf6c24358a1b48654`; every pre-existing dirty file
    was preserved byte-for-byte.
  - **Changed files (14 + brief).** Source (5):
    `src/API/Sanad.API/Controllers/CommunityPostsController.cs`;
    `src/API/Sanad.API/Controllers/CommentsController.cs`;
    `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostsQuery.cs`;
    `src/Modules/Community/Application/Sanad.Modules.Community.Application/Comments/GetCommentsQuery.cs`;
    `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`.
    Docs (4): `docs/app/community/README.md`; `docs/app/community/writes.md`;
    `docs/app/library/README.md`; `docs/app/caregivers/profile.md`.
    Postman (2): `docs/postman/app/Sanad.App.Community.postman_collection.json`;
    `docs/postman/app/Sanad.App.Library.postman_collection.json` (both parse as
    valid JSON; descriptions and examples updated in place).
    Tests (3): `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs`;
    `tests/Sanad.UnitTests/Community/CommunityInteractionTests.cs`;
    `tests/Sanad.UnitTests/Cms/LibraryTests.cs`.
    Brief (1): this file (T5 block only — no T1–T4 block and no T6 content was
    touched).
    **Manifest files deliberately NOT changed** (no change was required, not an
    omission): `.../Posts/GetPostQuery.cs`, `.../Posts/GetCommunityRecommendationsQuery.cs`,
    `.../Comments/GetCommentRepliesQuery.cs` (the author projection is applied
    in the controllers, so the existing handlers and their result types stay
    untouched and no other test file had to change);
    `.../Caregivers/Application/.../Discovery/CaregiverDiscovery.cs` (item 2
    verdict: no gap); `.../Cms/Application/.../Library/LibraryContentResponses.cs`
    and `.../Library/LibraryContentQueries.cs` (item 3 verdict: no gap).
  - **Item 1 — Community author fields with anonymity (contract + nullability).**
    New additive members on the Community post response
    (`CommunityPostResponse`): `displayName` (**string, nullable**) and
    `avatarUrl` (**string, nullable**). New additive members on
    `CommunityCommentResponse` / `CommunityReplyResponse`: `isAnonymous`
    (**bool, non-nullable**) plus `displayName` (**string, nullable**) and
    `avatarUrl` (**string, nullable**); **`authorId` changed from `Guid` to
    `Guid?`** on those two responses (nullable — it is `null` exactly when
    `isAnonymous` is `true`; the field name and position are unchanged, and
    edit/delete ownership is still enforced server-side). No other field of any
    response changed. `displayName` = the author's **English full name with the
    Arabic full name as fallback** (the rule the Identity inbox and the Chat
    participant already use), `null` when the identity row is missing or both
    names are blank — never a placeholder. `avatarUrl` is **always `null`**
    (honest-null, documented): Community has **no publicly servable avatar
    source** — the only stored avatar reference is the Identity private file
    key (`private/avatars/...`), readable only by its owner through
    `GET /api/v1/auth/avatar`; publishing that key as if it were a URL would
    hand the app a link that always 404s and would disclose a stable per-user
    file identifier in a public feed. (The Chat participant and caregiver
    discovery reads publish the same private key; that difference is
    deliberate and is recorded here as a decision point — a publicly fetchable
    avatar URL would need a new public endpoint, which this task may not add.)
    **Anonymity rule (one rule, real sources only).** Community stores
    anonymity on the **post only** (no comment/reply column exists and no
    schema change or migration is allowed). Therefore: (a) an **anonymous post
    carries no author identity at all** — `displayName`/`avatarUrl` are `null`
    and the post response still has **no author id field**; (b) a **comment or
    reply is anonymous exactly when it is written by the author of an anonymous
    post inside that post's own thread** — the one place where the post author
    would otherwise be identified through their own words. Such an item
    returns `authorId: null`, `displayName: null`, `avatarUrl: null`,
    `isAnonymous: true`; every other comment/reply is not anonymous and
    carries its author id and display name. This mirrors the post convention
    ("anonymity = null identity", the MOB-10 agreement) and never substitutes
    a placeholder name. The anonymous author's id is **never even looked up**:
    it is excluded from the batched identity lookup.
    **Batching:** one batched identity lookup per page for the posts feed,
    post detail, recommendations, favorites, comments and replies
    (`GetCommunityAuthorNamesAsync` → the new `GetCommunityAuthorProfilesQuery`
    handler); a per-row lookup is never performed (a counting context in the
    tests fails an accidental N+1). The comment and reply reads additionally
    resolve the **thread's post** once per page (the stored anonymity flag and
    author of that post) — a constant number of queries, never one per row.
    No new endpoint, no schema change, no migration, no authorization change,
    no pagination/filter/sort change.
  - **Item 2 — caregiver images / certificate-file serving: VERIFIED, NO CODE
    CHANGE.** Verdict: **public-safe — neither file is publicly served.** (i)
    Certificate files are private: `POST /api/v1/caregiver/certificates` and
    `PUT /api/v1/caregiver/certificates/{certificateId}/file` save through
    `SavePrivateAsync` (`src/API/Sanad.API/Controllers/CaregiverController.cs`
    ~667–687) with folder `caregiver-certificates`
    (`src/Modules/Caregivers/Application/Sanad.Modules.Caregivers.Application/Onboarding/CaregiverCertificates.cs`
    13–17), so the key is `private/caregiver-certificates/...`;
    `LocalDiskFileStorage.SavePrivateAsync` prefixes `private/`
    (`src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs`
    186–187) and `ResolveFullPath` writes it to the **sibling
    `<storage-root>-private` directory** that `UseStaticFiles` never serves
    (281–301); `OpenReadAsync` refuses any key that does not start with
    `private/` (236–244); and `PrivateStorageRequestGuard` answers every
    `/files/private...` request with `404`
    (`src/API/Sanad.API/ApplicationBuilderExtensions.cs` 226–238). The only
    download route is the admin-policy route
    `GET /api/v1/admin/caregivers/{caregiverId}/certificates/{certificateId}/file`
    (`src/API/Sanad.API/Controllers/AdminCaregiversController.cs` 26–27, 95–118;
    handler `GetCertificateFileQueryHandler` 517–552 reads through
    `OpenReadAsync`). The caregiver profile and the public caregiver profile
    expose certificate **type/expiry/status only**. (ii) Avatars are private
    too: `PUT /api/v1/auth/avatar` saves with `SavePrivateAsync` under
    `avatars` (`src/API/Sanad.API/Controllers/AuthController.cs` 694–724) and
    the only read route, `GET /api/v1/auth/avatar` (494–523), serves the
    **caller's own** avatar; the `avatarUrl` on the caregiver discovery reads
    (`CaregiverSearchCardResponse.AvatarUrl` /
    `CaregiverPublicProfileResponse.AvatarUrl`,
    `.../Discovery/CaregiverDiscovery.cs` 14–32, 85–109, 322–346) is that
    stored private reference or `null` — a storage key, **not a fetchable
    URL**. No real gap was found, so **no code was changed**; the verified
    truth is documented in `docs/app/caregivers/profile.md` (new section
    "Avatar and certificate files"), including the instruction that the app
    must not load `avatarUrl` as an image URL.
  - **Item 3 — Library four reads (+ `/saved`): VERIFIED, NO CODE CHANGE.**
    `GET /api/v1/library/categories`, `/content`, `/content/{id}`,
    `/recommendations` and `/saved` already expose every field the app needs
    from a real stored row, so no source file was touched. Verified sources:
    category fields from `library_categories` (active only, creation-time
    order); list/detail fields from `library_contents` +
    `library_content_sections`; **cover path** = the stored storage key under
    the `library/` prefix, saved by the admin upload through the public
    `SaveAsync(..., "library", ...)`
    (`src/API/Sanad.API/Controllers/AdminLibraryController.cs` 238–252) and
    therefore served by the anonymous static host as
    `{baseUrl}/files/{coverImagePath}`; **the real playable sample** =
    `videoUrl` (the administrator-stored absolute http(s) URL) + the real
    `durationSeconds` — there is **no video hosting and no built-in demo
    sample** in V1, so a playable sample exists only for a published `Video`
    row, and the honest rule is stated; **premium gating** via
    `LibraryPremiumAccess` (benefit key `PremiumContent`, family resolution
    per the Families v1 rule) — a gated detail returns metadata only
    (`videoUrl: null`, `sections: []`, `canAccess: false`, 200 not an error)
    while the real `durationSeconds` and the cover stay visible. Honest-null
    confirmed for `categoryId`/`categoryArabicName`/`categoryEnglishName`
    (content without a category) and for `durationSeconds`/`videoUrl`
    (articles). Two card fields from the app design have **no implemented
    source** and are therefore **not returned**: the content **author** (no
    author on a content row) and an article **read time** (no duration stored
    for articles) — documented as "do not render invented values".
  - **Executed evidence (this turn, sole build/test slot held by Worker A).**
    Strict build `dotnet build Sanad.slnx -c Release` → **exit 0, 0 warnings,
    0 errors** (no NoWarn/pragma/editorconfig suppression was added; the
    pre-existing "Chat projects are absent from Sanad.slnx and emit into
    bin\Debug" quirk noted under the T2 carried-forward items is unchanged).
    Focused tests
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~CommunityFeedTests|FullyQualifiedName~CommunityInteractionTests|FullyQualifiedName~LibraryTests"`
    → **exit 0, Total 36, Passed 36, Failed 0, Skipped 0**. As an extra
    non-required regression check (not part of the recorded gate) the same
    binaries were run with `--filter "FullyQualifiedName~Sanad.UnitTests.Community|FullyQualifiedName~Sanad.UnitTests.Cms"`
    → exit 0, Total 324, Passed 324, Failed 0, Skipped 0 (no neighbouring
    Community/Cms test was broken). The pre-existing
    `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`
    baseline failure was **not** repaired and was not touched by this change
    (it asserts CareHomes schemas only).
  - **New tests (11).** `CommunityFeedTests.cs`:
    `Post_feed_projects_the_author_display_name_and_never_identifies_an_anonymous_author`
    (asserts the named author, the null identity of the anonymous post and a
    missing identity row, and serializes the anonymous response to prove the
    wire body carries no name and no author id);
    `Post_feed_resolves_the_author_names_with_one_batched_lookup_per_page`
    (exactly one identity lookup; the anonymous author's id is never
    requested);
    `Post_detail_projects_the_author_display_name_and_hides_an_anonymous_author`.
    `CommunityInteractionTests.cs`:
    `Comment_list_projects_the_author_display_name`;
    `Comment_written_by_the_author_of_an_anonymous_post_carries_no_identity`
    (the failing-on-leak test: `authorId`/`displayName`/`avatarUrl` are null
    and the serialized body contains neither the name nor the id, while the
    other commenter's identity is still projected);
    `Reply_written_by_the_author_of_an_anonymous_post_carries_no_identity`;
    `Comment_list_keeps_the_display_name_null_when_the_identity_row_is_missing`;
    `Comment_and_reply_lists_resolve_the_author_names_with_one_batched_lookup_per_page`.
    `LibraryTests.cs`:
    `Public_content_reads_expose_the_cover_path_and_the_real_video_fields`
    (list + recommendations + detail: cover path, type, duration, video URL,
    category fields);
    `Public_content_detail_returns_null_category_fields_when_the_content_has_no_category`;
    `Premium_video_detail_keeps_the_real_duration_and_hides_the_video_url_without_the_benefit`.
    The Community tests exercise the real controllers over a SQLite Community
    database that also carries the shared Identity `users` table the batched
    lookup really reads, so the batched SQL path is executed, not mocked.
  - **Limitations (honest).** Unit tests only (SQLite in-memory database for
    the Community module, EF in-memory for the Library module): no runtime
    host, no HTTP request/response evidence, no database, no migration, no
    fixture seeding, no provider call, no deploy, no commit/push. The identity
    lookup is proven against SQLite with a hand-created `users` table; the
    PostgreSQL path (`identity.users`) is unchanged code that was not exercised
    here. The
    anonymity rule for comments/replies is derived from the thread's post
    because no comment/reply anonymity column exists — if the Owner later
    wants per-comment anonymity, that needs a schema change and a migration
    (explicitly out of this task's scope). The `authorId` nullability widening
    on comment/reply responses is a wire change the mobile client must accept
    (`Guid?`). `avatarUrl` is `null` everywhere until a public avatar source
    exists. The caregiver avatar/certificate truth is verified from source
    only; no HTTP request was made. **Items 5, 6, 7 and 8 are outstanding and
    are not implemented** — they are part 2 of T5.
  - **Next action:** independent acceptance review of this part-1 delivery
    (changed diff, the build/test evidence, and the item-2/item-3 verdicts),
    before Worker A starts part 2 (items 5, 6, 7, 8) and before any release
    discussion. T5 stays `ready_for_review` — only Mastermind may set
    `completed`.
- **T5 result (Worker A — part 2 of 2 — items 5, 6, 7, 8): `ready_for_review`,
  NOT `completed`.** Same worker/session/model as part 1
  (`ses_ea06d8d1affej5KJyDwi77uu2W`, `opencode/minimax-m3.1-flash-free#high`);
  part 1 (items 1, 2, 3) is unchanged by this turn and was **not** re-opened.
  - **Changed files (8 + 1 authorised test addition).** Source (2):
    `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/BookingQueries.cs`;
    `src/Modules/Families/Application/Sanad.Modules.Families.Application/Bookings/CaregiverBookingQueries.cs`.
    Docs (5): `docs/app/caregivers/bookings.md`; `docs/app/families/bookings.md`;
    `docs/app/families/medical-profile.md`; `docs/app/families/medical-access.md`;
    `docs/app/caregivers/payouts.md`.
    Postman (2): `docs/postman/app/Sanad.App.Caregiver.postman_collection.json`;
    `docs/postman/app/Sanad.App.Family.postman_collection.json` (booking
    descriptions only; the accepted T1 payment and T3 legal/help descriptions
    were left untouched; both collections re-parsed as valid JSON — Caregiver
    9 folders/43 items, Family 19 folders/94 items).
    Tests (1): `tests/Sanad.UnitTests/Families/CaregiverBookingDetailTests.cs`.
    **Authorised test addition (reported as required):**
    `tests/Sanad.UnitTests/Families/FamilyBookingReadTests.cs` — the
    pre-authorised file for the family-side projections, which the manifest's
    single test file (a caregiver booking-detail file) cannot host.
  - **Item 5 — family booking list/detail completeness (contract + nullability).**
    `BookingElderlySummaryResponse` (used by the family **and** caregiver
    booking detail) gains two additive, trailing members:
    `EmergencyContactResponse? EmergencyContact = null` (**nullable**) and
    `bool HasPhoto = false` (**non-nullable**). The **family** booking detail
    populates both from the already-loaded dependent row: the emergency contact
    is the stored `{name, relationship, phoneNumber}` (reusing the existing
    `EmergencyContactResponse` mapping — `null` when none is stored) and
    `hasPhoto` is `!string.IsNullOrWhiteSpace(elderly.ProfileImageKey)`. The
    family already reads the same contact on
    `GET /api/v1/family/dependents/{dependentId}/emergency-contact`, so this
    adds **no new access**; the **caregiver** booking detail always returns
    `emergencyContact: null` (no emergency-contact access or status model is
    invented and no caregiver access is added) and only `hasPhoto` (a fact; a
    caregiver has no route to the bytes, so no photo URL is published). When the
    dependent row is gone, every summary member stays `null`/`false` — no
    throw, no fabricated dependent. The family booking **list** is unchanged:
    the reserved caregiver name/avatar fields stay honest `null` (a booking
    stores the caregiver **aggregate id**, and the caregiver's identity name
    lives behind the Caregivers/Identity boundary, which exposes no seam to the
    Families module) and the app reads the caregiver card from
    `GET /api/v1/caregivers/{caregiverId}`; the family name/avatar stay on the
    family profile (`GET /api/v1/family`). The senior's date of birth and gender
    were already on the detail.
  - **Item 6 — caregiver booking medical context: MAPPED AND DOCUMENTED, NO CODE
    CHANGE.** The route → field → authorization mapping is now written down
    verbatim in `docs/app/families/medical-profile.md`,
    `docs/app/families/medical-access.md` and `docs/app/caregivers/bookings.md`.
    The verified truth: the **only** medical context a caregiver can read today
    is the caregiver booking detail's `elderly.healthNotes` (free text the
    family stored on the dependent row; `CaregiverAccess` + the booking must
    belong to the caller) and the submitting caregiver's own medical-report
    photo (`GET /api/v1/caregiver/bookings/medical-reports/{reportId}/photo`,
    `403 Reports.Medical.AccessDenied` for another caregiver). The structured
    medical profile (`GET`/`PUT /api/v1/family/dependents/{dependentId}/medical-profile`,
    `FamilyAccess` + Owner/Editor for the write) and the medications
    (`/api/v1/family/dependents/{dependentId}/medications`, `FamilyAccess`) are
    family-scoped only. The medical-access **grant** model exists
    (`POST/GET/DELETE /api/v1/family/dependents/{dependentId}/medical-access-grants`,
    `FamilyAccess`) and the decision audit is admin-only
    (`GET /api/v1/admin/caregivers/medical-access-audit`, SuperAdmin/SupportAdmin),
    but **no caregiver record-read route consumes a grant today** — so nothing in
    a caregiver response carries a medical profile, a medication or an emergency
    contact. A caregiver-facing medical-record read would be a new route plus new
    access rules, i.e. an authorization change, which this task may not make.
  - **Item 7 — caregiver booking detail (`visitHistory` / `upcomingVisits` /
    `familyReview`).** Three nullable, trailing members on
    `BookingDetailResponse`, populated **only** by the caregiver booking detail
    read (the family booking detail leaves them `null` — the same pattern the
    F6 cancellation-fact members already use): `visitHistory`
    (**`IReadOnlyList<CaregiverBookingVisitHistoryItem>?`, nullable**) = the real
    visit report and/or medical report rows **of this booking**, newest first
    (`null` — not an empty array — when no report was recorded yet); each item
    carries the stored facts `reportId`, `reportType` (`visit`|`medical`),
    `caregiverType`, `observedCondition`, `activities`, `systolic`, `diastolic`,
    `pulse`, `temperature`, `notes`, `assessment`, `startedOnUtc`,
    `completedOnUtc`, `measurementTakenOnUtc`, `submittedOnUtc`, `photoAvailable`
    (the fields of the other report kind stay `null`);
    `upcomingVisits` (**`IReadOnlyList<CaregiverBookingListItemResponse>?`,
    nullable, ALWAYS `null`**) and `familyReview`
    (**`BookingReviewResponse?`, nullable** = the stored family review: `id`,
    `bookingId`, `rating`, `comment`, `isAnonymous`, `createdOnUtc`; `null`
    when the family has not reviewed; the review author is never published, so
    an anonymous review stays anonymous).
    **How `upcomingVisits` stays honest-null:** the "upcoming" semantics are
    undecided pending the mobile team's **MOB-09** answer (the same booking, or
    the caregiver's other bookings), so the member exists and is always `null`:
    no other or future booking is mapped as a visit, no synthetic schedule is
    produced, and the caregiver's other bookings are never read by this query.
    The proof is the test
    `Detail_keeps_upcoming_visits_null_and_never_maps_another_booking_as_a_visit`,
    which seeds a second, later booking of the same caregiver and asserts
    `upcomingVisits` is `null` **and** that the other booking's id and date do
    not appear anywhere in the serialized response.
  - **Item 8 — caregiver financials: VERIFIED AND DOCUMENTED, NO PAYOUT LOGIC
    CHANGE.** The real amount fields and their sources are now written down in
    `docs/app/caregivers/payouts.md`: the caregiver booking detail's
    `baseCaregiverFee`, `platformFeePercentage`, `platformFeeAmount`,
    `totalPayableAmount` and `currency` come from the booking's immutable price
    snapshot (the caregiver's entitlement is the base fee in full; the
    family-paid platform fee is never deducted), and the payout reads report
    `unpaidAmount`/`transferredAmount`/`failedAmount`/`currency`/
    `nextExpectedPayoutOnUtc` and the per-row `amount` (= `grossAmount`) and
    `bookingItems[].caregiverAmount`. The **unavailable-value policy is
    honest-null, never a fabricated or zero-filled amount**: the payment-policy
    `minimumPayoutAmount`/`expectedProcessingDays` are always `null` (no such
    rule exists), the summary `currency` is `null` with no payout and no
    completed booking, `nextExpectedPayoutOnUtc` is `null` without a verified
    account, the bank names are `null` for an unknown bank code, and with no
    effective payout policy the summary **fails closed** with
    `400 Finance.PayoutPolicy.Missing` instead of reporting a zero balance.
    The existing in-memory evidence already proves the policy
    (`CaregiverPayoutReadTests.PaymentPolicy_ShouldReturnDescriptionsAndHonestNulls`
    and `PaymentPolicy_ShouldReturnNullDescriptions_WhenNoPolicyExists`), so no
    duplicate Caregivers test file was created; the booking-amount read is
    proven by the new `Detail_reports_the_stored_price_snapshot_amounts`.
  - **Executed evidence (this turn; Worker A held the sole build/test slot).**
    Strict build `dotnet build Sanad.slnx -c Release` → **exit 0, 0 warnings,
    0 errors** (no warning suppression was added). Focused run
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~CaregiverBookingDetailTests|FullyQualifiedName~FamilyBookingReadTests"`
    → **exit 0, Total 13, Passed 13, Failed 0, Skipped 0**. One earlier focused
    run of `CaregiverBookingDetailTests` alone failed (1 failed / 8 passed) on
    the pre-existing assertion
    `Detail_never_exposes_the_caregiver_incident_flag_a_ledger_or_a_derived_timeline`:
    its forbidden-name list contained the bare substring `history`, which the
    newly approved member name `visitHistory` matches. It was repaired
    minimally and honestly — the forbidden list keeps every F6 name
    (`isCaregiverIncident`, `reasonNote`, `actorUserId`, `policyVersion`,
    `statusAtCancellation`, `ledger`, `timeline`, `eventHistory`) and the test
    now asserts that the **only** history-shaped member is the approved
    `visitHistory` and that it is `null` for that booking — the F6 "no invented
    history/timeline" intent is preserved, not weakened. After the repair the
    same filter passed 9/9 and the recorded 13/13 gate above passed. As an
    extra non-required regression check the same binaries were run with
    `--filter "FullyQualifiedName~Sanad.UnitTests.Families|FullyQualifiedName~Sanad.UnitTests.Caregivers"`
    → exit 0, Total 1676, Passed 1673, Failed 0, Skipped 3 (the three skips are
    pre-existing Postgres-concurrency cases). The pre-existing
    `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes`
    baseline failure was **not** repaired and is out of scope.
  - **New tests (9).** `CaregiverBookingDetailTests.cs`:
    `Detail_publishes_the_real_visit_history_and_the_family_review` (both real
    report rows and the stored review, newest-first order, per-kind nulls);
    `Detail_keeps_upcoming_visits_null_and_never_maps_another_booking_as_a_visit`
    (the acceptance-critical honest-null proof);
    `Detail_leaves_the_record_members_null_when_no_report_or_review_exists`
    (degradation without throw);
    `Detail_reports_the_stored_price_snapshot_amounts` (item 8's booking
    amounts);
    `Detail_never_exposes_the_emergency_contact_to_a_caregiver` (item 5's hard
    rule: the stored contact never reaches the caregiver response);
    plus the repaired
    `Detail_never_exposes_the_caregiver_incident_flag_a_ledger_or_a_derived_timeline`.
    `FamilyBookingReadTests.cs` (authorised addition):
    `Family_booking_detail_publishes_the_elderly_photo_flag_and_the_stored_emergency_contact`;
    `Family_booking_detail_keeps_the_emergency_contact_null_when_none_is_stored`;
    `Family_booking_detail_degrades_without_throw_when_the_dependent_row_is_missing`;
    `Family_booking_detail_leaves_the_caregiver_only_record_members_null`.
  - **Limitations (honest).** Unit tests only (EF in-memory): no runtime host,
    no HTTP request/response evidence, no database, no migration, no fixture
    seeding, no provider call, no deploy, no commit/push. The caregiver
    medical-context gap (no caregiver route reads the structured medical
    profile or the medications through a grant) is a real, documented gap, not
    a defect fixed here: closing it needs a new route and new access rules,
    which this task may not add — it needs an Owner decision. The family's
    booking list still cannot carry the caregiver's name/avatar (no seam
    exists); the app reads the caregiver card from the caregiver discovery
    route. `upcomingVisits` remains **deferred pending MOB-09** — the member is
    always `null` and no other or future booking is mapped as a visit. The
    part-1 limitations still stand: unit/in-memory only, `avatarUrl` is `null`
    for Community authors, the comment/reply `authorId` is nullable, and the
    item-2/item-3 verdicts were source-verifications only.
  - **Next action:** independent acceptance review covering **both** parts of
    T5 (part 1 items 1/2/3 and this part 2 items 5/6/7/8) — the changed diff,
    the build/test evidence and the item-6/item-8 verdicts — before any
    release discussion. T5 stays `ready_for_review`; only Mastermind may set
    `completed`.
- **T5 acceptance verdict (independent acceptance review; session `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`, model `river/deepseek-ai/DeepSeek-V4.1-Flash#high`): ACCEPT — no blocking defect; covers part 1 + part 2. T5 is `completed`.**
  - **Reproduced evidence:** strict `dotnet build Sanad.slnx -c Release` → exit 0, 0 warnings, 0 errors; combined filter `FullyQualifiedName~CommunityFeedTests|FullyQualifiedName~CommunityInteractionTests|FullyQualifiedName~LibraryTests|FullyQualifiedName~CaregiverBookingDetailTests|FullyQualifiedName~FamilyBookingReadTests` → exit 0, Total 49, Passed 49, Failed 0, Skipped 0 (36 part 1 + 13 part 2); all four touched Postman collections parsed as valid JSON.
  - **Independently verified:** anonymity cannot leak on any reachable path — the rule is implemented as designed (a comment/reply is anonymous exactly when written by the anonymous post's author inside that post's own thread; a reply resolves its parent comment's post, never an unrelated post; the anonymous author's id is excluded from the batched lookup and a counting context would fail an accidental N+1; the leak tests serialize the wire body and assert the null identity **and** the absence of the real name and poster GUID while another commenter's identity is still projected); `Guid?` `authorId` cannot bypass ownership because edit/delete authorize from the stored author server-side; `upcomingVisits` is always `null` with a proof test that seeds a later different booking of the same caregiver and would fail if any other booking were mapped; the caregiver detail never returns the family's emergency contact (hard-coded `null` plus a leak test over the serialized body) and `hasPhoto` is a flag with no bytes or URL; `visitHistory` contains only this booking's report rows, newest first, `null` not empty when none; `familyReview` publishes no author identity; items 2, 3, 6 and 8 (verification-first, no code change) were independently confirmed against source (private certificate/avatar storage with the 404 `/files/private…` guard; Library cover/premium gating; the caregiver route→field→authorization mapping; the payout honest-null policy with no logic change); the change set is confined to the T5 manifest plus the one pre-authorised addition `tests/Sanad.UnitTests/Families/FamilyBookingReadTests.cs`; no authorization, pagination, filter or sort semantics changed and no field was removed or renamed.
  - **F6 test repair judged minimal with the guarantee preserved:** the forbidden-name list still contains `isCaregiverIncident`, `reasonNote`, `actorUserId`, `policyVersion`, `statusAtCancellation`, `ledger`, `timeline`, `eventHistory` plus the raw secret and actor-id absence checks; only the over-broad bare substring `history` was dropped (necessary because the newly approved `visitHistory` member matched it) and replaced by a targeted structural assertion that the only history-shaped top-level member is `VisitHistory` and that it is null on that no-report fixture.
  - **Three Low, non-blocking hardening items (optional, explicitly not blocking acceptance):** L1 the F6 structural check enumerates top-level properties only, so a future nested member such as `incidentHistory` would evade the name list (smallest fix: recurse into nested objects, or forbid `*History*` names except an allowlist); L2 anonymity fails open — `IsAnonymous` returns false when a thread post cannot be loaded — which is unreachable today because no post hard-deletion path exists anywhere in `src` (smallest fix: project null identity when the thread cannot be resolved for a known post id); L3 the caregiver read's `Elderly.HasPhoto` is never asserted, so a regression hard-coding `false` would pass (it is a boolean flag with no bytes or URL, so not a leak; smallest fix: one assertion in a caregiver-detail test).
  - **Wire-change note:** `authorId` on the comment/reply responses widened `Guid` → `Guid?`, documented in `docs/app/community/writes.md`, `docs/app/community/README.md` and the Postman descriptions; `avatarUrl` is always null because Community has no publicly servable avatar source — a publicly fetchable avatar URL would need a new public endpoint (recorded as a decision point, not a defect).
  - **Carried-forward owner items this task surfaced:** (i) no caregiver route reads the structured medical profile or medications through a grant — the grant model and admin audit exist, and closing it needs a new route plus access rules, i.e. an authorization change; (ii) the family booking list cannot carry the caregiver's name or avatar because a booking stores only the caregiver aggregate id and no cross-module seam exists (the app reads `GET /api/v1/caregivers/{caregiverId}`).
  - **Next action:** none — the three Low items are optional hardening outside the approved scope.

### T6 — Documentation restorations + E07/E08 verification-boundary text — `accepted`
- **Objective:** restore the consumer-doc claims and correct the E07/E08
  verification-tool boundary text. This task carries a **code change** in the E07
  guard, so it is **not** docs-only.
- **Manifest:** `docs/admin/ratings-moderation.md`; `docs/admin/family-home.md`;
  `docs/app/families/home.md`; `docs/app/chat/conversations.md`;
  `docs/app/elderly/check-in.md`; `docs/auth/registration-and-verification.md`;
  `docs/app/caregivers/schedule-and-availability.md`;
  `tools/Invoke-E07LocalHttpVerification.ps1` (F2 guard: total feature-table
  footprint + planned writes `<= 10` before any fixture write; existing 12 rows ⇒
  fail closed — no execution, no delete, no cap relaxation);
  `tools/Invoke-E08LocalHttpVerification.ps1` (D1 limitation text only).
- **Acceptance:** the E07 guard is verified by **minimal offline boundary checks
  only — no DB, no fixture write, no data deletion**; the E08 change touches
  limitation text only. The seven docs do not overlap T5's docs/Postman (per the
  intersection computation below).
- **T6 result (Worker B — session `ses_ea0e27e04ffeY1E14YEcikIRSc`, model
  `river/deepseek-ai/DeepSeek-V4.1-Flash#high`; verification by session
  `ses_ea0d872efffe33sfnrQXqZ8Teg`): `accepted`, not completed.** Only **three**
  files changed. (1) `docs/app/chat/conversations.md` — the false cross-page
  ordering guarantee was removed and replaced with the truth: the conversation-id
  tie-break is **in-memory per page**. (2)
  `tools/Invoke-E07LocalHttpVerification.ps1` — the F2 guard now runs a new
  **count-only** footprint query and a new **PURE**
  `Test-E07ReviewNoteFootprint -ExistingRows -PlannedWrites [-Cap=10]`; the
  runtime gate sits **after** the `-SkipFixtureWrite` report-only branch and
  **before** any fixture write, API start or HTTP check; it denies when the
  total is above the cap or any input is negative; a read failure, an
  unparseable count and a denial all exit 1 **before** any write; with the real
  12 retained rows plus 4 planned = 16 > 10 it **fails closed** with no write,
  no delete and no cap relaxation, and the guard has no bypass. (3)
  `tools/Invoke-E08LocalHttpVerification.ps1` — D1: one line of limitation text
  corrected (the created assets/references are deleted again by the verified
  API, not left in place). Six of the seven T6 docs needed **no** change and
  were verified against source. Offline evidence: **17/17 boundary assertions
  PASS, exit 0, no database access**; the guard has no bypass. Two accepted doc
  follow-ups were applied afterwards: `docs/app/chat/conversations.md:15` and
  `:383` now state a per-page conversation-id tie-break, and
  `docs/app/families/assessment.md:90` now uses `assessment-tiers/tier1.png`
  instead of a false `.svg` example (authorised manifest amendment; the storage
  allowlist is JPEG/PNG/WebP only).
- **Carry-forward for the approved Landing SVG task (T8):** when T8 adds
  `image/svg+xml` to the shared storage allowlist it must re-check
  `docs/admin/care-assessments.md:83` and `:144` **and** the
  assessment-illustration picker's own SVG rejection, so audit item #46 is not
  silently inverted. Nothing else about T8 changes.

- **Wave 3 closeout:** T5 and T6 both accepted. Verification sessions: T5 ses_ea0d1a7f5ffel8R7a4GwCYdX4c (build 0 warnings/0 errors, 49/49); T6 ses_ea0d872efffe33sfnrQXqZ8Teg (17/17 offline boundary assertions with no database access). Both gates independently reproduced. Worker A for Wave 3 was session `ses_ea06d8d1affej5KJyDwi77uu2W` on `opencode/minimax-m3.1-flash-free#high`; Worker B was session `ses_ea0e27e04ffeY1E14YEcikIRSc` on `river/deepseek-ai/DeepSeek-V4.1-Flash#high`. Nothing was committed, pushed or deployed, and the persistent local `SanadDb` was untouched by both tasks. Wave 4 is being dispatched as T7 Community one-way block on Worker A and T8 Landing shape/stats/testimonials + SVG on Worker B.

### T7 — Community one-way block — `completed` (same worker as T5, after T5)
- **Design:** **Community-owned** `CommunityAuthorBlock` (viewer→author). Mirror
  Chat `Block` patterns only. Filter **post reads only** (`GetPostsQuery`,
  `GetPostQuery`, `GetCommunityRecommendationsQuery`, `GetFavoritePostsQuery`).
  No Chat write/data, no comment/interaction denial, no anonymous addressing.
- **Manifest:** **NEW** `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Blocks/CommunityAuthorBlock.cs`;
  **NEW** `src/Modules/Community/Domain/Sanad.Modules.Community.Domain/Blocks/CommunityAuthorBlockId.cs`;
  **NEW** `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Configurations/CommunityAuthorBlockConfiguration.cs`;
  **NEW** migration + designer + snapshot edit in
  `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/Migrations/`
  (EF-generated; reserved unique identifier, predecessor-checked);
  **NEW** `src/Modules/Community/Application/Sanad.Modules.Community.Application/Blocks/CommunityBlockCommands.cs`;
  **NEW** `src/Modules/Community/Application/Sanad.Modules.Community.Application/Blocks/CommunityBlockQueries.cs`;
  **NEW** `src/API/Sanad.API/Controllers/CommunityBlocksController.cs`; edits to
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostsQuery.cs`,
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostQuery.cs`,
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetCommunityRecommendationsQuery.cs`,
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetFavoritePostsQuery.cs`,
  `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs`,
  `src/Modules/Community/Application/Sanad.Modules.Community.Application/Abstractions/Data/ICommunityDbContext.cs`;
  docs `docs/app/community/README.md`;
  Postman `docs/postman/app/Sanad.App.Community.postman_collection.json`;
  tests `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs`,
  `tests/Sanad.UnitTests/Chat/BlockTests.cs`.
- **T7 acceptance verdict (independent acceptance review; first review session `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`, model `river/deepseek-ai/DeepSeek-V4.1-Flash#high`; re-verification session `ses_ea0d872efffe33sfnrQXqZ8Teg`): REJECT as delivered, then ACCEPT. T7 is `completed`.**
  - **Delivered (Worker A; session `ses_ea06d8d1affej5KJyDwi77uu2W`, model `opencode/minimax-m3.1-flash-free#high`):** a **Community-owned** `CommunityAuthorBlock` (viewer → blocked author) mirroring the Chat `Block` patterns — new aggregate + typed id, EF configuration (table `CommunityAuthorBlocks`, unique index on `(BlockerUserId, BlockedAuthorUserId)`), block/unblock commands, blocked-authors query, `CommunityBlocksController`, migration `20261021192605_AddCommunityAuthorBlocks` (files only, predecessor `20261017154400_AddCommunityModeration`, no database update and no `SanadDb` contact), with the filters composed **before** `Skip`/`Take` in `GetPostsQuery`/`GetPostQuery`/`GetFavoritePostsQuery` and via a `NOT EXISTS` in the recommendations raw SQL. Endpoints: `POST /api/v1/community/blocks` (204, idempotent; 400 `Community.Block.Invalid`; unknown target → the generic privacy 404 with code `Community.Block.NotFound`), `DELETE /api/v1/community/blocks/{authorId}` (204 always), `GET /api/v1/community/blocks` (200 bare paged list). Post reads only — comments, replies and interactions are not denied; no Chat code or data touched; no authorization change; no field removed or renamed.
  - **First verdict (session `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`): REJECT as delivered — correct on the merits, but the gate was red in its run at 62 passed / 1 failed.** Verified sound: filter placement, isolation from the JWT only, idempotency backed by the unique index, validation, migration integrity with no snapshot drift, scope discipline, and docs/Postman truth. The failure was a new test (`Blocked_authors_list_resolves_the_display_names_with_one_batched_lookup`) that built its expected array in creation order and compared against `OrderBy(id => id)`: two `Guid.CreateVersion7()` ids created microseconds apart shared their timestamp prefix, so the assertion passed only by chance and the author's earlier 63/63 was a lucky pass.
  - **Fix:** the assertion became a set comparison (`Assert.Equal(2, returned.Length)` plus two `Assert.Contains`) with no dependence on the relative order of the two ids — intent preserved (exactly the two blocked authors, one batched identity lookup). The two missing guarantees were pinned by new tests: an API-level test driving the real controller (valid block → 204 with the stored `BlockerUserId` equal to the JWT claim, so a viewer-from-body regression fails; self-block → 400; unknown target → the generic privacy 404 body; `DELETE` → 204 for blocked and never-blocked; `GET` → 200 bare list) and a paging test (more visible posts than `pageSize` → a full page of the newest visible posts, so a filter-after-`Skip`/`Take` regression cannot pass).
  - **Doc wording corrected with no product change:** the route does distinguish a registered account id (204) from an unregistered one (404) despite the generic body; the anonymous-post filtering is silent but **observable** across a block/unblock transition, so "block and observe" could unmask an anonymous author — stated honestly with an explicit prohibition; the favourites read is recorded as **accepted tech debt** (one constant extra query plus an id array that grows with the blocked authors' published-post count) and the over-stated "EF cannot translate" reason was removed; the Postman block example now uses a registered-author placeholder instead of the all-zero GUID that would 400.
  - **Re-verification (session `ses_ea0d872efffe33sfnrQXqZ8Teg`): ACCEPT.** Strict `--no-incremental` build 0 warnings / 0 errors; three consecutive runs of the combined filter **141/141** each, exit 0, identical (T7 side 65 = 16 T7 tests including the two added + T5 part-1 36 + the pre-existing Chat `BlockTests` 13 unmodified); F1 order-independence confirmed with no residual id-order dependence anywhere in the file; no product behaviour changed (the four reads still filter before paging, the migration still carries the unique index, the T8 storage work untouched).
  - **Recorded limitations and caveats:** the favourites id array is accepted tech debt; the block route distinguishes a registered id (accepted, mirrors the Chat design); evidence is unit/SQLite and EF-in-memory only with no runtime/HTTP/DB run; one out-of-manifest two-line addition was forced in `tests/Sanad.UnitTests/Community/AdminCommunityRestrictionQueryTests.cs` (the `CountingCommunityDbContext` wrapper implements `ICommunityDbContext`, so the new DbSet member required one property + one using; behaviour-free); and because nothing is committed, per-file authorship inside the uncommitted window cannot be proven by diff (both reviewers noted this) — what is verifiable is that the current behaviour matches the reviewed description and is now pinned by the new tests.

### T8 — Landing shape/stats/testimonials + SVG (approved) — `completed`
- **Objective:** bilingual nested shape for all 11 stored types documented; live
  facility count via `/landing/stats` (no fake count; 60-s ISR accepted);
  testimonial badge/anonymity preserved; **SVG upload + safe serving**.
- **SVG design (approved, concrete):** accept `image/svg+xml` **after
  sanitization** in the upload path, not MIME alone:
  - Upload: `src/API/Sanad.API/Controllers/AdminLandingController.cs` (`UploadMedia`,
    `SaveImageAsync`, `HasMatchingImageSignature`) — add `image/svg+xml`.
  - Sanitizer: **NEW** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingSvgSanitizer.cs`
    (strip `<script>`, `<foreignObject>`, `on*` attributes, external references).
  - Storage: `src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs`
    (`AllowedContentTypes`, extension map, reverse map) — add `image/svg+xml` →
    `.svg`. **Note:** this allowlist is shared by all uploaders; the change is
    global and covered by storage tests.
  - Serving: `src/API/Sanad.API/ApplicationBuilderExtensions.cs` (`UseStaticFiles`
    + `PrivateStorageRequestGuard`) — harden `.svg` responses (`X-Content-Type-Options:
    nosniff` + a strict `Content-Security-Policy`), and only serve
    sanitizer-passed files.
- **Manifest:** `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingSectionResponses.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingSectionMappings.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/LandingRefResolver.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetPublishedLandingSectionsQueryHandler.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Landing/GetLandingStatsQueryHandler.cs`;
  the SVG files above; docs `docs/app/public/landing.md`, `docs/admin/landing.md`;
  Postman `docs/postman/app/Sanad.App.Public.postman_collection.json`,
  `docs/postman/admins/Sanad.Admin.postman_collection.json`;
  tests `tests/Sanad.UnitTests/Cms/LandingReadsTests.cs`,
  `tests/Sanad.UnitTests/Cms/LandingRefsTests.cs`,
  `tests/Sanad.UnitTests/Cms/LandingAdminTests.cs`,
  `tests/Sanad.UnitTests/Storage/LocalFileStorageTests.cs`.
- **T8 acceptance verdict (independent acceptance review; session `ses_ea0d872efffe33sfnrQXqZ8Teg`): ACCEPT — no blocking defect. T8 is `completed`.**
  - **Delivered (Worker B; session `ses_ea0e27e04ffeY1E14YEcikIRSc`, model `river/deepseek-ai/DeepSeek-V4.1-Flash#high`):** over an authoring turn, a gate turn and a hardening turn.
  - **Items 1–3 required no behaviour change, and were verified as truthfully "document + test", not under-delivered:** all 11 stored Landing section types already produce the bilingual title/body with raw `contentJson`; `/landing/stats` already counts approved facility rows live per request with an honest zero and no constant; testimonials already preserve genuine stars/text and map an anonymous reviewer to the `Verified family` / `عائلة موثقة` badge with no invented role or endorsement (the wire record has exactly six members).
  - **Item 4 (SVG) implemented:** NEW `LandingSvgSanitizer` (DTD processing prohibited and the XML resolver null, so XXE and entity expansion are impossible rather than filtered; `ConformanceLevel.Document`; strips `<script>`, `<foreignObject>`, `<style>`, `<iframe>`, `<embed>`, `<object>` and the five SMIL elements `set`/`animate`/`animateTransform`/`animateMotion`/`discard` with their contents; strips `on*` attributes; keeps only same-document `#fragment` references in `href`/`xlink:href`/`src`; drops any retained attribute whose `url(…)` is not a same-document `url(#…)`; rejects on parse failure, a non-`svg` root, empty input and over-depth; the stored bytes are always the sanitizer's output). `MaxNestingDepth` 256 is enforced by an **iterative streaming pre-pass over the raw bytes before any DOM load, recursive walk or serialization**, so a deeply nested document is rejected with nothing stored instead of causing an uncatchable `StackOverflowException`. The shared storage allowlist gains `image/svg+xml`, but SVG is **folder-gated to `landing`** (`SvgAllowedFolders`), so the three uploaders that have no image-signature check of their own (assessment illustrations, splash screens, lookup icons) still reject SVG with `Storage.File.UnsupportedType`, and the private store also has no SVG. Serving adds `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; sandbox` for `.svg` responses only.
  - **Deviation from the literal approved design — recorded as an Owner item, not hidden:** the brief said the allowlist change is global ("shared by all uploaders"); the worker instead folder-gated SVG to `landing` because a literal global allowlist would let the three signature-less uploaders store an unsanitized SVG served from the API origin. Both reviewers verified the gate is sound (exact match after folder normalization, no traversal or aliasing, `UnsupportedType` returned before anything is written) and recommended keeping it. Consequence recorded: `docs/admin/care-assessments.md:83-84` and `:143-144` and audit item #46 ("JPEG/PNG/WebP is correct; do not change the correct picker to SVG") remain TRUE only while the gate stays — if the literal global allowlist is ever chosen, those lines become false unless the assessment upload path gains its own rejection. Ruling still pending with the Owner.
  - **Second Owner item:** the `testimonials`/`faqs` sibling shape — for a resolving type the sibling array is `[]` while every other type returns `null`; it is pre-existing behaviour, now documented truthfully and pinned by tests, and both reviewers recommend keeping it for now because normalising it to `null` is client-visible and belongs in its own wire-affecting task.
  - **Evidence:** strict `--no-incremental` build **0 warnings / 0 errors**; focused filter **56/56** at acceptance, then **76/76** after the hardening (20 tests added); final combined re-verification **141/141** on three consecutive runs, exit 0. Honest earlier failures recorded: the gate turn's first run was **54/56** — its own test asserted an empty sibling array where production returns `null` and vice versa, and another serialized PascalCase instead of the web contract — both test defects fixed and one false doc sentence corrected; and the T7 worker's full rebuild had surfaced an `xUnit2031` warning in T8's file, cleared in the gate turn with the predicate overload (no suppression, no weakened assertion).
  - **Hardening findings closed after acceptance:** the MEDIUM depth/DoS gap (closed by the iterative pre-pass, with 50 000-level, 256/257-boundary and end-to-end 400-with-empty-storage tests); the LOW–MEDIUM SMIL vector (closed by removing the five elements); the LOW external `url()` egress (closed for `url(…)` arguments; the residual `image-set(…)`/`style`-attribute egress stays in the accepted LOW class and is blocked by the served CSP); the incorrect cap wording (now: 5 MB request cap versus the persisted `Storage:Local:MaxBytes` default 2 MiB and the `400 Storage.File.TooLarge` outcome, in `docs/admin/landing.md` and the Admin Postman collection); and the coverage gaps (a controller→sanitizer→storage wiring test asserting the bytes actually persisted contain no `script`/`alert`/`onload` while safe markup survives; strip cases for a case-varied name, a namespace-prefixed name, a comment/CDATA payload and `iframe`/`embed`/`object`; and a `SavePrivateAsync` SVG rejection test).
  - **Also recorded:** the brief's storage test path was wrong — the real file is `tests/Sanad.UnitTests/BuildingBlocks/LocalDiskFileStorageTests.cs`; the stale `docs/app/public/landing.md` H1 ("Phase 1 reads") was corrected; and the already-sent frontend follow-up report line 38 ("SVG … not yet available") is now stale and cannot be unsent, so a correction should accompany any release.
  - **Residual risk to record:** the folder gate keys on the folder **name**, so a future caller passing `"landing"` would inherit SVG storage without sanitizing — "landing is reserved for the sanitized SVG path" must stay an explicit review/ledger rule, and any future SVG path should be expressed in the storage API rather than by growing the folder list.

- **Wave 4 closeout:** T7 and T8 both accepted. T7 was rejected as delivered and then accepted at re-verification by session `ses_ea0d1a7f5ffel8R7a4GwCYdX4c` → `ses_ea0d872efffe33sfnrQXqZ8Teg`; T8 was verified by session `ses_ea0d872efffe33sfnrQXqZ8Teg`. Both gates independently reproduced (build 0 warnings / 0 errors; T7 side 65, T8 side 76, combined 141/141 on three consecutive runs). Worker A for Wave 4 was session `ses_ea06d8d1affej5KJyDwi77uu2W` on `opencode/minimax-m3.1-flash-free#high`; Worker B was session `ses_ea0e27e04ffeY1E14YEcikIRSc` on `river/deepseek-ai/DeepSeek-V4.1-Flash#high`. Nothing was committed, pushed or deployed; the persistent local `SanadDb` was untouched by both tasks. **Correction (2026-10-22, factual):** only **T7** generated a migration — the Community `20261021192605_AddCommunityAuthorBlocks` (files only, never applied); **T8 generated no migration at all** (the Landing/SVG work changed the shared storage allowlist only). The earlier "both tasks' migrations were generated as files only" wording was wrong.

- **Wave 5 kickoff:** T9 Partner contact + public legal (approved, **solo** wave) is dispatched to Worker A, session `ses_ea06d8d1affej5KJyDwi77uu2W`, model `opencode/minimax-m3.1-flash-free#high`, `in_progress`, HEAD pinned at `4fa7c77e3544abb0650aa5baf6c24358a1b48654`, holding the sole build/test slot; Worker B in this wave is the brief scribe only and must not run dotnet. T10 and T11 stay `pending`.
- **Owner decisions (2026-10-22) — recorded as given:** (1) **T9-B (public legal) stays deferred** until Product/Content answer the frontend team's **WEB-08** ("the actual intended audience", the AR/EN doc owners, the authoritative texts — the team explicitly asked the backend not to pick an audience arbitrarily); the backend must not guess an audience. (2) **T10 and T11 continue** (sequentially, after this T9-A correction). (3) The **Landing-only SVG folder gate stays** (SVG storage is reserved for the sanitized Landing path). (4) The **existing `[]` / `null` response shapes stay unchanged** — *scope clarified (2026-10-22, Worker A, brief scribe):* that decision is about the **LANDING sibling arrays** (`testimonials: []` / `faqs: []` / `supportContact: null` on the landing read). It is **not** a freeze on every DTO in the codebase: an additive, documented member on a **new** response shape (as T11's public site-settings read adds `navbarLinks` / `seo`) is allowed, and the existing shapes themselves stay exactly as they are. **Release is NOT authorized** by this decision; nothing is deployed, committed or pushed.
- **Wave 6 (T10, solo):** Worker A (session `ses_ea06d8d1affej5KJyDwi77uu2W`, model
  `opencode/minimax-m3.1-flash-free#high`) delivered the subscription plan-owned
  bilingual copy and handed it back as **`ready_for_review`** (contract, migration,
  gate evidence and the one out-of-manifest configuration file are recorded in the
  T10 section below). Gate: build `Sanad.slnx -c Release --no-incremental` → 0
  warnings / 0 errors; focused filter `SubscriptionQueryTests|SubscriptionPlanAuthoringCommandTests|FamilySubscriptionsControllerTests|AdminSubscriptionsControllerTests`
  → 46/46. The only migration generated was the Families
  `20261022044106_AddSubscriptionPlanVersionCopy` (files only, never applied).
  **Review of record (`ses_ea0d1a7f5ffel8R7a4GwCYdX4c`): ACCEPTED with Low
  findings** (reviewer reproduced build 0/0 and 46/46); the out-of-manifest
  `FamilySubscriptionConfiguration.cs` amendment was accepted as necessary.
  A **bounded correction turn** then closed the Low findings (wording, wire
  shape and the two test gaps) and is `ready_for_review` pending focused
  verification. **T10 is `completed` under the explicit Mastermind verdict
  (2026-10-22) with the final independent 48/48 and the valid strict 0/0
  evidence; the InMemory-only limitation and the earlier unauthorized
  whole-suite deviation are recorded in the T10 section.** **T9-B stays
  deferred/blocked pending WEB-08.** Nothing was committed, pushed or
  deployed, and no database was touched.
- **Wave 7 (T11, solo):** Worker A (session `ses_ea06d8d1affej5KJyDwi77uu2W`,
  model `opencode/minimax-m3.1-flash-free#high`, HEAD
  `4fa7c77e3544abb0650aa5baf6c24358a1b48654`, sole build/test slot) delivered
  the single-row bilingual **SiteSettings** CMS aggregate (editable navbar links
  + SEO defaults), the existing-policy admin authoring surface and one
  anonymous public read, and handed it back as **`ready_for_review`** (the
  contract, the singleton strategy, the migration and the evidence are recorded
  in the T11 section below). Gate: build `Sanad.slnx -c Release --no-incremental`
  → **0 warnings / 0 errors**; focused filter
  `SiteSettingsAdminTests|SiteSettingsReadTests|SiteSettingsControllerTests` →
  **16/16**. The only migration generated was the CMS
  `20261022060548_AddSiteSettings` (files only, never applied; predecessor
  `20261021120000_AddFamilyHomeSections`). The first review **rejected** the
  delivery (one Medium: a null navbar element ended in an unhandled 500); the
  bounded correction turn closed it and the reviewer then **accepted** T11, so
  T11 is **`completed`** on unit-level evidence (18/18) — no HTTP run, no
  database and no migration apply are claimed. **T9-A accepted and T9-B
  deferred/blocked pending WEB-08; the plan is not complete and release is NOT
  authorized.** Nothing was committed, pushed or deployed, and no database was
  touched.

### T9 — Partner contact + public legal (approved) — T9-A `accepted` (F1 fix verified 64/64); **T9-B deferred/blocked** pending WEB-08 (T9 as a whole stays partial/deferred)
- **Partner contact (settled destination = admin support inbox, proper
  application layer, no fake identity):** because `SupportTicket.Create` requires a
  non-empty `UserId`, partner contact needs its own persisted aggregate and a real
  command path:
  - **NEW** domain `src/Modules/Identity/Domain/Sanad.Modules.Identity.Domain/Support/PartnerContact.cs`
    (+ **NEW** `PartnerContactId.cs`) storing the contact's own submitted
    name/company/email as **request data** (not a user account).
  - **NEW** config `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Configurations/PartnerContactConfiguration.cs`;
    **NEW** migration + designer + snapshot edit in
    `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/Migrations/`;
    edits to `src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs`.
  - **NEW** application `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/SubmitPartnerContactCommand.cs`
    + **NEW** `SubmitPartnerContactCommandHandler.cs` (persists via
    `IIdentityDbContext`; this is the application layer, **not** API-layer
    persistence) + **NEW** `SubmitPartnerContactCommandValidator.cs` +
    **NEW** `PartnerContactErrors.cs`.
  - **NEW** `src/API/Sanad.API/Controllers/PartnerContactController.cs` (anonymous;
    applies the endpoint rate-limit policy and an idempotency key to the command;
    minimal anti-spam).
  - **NEW** admin read `src/Modules/Identity/Application/Sanad.Modules.Identity.Application/Support/AdminPartnerContactQueries.cs`
    + route on the existing admin support surface; docs `docs/admin/support-inbox.md`
    (see collision note) or **NEW** `docs/admin/partner-contact.md`.
- **Public legal (anonymous Landing read):** **NEW**
  `src/API/Sanad.API/Controllers/PublicLegalController.cs` reusing the CMS legal
  read with an explicit audience
  (`src/API/Sanad.API/Controllers/LegalController.cs` stays `NormalAccess` and is
  **not** edited by T9). Docs `docs/app/settings/legal.md`.
- **Manifest (docs/Postman/tests):** `docs/app/public/landing.md` (**NEW** public
  doc `docs/app/public/partner-contact.md`, **NEW** `docs/app/public/legal.md` if
  separate), `docs/app/settings/legal.md`, Postman
  `docs/postman/app/Sanad.App.Public.postman_collection.json`,
  `docs/postman/admins/Sanad.Admin.postman_collection.json`;
  **NEW** `tests/Sanad.UnitTests/API/PartnerContactControllerTests.cs`,
  **NEW** `tests/Sanad.UnitTests/API/PublicLegalControllerTests.cs`,
  `tests/Sanad.UnitTests/Identity/Support/PartnerContactTests.cs`.
- **T9-A result (Worker A, session `ses_ea06d8d1affej5KJyDwi77uu2W`, model
  `opencode/minimax-m3.1-flash-free#high`): `ready_for_review` for the partner
  contact; T9 as a whole remains partial/blocked and is never `completed`.**
  - **T9-B (public legal) is NOT delivered — deferred, not guessed.** The
    ambiguity is recorded with evidence: `LegalAudience`
    (`src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Legal/LegalAudience.cs`)
    has exactly five members and **no public/all/anonymous member** —
    `Family = 1`, `MedicalCaregiver = 2`, `CompanionCaregiver = 3`,
    `Elderly = 4`, `CareHomeOwner = 5` — all *app* audiences; legal content is
    published per `(documentType, audience)`
    (`cms.legal_documents`, unique on `document_type + audience + version`), so
    the audiences carry **different** content; the public landing page is the
    entry point for prospective families **and** caregivers, so more than one
    member is plausible; and the frontend team's own **WEB-08** (Required,
    Frontend web + Product/Content, linked to this task) asks for "the actual
    intended audience" and states "**Please do not ask us to pick an audience
    arbitrarily**" — still unanswered. Per the task instruction the backend
    stopped instead of guessing: **no `PublicLegalController.cs`, no
    `PublicLegalControllerTests.cs` and no `docs/app/settings/legal.md` edit**
    (its current text — "Both routes are for signed-in app accounts" — stays
    accurate). Unblocking needs one line from WEB-08: the intended
    `LegalAudience` member (or a new "public" audience, which would be a domain
    + migration change). `LegalController` behaviour is unchanged.
  - **T9-A contract (as built).** `POST /api/v1/partner-contact` — anonymous
    (explicit `[AllowAnonymous]`, no token read, no identity faked): body
    `{name (2–200), company? (≤200), email (≤320), subject? (≤150, max-only),
    message (10–2000)}` plus the **required** `Idempotency-Key` header →
    `200 {contactId, submittedOnUtc}`; `400 Api.Validation.Failed` /
    `400 Identity.PartnerContact.Invalid` (nothing persisted); `409
    Identity.PartnerContact.IdempotencyConflict` (same key, different content);
    `429 Identity.PartnerContact.RateLimited` + `Retry-After` (5 submissions per
    rolling hour per caller). The submitted details are stored as **request
    data** on their own aggregate — no user row, no user id — and the
    server-derived caller scope (a one-way digest of the request address) is
    never returned by any read. Admin read: `GET
    /api/v1/admin/support-tickets/partner-contacts` (+ `/{id}`) on the
    **existing** admin support surface with the **existing `SupportInboxAdmin`
    policy** — no new policy. Migration: Identity
    `20261021205122_AddPartnerContacts` (files only, predecessor
    `20261020191733_AddSupportTicketWorkflow`, **never applied**); no database
    was read or written.
  - **Initial independent review (session `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`):
    ACCEPTED with one MEDIUM finding (F1).** The delivered T9-A gate was
    **build 0 warnings / 0 errors** and **58/58** on
    `--filter "FullyQualifiedName~PartnerContact|FullyQualifiedName~PublicLegal|FullyQualifiedName~Legal"`
    (15 new partner-contact tests + 43 pre-existing legal tests). F1: the
    submit handler pre-checked `(clientKey, idempotencyKey)` and then
    `Add`/`SaveChangesAsync` **without catching the unique-index race**, so two
    concurrent identical retries could answer `500` instead of the same
    receipt, and a same-key/different-content race could answer `500` instead
    of `409`.
  - **F1 correction (this turn, same worker/session).** The handler now catches
    **only** a unique/index violation (`DbUpdateException` filtered by
    PostgreSQL SQLSTATE `23505` and the SQLite/SQL Server unique messages —
    mirroring the house `ChatPersistenceConflicts.IsUniqueViolation` shape; any
    other database failure is **not** caught), detaches the failed added row
    when the context is a `DbContext` (the house `SosApplication` pattern), and
    re-reads the persisted winner by the exact `(clientKey, idempotencyKey)`
    pair. Same content → the winner's receipt; different content → the
    `Identity.PartnerContact.IdempotencyConflict`; **no winner → the original
    failure is re-thrown** (never a false success); an unrelated
    `DbUpdateException` and a cancellation both **propagate**. The pre-check
    and the race path now share one `SameContent` comparison, so a sequential
    retry and a raced retry answer identically. No schema change, no new
    dependency, no retry loop, no process-local locking, and the rate-limit
    policy is unchanged. The unused `MinimumSubjectLength` constant was removed
    from the domain (the subject stays **max-only**); no new validation was
    imposed. Cheap reviewer findings closed: F3 — the test now asserts the
    **explicit `AllowAnonymous`** attribute and that the submit action carries
    no `[Authorize]`; F5 — the test now asserts the **web-serialized** receipt
    shape (`contactId.value` = the guid, `submittedOnUtc` = the UTC instant).
  - **New failure-path tests (6, in the two owned test files).**
    `Submit_returns_the_persisted_winner_when_a_concurrent_retry_lost_the_unique_race`
    (unique violation + a persisted winner with the same payload → the winner's
    receipt, and the failed insert left nothing behind);
    `Submit_conflicts_when_a_concurrent_race_won_with_different_content` (same
    race, different payload → `409`); `Submit_propagates_a_unique_violation_when_no_winner_was_persisted`
    (no winner → the failure propagates, no false success);
    `Submit_does_not_swallow_an_unrelated_persistence_failure`; 
    `Submit_does_not_swallow_a_cancellation`; and
    `The_receipt_is_serialized_in_the_documented_web_shape` (F5). The race is
    induced with a test double over the in-memory store (the EF in-memory
    provider does not enforce unique indexes), so the catch/re-read path itself
    is executed — not just the sequential idempotency. No new framework, no
    persistent database.
  - **Gate after the F1 correction.** `dotnet build Sanad.slnx -c Release
    --no-incremental` → **exit 0, 0 warnings, 0 errors**; `dotnet test
    tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build
    --filter "FullyQualifiedName~PartnerContact|FullyQualifiedName~PublicLegal|FullyQualifiedName~Legal"`
    → **exit 0, Total 64, Passed 64, Failed 0, Skipped 0** (21 partner-contact
    tests + 43 pre-existing legal tests). Unit level only: no runtime host, no
    HTTP evidence, no database access, no migration applied, no deployment, no
    commit or push.
  - **Next action:** independent re-review of the F1 correction (and the closed
    F3/F5 findings) by the reviewer of record. T10 and T11 follow sequentially
    afterwards; Worker A does not start them in this turn.

### T10 — Subscription plan-owned bilingual copy — `completed` (Mastermind verdict, 2026-10-22)
- **Objective (settled design):** add **version-owned bilingual text fields** to
  the plan version — `ArabicName`/`EnglishName`, `ArabicDescription`/
  `EnglishDescription`, and per-benefit bilingual labels — preserving existing
  version/financial semantics (price/cycle/currency/limits unchanged). Text is
  **authored** via the existing admin authoring path, never generated.
- **Manifest:** `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionPlanVersion.cs`;
  `src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionBenefit.cs`;
  `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Configurations/SubscriptionPlanVersionConfiguration.cs`;
  **NEW** migration + designer + snapshot edit in
  `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Migrations/`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionCommands.cs`
  (`CreateSubscriptionPlanVersionCommand`);
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/SubscriptionQueries.cs`;
  `src/Modules/Families/Application/Sanad.Modules.Families.Application/Subscriptions/AdminSubscriptionQueries.cs`;
  `src/API/Sanad.API/Controllers/FamilySubscriptionsController.cs`;
  `src/API/Sanad.API/Controllers/AdminSubscriptionsController.cs`;
  `docs/app/families/subscriptions.md`; `docs/admin/subscriptions.md`;
  `docs/postman/app/Sanad.App.Family.postman_collection.json`,
  `docs/postman/admins/Sanad.Admin.postman_collection.json`;
  tests `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionQueryTests.cs`,
  `tests/Sanad.UnitTests/Families/Subscriptions/SubscriptionPlanAuthoringCommandTests.cs`,
  `tests/Sanad.UnitTests/API/FamilySubscriptionsControllerTests.cs`,
  `tests/Sanad.UnitTests/API/AdminSubscriptionsControllerTests.cs`.
- **T10 result (Worker A, session `ses_ea06d8d1affej5KJyDwi77uu2W`, model
  `opencode/minimax-m3.1-flash-free#high`): `ready_for_review`.** Unit level
  only — no runtime host, no HTTP evidence, no database access, no migration
  applied, no deployment, no commit and no push. Release stays unauthorized.
  - **Contract as built (wire).** The copy is **version-owned** and **nullable**:
    `SubscriptionPlanVersion.ArabicName` / `EnglishName` (max 200 characters
    each), `ArabicDescription` / `EnglishDescription` (max 1000 each) and, per
    benefit row of that same version, `SubscriptionBenefit.ArabicLabel` /
    `EnglishLabel` (max 200 each). Values are trimmed; empty/whitespace becomes
    absent; an over-long value is rejected (`400 Subscriptions.Plan.InvalidTerms`).
    The family catalog (`GET /api/v1/family/subscriptions/plans`) and the admin
    plan list/detail reads return them as `arabicName`, `englishName`,
    `arabicDescription`, `englishDescription` and per-benefit `arabicLabel` /
    `englishLabel`. All new members are **trailing optional** on the existing
    records. The admin authoring path
    (`POST /api/v1/admin/subscriptions/plans`) accepts the same optional fields
    (version copy + per-benefit labels) and forwards them unchanged; no new
    endpoint was added.
  - **Wire-shape impact (additive, stated precisely).** The catalog and the
    admin plan list/detail responses gain the new members. The
    **current-subscription / snapshot benefit objects also gain additive
    `arabicLabel: null` and `englishLabel: null` members** (the family
    `GET /api/v1/family/subscriptions/current` benefits and the admin
    family-subscription list/detail benefits share `SubscriptionBenefitResponse`).
    The snapshot rows themselves stay **terms-only**: they do not persist the
    copy and always report the labels as absent, so a subscription snapshot is
    not retro-labelled by a later plan-version copy edit.
  - **Authored, never generated.** Nothing in the stack translates, generates,
    back-fills or defaults the copy. The value is stored as authored **except
    that surrounding whitespace is trimmed and a whitespace-only value is stored
    as absent (`null`)** — it is not returned byte-for-byte verbatim. Legacy
    versions (created before the copy existed) and versions authored without
    copy keep `null` for every copy field — honest-absent, no invented
    fallback, and the frontend must own its own fallback text.
  - **Financial/version semantics unchanged.** Price, cycle, currency, member
    limit, monthly-booking limit, rollover, benefit inclusion, publish/retire
    behaviour and subscription-snapshot behaviour are untouched; the copy is
    display-only. Two versions of the same plan key keep their own copy
    (publishing one never mutates the other).
  - **Migration (files only, never applied).** NEW
    `20261022044106_AddSubscriptionPlanVersionCopy` (+ designer + model-snapshot
    edit) in
    `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Migrations/`;
    predecessor `20261018170707_AddCaregiverMedicalAccessAudit`. Up adds six
    nullable columns with **no defaults and no back-fill** (four on
    `families.subscription_plan_versions`, two on
    `families.subscription_plan_version_benefits`); Down drops all six. The
    snapshot's `family_subscription_benefits` tables are unchanged.
  - **One file outside the manifest (deliberate, reported, and now accepted by
    the reviewer of record as necessary):**
    `src/Modules/Families/Infrastructure/Sanad.Modules.Families.Infrastructure/Persistence/Configurations/FamilySubscriptionConfiguration.cs`
    — **4 `Ignore` calls** (the two label properties × the two snapshot-owned
    benefit collections `family_subscription_benefits` and
    `family_subscription_pending_downgrade_benefits`), because a subscription
    snapshot stores the benefit terms only. Without it the shared owned type
    would have added label columns to the snapshot tables (schema beyond the
    approved copy scope). No snapshot behaviour, field or persisted shape
    changed.
  - **Gate evidence (this turn, final code).**
    `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded,
    0 Warning(s), 0 Error(s)**.
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~SubscriptionQueryTests|FullyQualifiedName~SubscriptionPlanAuthoringCommandTests|FullyQualifiedName~FamilySubscriptionsControllerTests|FullyQualifiedName~AdminSubscriptionsControllerTests"`
    → **Passed! Failed: 0, Passed: 46, Skipped: 0, Total: 46**.
  - **Docs/Postman.** `docs/app/families/subscriptions.md` gained a "Bilingual
    plan copy" section with a wire example; `docs/admin/subscriptions.md`
    documents the authoring fields, the limits and the example request; the
    Postman app plan-catalog item and the Postman admin plan create/list/detail
    items carry the copy. Both collections re-parse as valid JSON.
  - **Next action:** independent review of T10. **T11 stays `pending`** *(at
    that point; T11 is now `completed` — see the T11 section)* and
    **T9-B stays deferred/blocked** pending WEB-08 — neither is changed by this
    task.
- **T10 review of record (reviewer `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`):
  ACCEPTED with Low findings.** The reviewer independently reproduced the
  gate: **build 0 warnings / 0 errors** and the focused filter **46/46**. The
  out-of-manifest configuration amendment above was **accepted as necessary**
  (4 `Ignore` calls in `FamilySubscriptionConfiguration.cs`, both snapshot
  collections). Recorded limitation: **there is no runtime relational
  evidence** for this task — unit-level only, no HTTP/DB run, and the migration
  was never applied. **Process deviation recorded (must not recur):** the
  previous handback included an unsolicited **whole-suite** `dotnet test` run
  outside the approved focused gate; that was out of process and must not be
  repeated — the approved gate is the single strict build plus the single
  focused filter only.
- **T10 correction turn (Worker A, same session, bounded correction only):**
  `ready_for_review` **pending focused verification**. Scope: wording only in
  `docs/admin/subscriptions.md`, the subscription text of
  `docs/postman/admins/Sanad.Admin.postman_collection.json`, the two
  subscription test files, and this brief. No product, migration or config
  change.
  - **Wording corrected (the "verbatim" claim was false).** The admin doc,
    the admin Postman descriptions and this brief no longer claim the copy is
    stored/returned "verbatim"/"exactly as authored"; they now state the real
    contract: the value is authored text stored **with surrounding whitespace
    trimmed, and a whitespace-only value stored as absent (`null`)** — never
    generated, translated or back-filled.
  - **Wire shape stated precisely (the "all shapes unchanged" claim was
    wrong).** The brief now records that the current-subscription and admin
    family-subscription benefit objects gain **additive**
    `arabicLabel: null` / `englishLabel: null` members, while the snapshot
    rows stay terms-only and never persist the copy.
  - **Test gaps closed (real handler paths, no mock DTO).**
    `SubscriptionQueryTests.Catalog_returns_the_version_owned_bilingual_copy_and_benefit_labels`
    now authors the copy **and a per-benefit AR/EN label** through the real
    `CreateSubscriptionPlanVersionCommandHandler`, publishes through the real
    `PublishSubscriptionPlanVersionCommandHandler`, and asserts the labelled
    benefit's AR/EN labels (and an unlabelled benefit's absent labels) from the
    real `GetSubscriptionCatalogQueryHandler` **and** from the real
    `GetAdminSubscriptionPlanQueryHandler` detail read.
    `SubscriptionPlanAuthoringCommandTests.Two_versions_of_the_same_key_keep_their_own_copy`
    now authors **distinct benefit labels per version** and asserts both
    versions keep their own labels after publishing one of them. Two bounded
    authoring cases were added with the existing helper: whitespace/trim
    normalization (`'  Premium  '` → `'Premium'`, whitespace-only → `null`,
    same for a benefit label) and the over-long rejections (description 1001,
    benefit label 201) proving **nothing is persisted**.
  - **Out of scope, reported not edited:** `docs/app/families/subscriptions.md`
    and the app Postman plan-catalog description still say the copy is
    "returned exactly as authored"; they were not in this turn's owned paths and
    are flagged for the next owning turn.
  - **Gate for this correction (single strict build + single focused filter,
    no whole-suite run, no EF command, no database):**
    `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded,
    0 Warning(s), 0 Error(s)** (exit 0);
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~SubscriptionQueryTests|FullyQualifiedName~SubscriptionPlanAuthoringCommandTests|FullyQualifiedName~FamilySubscriptionsControllerTests|FullyQualifiedName~AdminSubscriptionsControllerTests"`
    → **Passed! Failed: 0, Passed: 48, Skipped: 0, Total: 48** (exit 0; the
    focused filter now runs 48 tests — the 46 of the reviewed delivery plus the
    two added authoring cases: whitespace/trim normalization and the over-long
    description/label rejection).
    The correction turn is **`ready_for_review` pending focused verification**;
    it does not mark T10 `completed`.
- **T10 docs-only completion (Worker A, same session; product unchanged):**
  `ready_for_review`. The previous scope restriction had wrongly excluded two
  original T10-owned consumer files; they are now explicitly authorized and
  corrected. Only these files changed: `docs/app/families/subscriptions.md` and
  the **subscription plan-catalog description only** in
  `docs/postman/app/Sanad.App.Family.postman_collection.json`, plus this note.
  - **Wording now uniform across the four T10 consumer files** (app/admin
    subscriptions docs + app Family/admin Admin Postman subscription
    descriptions): the copy is **authored**, stored **with surrounding
    whitespace trimmed**, and a **whitespace-only value is stored as absent
    (`null`)**; it is **never generated, translated or back-filled**. The
    earlier "returned exactly as authored" / "verbatim" wording is gone
    everywhere; limits (name 200, description 1000, benefit label 200) and
    nullability are retained, and the accepted T1 payment / T3 legal content
    is untouched.
  - **Read-only sweep of the four consumer files** found **no other
    contradictory verbatim / "exactly as authored" T10 claim**; the remaining
    matches in those files ("exactly once", "unchanged", "pending state
    unchanged") belong to other features (enum/benefit rules, coupon
    normalization, downgrade, legal audience, CMS video) and were not touched.
  - **Verification:** both Postman collections parse as valid JSON
    (`Sanad.App.Family` and `Sanad.Admin`), the admin plan-create body still
    parses with its copy fields, and the changed app description was read back
    in context. **No dotnet build, no test run, no EF command and no database
    access in this docs-only turn** — the final-code evidence stands:
    build **0 warnings / 0 errors** and focused filter **48/48**.
- **T10 completion verdict (Mastermind, 2026-10-22 — recorded by the brief
  scribe, Worker A).** T10 is **`completed`** under this explicit Mastermind
  verdict: the reviewer of record (`ses_ea0d1a7f5ffel8R7a4GwCYdX4c`) accepted
  the delivery with Low findings, and the final focused verification is
  **48/48** (`SubscriptionQueryTests|SubscriptionPlanAuthoringCommandTests|FamilySubscriptionsControllerTests|AdminSubscriptionsControllerTests`)
  on a valid strict build with **0 warnings / 0 errors** and no warning/error
  output.
  - **Final evidence:** strict build `dotnet build Sanad.slnx -c Release --no-incremental`
    → **Build succeeded, 0 Warning(s), 0 Error(s)**; focused filter →
    **Passed! Failed: 0, Passed: 48, Skipped: 0, Total: 48**.
  - **Corrected contract wording (kept):** the copy is authored text stored
    **with surrounding whitespace trimmed**, a **whitespace-only value is
    stored as absent (`null`)**, and it is **never generated, translated or
    back-filled** — it is not returned byte-for-byte verbatim.
  - **Snapshot additive labels (kept):** the current-subscription and admin
    family-subscription **benefit objects** gain additive
    `arabicLabel: null` / `englishLabel: null` members; the **snapshot rows
    stay terms-only** and never persist the copy.
  - **Disclosed limitation:** the T10 evidence is **InMemory-provider unit
    evidence only**. The relational behaviour of the Families migration
    (`20261022044106_AddSubscriptionPlanVersionCopy`, files only, never
    applied) and the shared owned-type binding were **not** exercised against
    a real relational database in this phase, and there is **no runtime HTTP
    evidence**.
  - **Process deviation recorded (must not recur):** an earlier T10 handback
    included an **unsolicited whole-suite** `dotnet test` run outside the
    approved focused gate. That was out of process; the approved gate is one
    strict build plus one focused filter only.

### T11 — Navbar/SEO editable site settings (approved) — `completed` (reviewer acceptance, 2026-10-22)
- **Objective (minimal consistent design, explicit recommendation):** one
  single-row, bilingual **`SiteSettings`** CMS aggregate mirroring the existing
  Landing/Splash pattern (editable navbar links + SEO defaults), with an admin
  authoring surface and one anonymous read. This is the recommended design; no
  Owner registry approval is required.
- **Manifest (all NEW unless noted):** `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Settings/SiteSettings.cs`;
  `src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Settings/SiteSettingsId.cs`;
  `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Configurations/SiteSettingsConfiguration.cs`;
  migration + designer + snapshot edit in
  `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/`;
  edits to `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/CmsDbContext.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Abstractions/Data/ICmsDbContext.cs`;
  `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/DependencyInjection.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Settings/SiteSettingsCommands.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Settings/SiteSettingsQueries.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Settings/SiteSettingsValidators.cs`;
  `src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Settings/SiteSettingsResponses.cs`;
  `src/API/Sanad.API/Controllers/AdminSiteSettingsController.cs`;
  `src/API/Sanad.API/Controllers/PublicSiteSettingsController.cs`;
  docs `docs/admin/site-settings.md`, `docs/app/public/site-settings.md`;
  Postman `docs/postman/admins/Sanad.Admin.postman_collection.json`,
  `docs/postman/app/Sanad.App.Public.postman_collection.json`;
  tests `tests/Sanad.UnitTests/Cms/SiteSettingsAdminTests.cs`,
  `tests/Sanad.UnitTests/Cms/SiteSettingsReadTests.cs`,
  `tests/Sanad.UnitTests/API/SiteSettingsControllerTests.cs`.
- **Wave 7 dispatch:** T11 (solo) to Worker A, session
  `ses_ea06d8d1affej5KJyDwi77uu2W`, model
  `opencode/minimax-m3.1-flash-free#high`, HEAD pinned at
  `4fa7c77e3544abb0650aa5baf6c24358a1b48654`, holding the sole build/test
  slot; the brief scribe writes only this brief.
- **T11 result (Worker A, same session): `ready_for_review`.** Unit level
  only — no runtime host, no HTTP evidence, no database access, no migration
  applied, no deployment, no commit and no push. Release stays unauthorized.
  - **Contract as built.** Two routes and one existing policy:
    `GET /api/v1/site-settings` (**`AllowAnonymous`**) and
    `GET`/`PUT /api/v1/admin/site-settings` under the **existing
    `AuthorizationPolicies.CmsContent`** policy (SuperAdmin/ContentAdmin). No
    new policy, no new endpoint family, no change to an existing endpoint.
  - **Admin write (`PUT`, replace semantics).** The request replaces the whole
    document: `navbarLinks` (omitted ⇒ no links) and `seo` (omitted ⇒ no SEO
    defaults). Nothing is kept implicitly, generated or translated. A link
    needs `key`, `arabicLabel`, `englishLabel`, `url`; the **authored array
    order is the navbar order** and the reads return that same order. Bounds:
    20 links, key 60, labels 120, url 500, SEO title 200, SEO description 500.
    Values are stored as authored apart from surrounding whitespace being
    trimmed; a whitespace-only value becomes absent.
  - **Admin read (`GET`).** `200` returns
    `{ navbarLinks, seo, createdOnUtc, updatedOnUtc }`; `404
    Cms.SiteSettings.NotFound` before anything is authored.
  - **Public read (`GET`, anonymous).** `200` always, with
    `{ navbarLinks, seo }` only — no identity, no timestamps, no authoring
    metadata. **Empty store: `200` with `navbarLinks: []` and `seo: null`** —
    honest, never fabricated content (the same `[]`/`null` shape the landing
    sibling arrays use).
  - **Link target rule.** A target is a root-relative app route (`/library`)
    or an absolute `http`/`https` url. `javascript:`, `data:`, every other
    scheme and protocol-relative targets (`//host/path`, `/\host`) are
    refused. Reported as a bounded rule, not a silent guess: `mailto:`/`tel:`
    are **not** supported in this phase.
  - **Singleton strategy (persistence + application).** The row identity is
    fixed (`SiteSettingsId.Singleton` = key `1`); the domain can only create
    that key, and the table carries the check constraint
    `ck_site_settings_single_row` (`"id" = 1`), so a second row is impossible.
    A concurrent first write therefore loses the insert race and the handler
    maps the duplicate-key failure to **`409 Cms.SiteSettings.Conflict`**
    (house convention, like `Cms.Splash.InternalNameAlreadyInUse`) instead of
    letting an uncaught race escape.
  - **Errors/statuses.** `400 Cms.SiteSettings.Invalid` for unusable content
    (bad target, duplicated key, oversized value) with **no mutation**;
    `400 Api.Validation.Failed` for an out-of-bounds request shape;
    `404 Cms.SiteSettings.NotFound`; `409 Cms.SiteSettings.Conflict`; `200` on
    success. The two bounded `Cms.SiteSettings.*` statuses are mapped **locally
    in the admin controller** (the T8 `Cms.Landing.Media.*` precedent), so the
    shared `ResultProblemDetailsMapper` stays untouched.
  - **Migration (files only, never applied).** NEW
    `20261022060548_AddSiteSettings` (+ designer + model-snapshot edit) in
    `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/Migrations/`;
    predecessor `20261021120000_AddFamilyHomeSections` (verified as the current
    CMS head). Up creates `cms.site_settings` (PK `id`, `navbar_links_json`
    required, four nullable SEO columns, `created_on_utc`, `updated_on_utc`,
    PK constraint `PK_site_settings` + check constraint
    `ck_site_settings_single_row`); Down drops the table. **No seed data, no
    destructive change.**
  - **Manifest amendment (reported, not silently expanded).** The manifest
    listed `src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/DependencyInjection.cs`
    as an EDIT; **no DI edit was needed and none was made** — MediatR
    handlers and the FluentValidation validator are auto-registered from the
    CMS Application assembly by the existing API registration. **No file
    outside the manifest was edited.**
  - **Consumer docs/Postman.** NEW `docs/admin/site-settings.md` and
    `docs/app/public/site-settings.md`; the admin Postman collection gained
    the new "22. Site settings (manual authoring)" entries and the app Public
    collection the new "07. Site settings" entry (the T8/T9/T10 entries are
    retained). Both collections re-parse as valid JSON and the new admin
    request body parses.
  - **Gate evidence (this turn, final code).**
    `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded,
    0 Warning(s), 0 Error(s)** (exit 0);
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~SiteSettingsAdminTests|FullyQualifiedName~SiteSettingsReadTests|FullyQualifiedName~SiteSettingsControllerTests"`
    → **Passed! Failed: 0, Passed: 16, Skipped: 0, Total: 16** (exit 0).
    **No whole-suite run** (the earlier deviation must not recur), no EF
    command beyond the one generation run, no database access.
  - **Disclosed limitations.** (1) The evidence is **InMemory-provider unit
    evidence only**: the migration was never applied and the relational
    behaviour (the check constraint, the duplicate-key race) was **not**
    exercised against a real database, so the `409` conflict path is covered
    at the controller-mapping level and by the handler's duplicate-key
    mapping, not by a real concurrent insert. (2) There is **no runtime HTTP
    evidence** and no deployed surface.
  - **Next action:** independent review of T11. **T9-A stays accepted and
    T9-B stays deferred/blocked** pending WEB-08; the overall plan is **not**
    complete and no release is authorized.
- **T11 review of record (reviewer `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`):
  REJECT.** The reviewer independently reproduced the gate: **build
  0 warnings / 0 errors** and the focused filter **16/16**
  (`SiteSettingsAdminTests|SiteSettingsReadTests|SiteSettingsControllerTests`).
  **One Medium blocker** plus Low residuals:
  - **Medium — a null navbar element ends in an unhandled 500.**
    `src/API/Sanad.API/Controllers/AdminSiteSettingsController.cs:41-47`: the
    request's `navbarLinks` array may carry a **null element** (the
    System.Text.Json Web defaults materialize `{"navbarLinks":[null]}` into a
    non-null list with a null element), and the `?.` guard protects only the
    list, so `link.Key` dereferences null and throws
    `NullReferenceException` → an unhandled **500** instead of a documented
    400. The house precedent for a null element in a JSON array is
    `src/API/Sanad.API/Controllers/Requests/LibraryContentValidation.cs:36-57`
    (a fixed 400 instead of a crash).
  - **Low residuals.** (1) `SiteSettings.Update`
    (`src/Modules/Cms/Domain/Sanad.Modules.Cms.Domain/Settings/SiteSettings.cs:111-128`)
    assigns `NavbarLinksJson` and then the SEO fields **sequentially**, so a
    later failure can leave earlier fields already mutated on the tracked
    entity. (2) there was **no test that an invalid update against an already
    populated row leaves that row unchanged**. (3) the **validator/pipeline
    path is untested** — no HTTP/pipeline evidence for this surface (the
    reviewer's own item 4, `WebApplicationFactory`/pipeline tests —
    **deferred, not implemented** in this correction turn). (4) the relational
    invariants (the single-row check constraint, the duplicate-key race) stay
    **InMemory-provider-only** evidence.
  - **Reviewer evidence method (read-only).** The null-element materialization
    was reproduced by the reviewer with a **read-only `dotnet fsi` probe** that
    deserialized the documented request body with the Web defaults and showed
    the bound list is non-null with a null element — a probe only, with **no
    repository write, no product build, no test-suite change and no runtime
    host**; the rest of the review was a read-only diff read of the delivered
    files.
- **T11 correction turn (Worker A slot, bounded correction only):
  `ready_for_review` pending focused verification.** Scope: the admin
  site-settings controller, the `SiteSettings` aggregate, the two site-settings
  test files and this brief. **No** change to URL rules, limits, error codes,
  statuses, the public contract, the migration, DI, docs or Postman.
  - **Medium fixed — the chosen shape is the *fixed 400* flavour of the house
    precedent (a controller-level refusal), not the defensive projection
    alone.** `AdminSiteSettingsController.Update` now refuses a request whose
    `navbarLinks` array carries a null element **before** the command is
    dispatched: it answers **400 `Cms.SiteSettings.Invalid`** — the already
    documented 400 of this surface, with the **same problem body as every other
    `Cms.SiteSettings.Invalid` answer** (no new error code, no new response
    shape, no new field) — and **the write is never dispatched**, so the stored
    row cannot change. The projection itself was also made null-safe (a null
    element maps to an empty link) so that no request shape can end in an
    unhandled failure. **Why this shape:** (a) it yields the documented
    `Cms.SiteSettings.Invalid` 400 **deterministically at the unit level**,
    without the deferred pipeline work; (b) the controller-level
    "request check → 400 with the feature code" is the existing house pattern
    (`AdminLibraryController` + `LibraryContentValidation`); (c) the
    projection-only alternative would have been refused by the
    **FluentValidation shape rule** first (`Api.Validation.Failed`), so the
    observable code would have depended on the untested pipeline; (d) the
    **indexed `errors` body** of the library precedent was deliberately **not**
    copied — it would have introduced a new response shape and a
    docs/Postman surface change that this turn forbids.
  - **Low 2 fixed — the aggregate update is now atomic.** `SiteSettings.Update`
    normalizes/validates **all** inputs into locals first and only then
    assigns `NavbarLinksJson`, the four SEO fields and `UpdatedOnUtc`; a later
    failure can no longer leave an earlier field mutated on the tracked entity.
    **No behaviour change otherwise** (same normalization, same trimming, same
    limits, same timestamps).
  - **Low 3 + Low 1 closed with two new tests.**
    `SiteSettingsAdminTests.An_invalid_update_leaves_the_authored_row_unchanged`
    authors a **populated** row (one link + the bilingual SEO block), issues two
    **invalid** updates — an unusable target (`javascript:alert(1)`) and an
    update whose links are valid but whose SEO title is out of bounds (exactly
    the case the old sequential update mutated) — asserts the documented
    `Cms.SiteSettings.Invalid` refusal for each, and then re-reads the row and
    proves it is **unchanged**: the stored links JSON, the four SEO values and
    `UpdatedOnUtc` are identical, and the admin read still returns the original
    document.
    `SiteSettingsControllerTests.Admin_update_refuses_a_null_navbar_element_with_the_documented_400`
    sends a body whose `navbarLinks` array contains a **null element** and
    asserts the **400 `Cms.SiteSettings.Invalid`** answer with the standard
    problem body, and that **no command was dispatched at all**
    (`sender.LastRequest` is null) — so nothing could mutate the stored row.
  - **Reported, not edited.** (1) The end-to-end **HTTP/pipeline** behaviour of
    this surface (the `ValidationBehavior` + `ValidationExceptionHandler` path
    and the real JSON body binding) is still **untested** — the reviewer's item
    4 is **deferred** and was **not** implemented in this turn. (2) The
    relational invariants (the single-row check constraint, the duplicate-key
    race → `409`) stay **InMemory-provider-only** evidence; the migration was
    still never applied. (3) **No doc/Postman sentence became factually wrong**
    by this correction: `docs/admin/site-settings.md` already documents the
    **400 `Cms.SiteSettings.Invalid`** ("when the content is unusable — nothing
    is stored and the current row is unchanged") for this surface and the
    answer body shape is unchanged, so **no doc or Postman edit was needed or
    made**.
  - **Gate for this correction (single strict build + single focused filter, no
    whole-suite run, no EF command, no database, no HTTP, no secrets/env
    reads, no git write, no deploy):**
    `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded,
    0 Warning(s), 0 Error(s)** (exit 0);
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~SiteSettingsAdminTests|FullyQualifiedName~SiteSettingsReadTests|FullyQualifiedName~SiteSettingsControllerTests"`
    → **Passed! Failed: 0, Passed: 18, Skipped: 0, Total: 18** (exit 0; the
    focused filter now runs **18** tests — the 16 of the reviewed delivery plus
    the two added cases: the invalid-update/no-mutation case and the
    null-navbar-element 400 case).
    The correction turn is **`ready_for_review` pending focused verification**;
    it does not mark T11 `completed` — the acceptance verdict follows below.
  - **State kept.** **T9-A stays accepted and T9-B stays deferred/blocked**
    pending WEB-08; the overall plan is **not complete** and **no release is
    authorized** (release state unchanged: not authorized).
- **T11 acceptance verdict (reviewer `ses_ea0d1a7f5ffel8R7a4GwCYdX4c`,
  2026-10-22, brief scribe): ACCEPTED after the original REJECT.** The
  reviewer verified the correction and approved the standardized-400-body
  choice as compliant.
  - **Medium closed — a null navbar element no longer reaches a 500.** The
    admin controller now refuses a request whose `navbarLinks` array carries a
    null element **before the command is dispatched**, with the surface's
    already documented **400 `Cms.SiteSettings.Invalid`** and the **same
    problem body** as every other `Cms.SiteSettings.Invalid` answer — **no new
    error code, no new status, no new field**. The **indexed `errors` body of
    the library precedent was deliberately not copied** (it would have added a
    new response surface); the house precedent used is
    `LibraryContentValidation` / `AdminLibraryController`. The projection is
    null-safe as well, and the request element is annotated
    `SiteSettingsNavbarLinkRequest?` with the **wire unchanged**.
  - **Low closed — the aggregate update is atomic.** `SiteSettings.Update`
    normalizes/validates every input into locals **first** and only then
    assigns the fields, so a later failure can no longer leave an earlier
    field mutated on the tracked entity.
  - **Two discriminating tests added.** An invalid update against an already
    populated row leaves that row **unchanged** (including the previously
    mutating oversized-SEO case), and the null-element request is refused with
    **400 and no command dispatched**.
  - **Edge replays verified.** `[null]`, `[valid, null]` and `[null, null]`
    → **400, no mutation**; `[]` → **200 `[]`**; **omitted** → `[]`; `[{}]` →
    **400** via the handler's `Required` rule.
  - **Final gates on the final code.**
    `dotnet build Sanad.slnx -c Release --no-incremental` → **Build succeeded,
    0 Warning(s), 0 Error(s)** (exit 0);
    `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~SiteSettingsAdminTests|FullyQualifiedName~SiteSettingsReadTests|FullyQualifiedName~SiteSettingsControllerTests"`
    → **Passed! Failed: 0, Passed: 18, Skipped: 0, Total: 18** (exit 0; the 16
    of the reviewed delivery plus the 2 discriminating cases).
  - **Residuals recorded explicitly — deferred or out of scope, not
    regressions.** (1) The reviewer's **item 4** (the `ValidationBehavior` /
    pipeline / real JSON-binding tests) stays **deferred**: the controller
    tests stub `ISender`, so the pipeline path of this surface is still
    unproven. (2) The **relational invariants** (the single-row check
    constraint, the duplicate-key → `409` race) remain **InMemory-only**
    evidence, and the migration `20261022060548_AddSiteSettings` was **never
    applied**. (3) There is **no runtime/HTTP evidence** for this surface.
    (4) A **literal whole-body `null`** relies on MVC's implicit-required
    behaviour — that is **reasoned, not executed**. (5) Pre-existing and out
    of scope: `20261021120000_AddFamilyHomeSections.Designer.cs` is an
    **empty-stub designer**; it was not introduced by T11.
  - **Consequence.** T11 is **`completed`** on **unit-level evidence only** —
    no HTTP run, no database, no migration apply, no deployment, no commit and
    no push are claimed.

## Wave plan (≤2 bounded whole-phase workers per wave, exact-file dispatch)

| Wave | Worker A | Worker B |
|---|---|---|
| 1 | T1 Payment | T3 Audience |
| 2 | T2 Resident eligibility | T4 Admin + F1 |
| 3 | T5 Mobile reads | T6 Docs + E07/E08 text |
| 4 | T7 Community block | T8 Landing shape + SVG |
| 5 | T9 Partner contact + public legal (solo) | — |
| 6 | T10 Subscription copy (solo) | — |
| 7 | T11 Navbar/SEO site settings (solo) | — |

**Collision fix (explicit — the earlier draft was NOT collision-free).** An earlier
wave draft ran **T9 + T10 together in one wave**; they both edit
`docs/postman/admins/Sanad.Admin.postman_collection.json`, a real collision. The
locked schedule above resolves it by making **T9 and T10 solo waves (5 and 6)**.
No claim is made that the earlier T9+T10 wave was collision-free.

**Serial documentation handback:** after each wave the phase worker hands back;
**one assigned worker applies a single serial update** to the shared status brief.
No two workers write the same status/doc file concurrently, product/consumer docs
stay the phase worker's own responsibility, and the parent never edits.

**Exact-file intersection (computed by parsing every manifest in this brief, not
handpicked).** The set below is the complete list of files appearing in ≥2 task
manifests; each is assigned a single owning wave so no two concurrent workers
share a file (T9 and T10 are solo because of the Admin-Postman row):

| Shared file | Tasks | Resolution (locked schedule) |
|---|---|---|
| `docs/app/families/subscriptions.md` | T1, T10 | Waves 1 → 6 — serialized |
| `docs/postman/app/Sanad.App.Family.postman_collection.json` | T1, T5, T10 | Waves 1 → 3 → 6 — serialized |
| `docs/postman/app/Sanad.App.Public.postman_collection.json` | T1, T8, T9, T11 | Waves 1 → 4 → 5 → 7 — serialized |
| `docs/postman/admins/Sanad.Admin.postman_collection.json` | T4, T8, T9, T10, T11 | Waves 2 → 4 → 5 → 6 → 7 — serialized (T9 solo wave 5, T10 solo wave 6) |
| `docs/app/settings/legal.md` | T3, T9 | Waves 1 → 5 — serialized |
| `docs/admin/support-inbox.md` | T4, T9 | Waves 2 → 5 — serialized |
| `docs/app/public/landing.md` | T8, T9 | Waves 4 → 5 — serialized |
| `src/API/Sanad.API/Controllers/LegalController.cs` | T3 (edits), T9 (references read-only) | Waves 1 → 5 — no write conflict; T9 adds a **NEW** `PublicLegalController.cs` and does not edit this file |
| `docs/app/community/README.md`, `docs/postman/app/Sanad.App.Community.postman_collection.json`, `tests/Sanad.UnitTests/Community/CommunityFeedTests.cs`, `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostsQuery.cs`, `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetPostQuery.cs`, `src/Modules/Community/Application/Sanad.Modules.Community.Application/Posts/GetCommunityRecommendationsQuery.cs`, `src/Modules/Community/Infrastructure/Sanad.Modules.Community.Infrastructure/Persistence/CommunityDbContext.cs` | T5, T7 | Same Worker A, Waves 3 → 4 — T5 first |

Single-owner files (no concurrency risk, listed for completeness):
`docs/postman/app/Sanad.App.Caregiver.postman_collection.json` (T5),
`docs/postman/admins/Sanad.Admin.CareHomes.postman_collection.json` (T4),
`docs/app/caregivers/bookings.md` (T5), `docs/care-homes/visits.md` (T2),
`src/API/Sanad.API/DependencyInjection.cs` (T1),
`src/BuildingBlocks/Infrastructure/Sanad.BuildingBlocks.Infrastructure/Storage/LocalDiskFileStorage.cs` (T8),
`src/API/Sanad.API/ApplicationBuilderExtensions.cs` (T8),
`src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/DependencyInjection.cs` (T11),
`src/Modules/Cms/Infrastructure/Sanad.Modules.Cms.Infrastructure/Persistence/CmsDbContext.cs` (T11),
`src/Modules/Cms/Application/Sanad.Modules.Cms.Application/Abstractions/Data/ICmsDbContext.cs` (T11),
`src/Modules/Identity/Infrastructure/Sanad.Modules.Identity.Infrastructure/Persistence/IdentityDbContext.cs` (T9),
`src/Modules/Families/Domain/Sanad.Modules.Families.Domain/Subscriptions/SubscriptionPlanVersion.cs` (T10).

No two concurrent workers share a file. Runtime, build, migration and release
stay serialized across both workers; **task progress is reported only after
dispatch.**

## Owner decisions (locked 2026-10-21)

1. **O-1 Multi-account audience omission (T3): FINAL.** Omitted `audience` with
   **multiple eligible** audiences returns coded `400 AudienceRequired`; with
   **exactly one** eligible audience the audience is selected automatically.
2. **O-2 Supplier count (T9/T11): FINAL — defer, no fake `0`.** No supplier domain
   exists; the frontend must **omit** the supplier count rather than display `0`.
3. **O-3 App-rating + `/choose-type`: FINAL — defer backend work pending
   requirements.** Both are deferred and **do not block** the other tasks; the
   consumer handoff reports request the missing requirements as structured
   checklists.
4. *(Optional, non-blocking)* partner **editorial** testimonials only if the Owner
   explicitly wants them and supplies consent/source/role; the genuine-ratings/
   no-invented-role rule stands.

**Supplied intake (recorded).** Owner-supplied `BACKEND_ENDPOINTS_REQUEST.md`
(frontend + mobile), dated 2026-10-20, was reconciled read-only against current
controllers, handlers, domain rules, consumer docs and earlier verification
records. Owner directions given 2026-10-21: existing VPS is **Production**; no
domain/HTTPS/staging; validation on the persistent local `SanadDb`. Live failures
were **not** reproduced in this pass.

Everything else is settled from existing code and must not be reopened (E05, chat
scope, E06, growth, admin roles, community posts-only block, bilingual nested CMS,
SVG).

## F1 / F2 / D1 carry-forward

- **F1** (T4): add the missing `404 [ProducesResponseType]` to
  `src/API/Sanad.API/Controllers/AdminSupportTicketsController.cs`.
- **F2** (T6): the E07 guard (`tools/Invoke-E07LocalHttpVerification.ps1`) must
  check the **total feature-table footprint + planned writes `<= 10` before any
  fixture write**; the existing **12** retained rows mean **fail closed — no
  execution, no delete, no cap relaxation**. Offline pure guard-boundary check may
  be proposed (no DB).
- **D1** (T6): correct only the misleading E08 limitation text at
  `tools/Invoke-E08LocalHttpVerification.ps1`.

## Deferred pending requirements (do not block the rest)

- **`/choose-type`** — no route or spec exists; backend work deferred until the
  frontend supplies the required product details (see report `WEB` checklist). Does
  not block T1–T11.
- **Supplier count (O-2)** — deferred; the frontend omits the value (no fake `0`).
- **App-rating (O-3)** — deferred until the frontend supplies the feature
  requirements (see report `MOB` checklist).
- **Partner editorial testimonials** — optional; only if the Owner requests them
  and supplies consent/source/role.

(Items 7 and 9 are **not** deferred: 7 is a sourced caregiver-detail projection in
T5; 9 is T2's eligible-residents read.)

## Serialized runtime, L1 cap, and release

- Runtime verification is serialized behind the local L1 contract: **no reset;
  ≤10 synthetic rows per feature table preserved**; role/audit/migration rows may
  exceed ten. F2 adds a guard, it does not consume rows.
- Release (commit/push/deploy/migration apply) is a **separate** authorized step
  with its own receipt. Contract approval here is **not** release approval. There
  is **no** domain/HTTPS/staging step.

## Status and handback

- **Consolidated plan status (2026-10-22, brief scribe — the current
  checkpoint; it replaces the planning-turn status recorded below).**
  - **T1–T8: `completed`** — the phase's implementation tasks are done. The
    per-task wording is kept as recorded: T1/T3/T4/T6 and T2 are `accepted` in
    their own sections (T2 per its Wave 2 closeout), T5/T7/T8 are `completed`.
  - **T9-A (partner contact): accepted** — the accepted F1 fix is verified at
    **64/64**.
  - **T9-B (public legal): deferred** pending the **WEB-08** Product/Content
    answer. `LegalAudience` has **no public member**, and the frontend's
    WEB-08 reserves the audience choice, so **no audience is guessed**. **T9 as
    a whole stays partial/deferred.**
  - **T10: `completed`** — final independent focused verification **48/48** on
    a valid strict build with **0 warnings / 0 errors**.
  - **T11: `completed`** — final focused verification **18/18** on a valid
    strict build with **0 warnings / 0 errors**.
  - **The plan is NOT complete** (T9-B remains deferred) and **release is NOT
    authorized** (release state unchanged: not authorized). **All the work is
    uncommitted** — nothing was committed, pushed or deployed in any of these
    tasks.
  - **File-only migrations awaiting release (none of them applied):** Community
    `20261021192605_AddCommunityAuthorBlocks`, Identity
    `20261021205122_AddPartnerContacts`, Families
    `20261022044106_AddSubscriptionPlanVersionCopy`, Cms
    `20261022060548_AddSiteSettings`. Applying them is a **separate authorized
    release step** with its own receipt, and **a pre-cutover backup is
    required at deploy per house practice**.
- *(Planning-turn record, superseded by the block above: scope approved, every
  task was `pending`, release `not_requested`, nothing implemented or
  deployed in that turn, and no handoff edit was made by it.)*
- Consumer handoff reports (ready to forward):
  `docs/internal/work/portal-api-reconciliation/reports/frontend-team-follow-up-2026-10-21.md`
  and
  `docs/internal/work/portal-api-reconciliation/reports/mobile-team-follow-up-2026-10-21.md`.
- Active brief path:
  `docs/internal/work/portal-api-reconciliation/worker-prompts/frontend-follow-up-2026-10-21.md`.
- Files changed by that planning turn: **three** — this brief and the two reports.

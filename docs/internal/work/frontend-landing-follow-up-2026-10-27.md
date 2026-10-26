# Frontend landing follow-up — 2026-10-27 (ACTIVE BRIEF)

Phase state: `approved` (preparation complete; implementation NOT started — start
only on parent/Mastermind direction). Release state: `authorized` per the
approved plan — commit, push, publish and deploy to the **same Staging rehearsal
(72.62.92.144)** only after all green gates below.

## Approval and scope

- Owner approved the follow-up scope in conversation on 2026-10-27.
- Scope authority: the approved plan/response draft (approved working copy at
  `C:\Users\mosad\.opencode\plan\frontend-landing-response-draft-2026-10-27.md`);
  sections 1.1–1.4 are the backend tasks here. Frontend/mobile actions (§2–3)
  and deferred infrastructure (§4) are out of backend scope.
- Deliverable to the frontend team after deployment: an accessible updated
  money-action matrix and the English response based on that draft (§5 delivery
  record). Do not copy private host credential details or stale deployed-SHA
  preconditions into it.

## Base pin

- Worktree HEAD: `812493d92e247cda5614dbcd82b7bc746278ca54` (short `812493d9`).
- Deployed rehearsal revision (last independently observed): `47329bf5e9bc22a0ba8d01a8b7d145830dbb924e`
  (receipt/docs commit `812493d9`) from the verified mobile-integration release —
  NOT `e664592e`; a stale private-handoff line must not override the current
  verified release. Verify `GET /api/v1/deployment/info` read-only only if
  needed and record what is actually observed.
- Preserved local delta (unstaged; never stage): private handoff edit
  `docs/internal/Mastermind_Handoff.md` (+20 private release record), untracked
  `docs/internal/Mastermind_Handoff_Historical_2026-10-23.md`, and debris
  (`.opencode-audit-tmp/`, `workflow-audit/`, `buildout*.txt`, `testout*.txt`,
  `testout*_exit.txt`, `migout.txt`, `e07-library-smoke.ps1`).
- Migrations expected: none. If a task proves one is required, stop for Owner
  review first.
- Read first: `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
  private `docs/internal/Mastermind_Handoff.md` (top = current next action),
  approved draft above. Inspect `git status`/`diff` before editing.

## Actual state vs target (source-traced at `812493d9`)

| Route | Current policy | Current roles | Target roles | Change |
|---|---|---|---|---|
| `GET /api/v1/admin/caregivers/lookup` (`AdminCaregiverLookupController.cs:20`) | `CaregiverReviewAdmin` (`DependencyInjection.cs:406-420`) | SuperAdmin + SupportAdmin | SuperAdmin + FinanceAdmin | **policy change**: remove SupportAdmin, add FinanceAdmin |
| `GET /api/v1/admin/elderly/lookup` (`AdminElderlyLookupController.cs:19`) | `OperationalReadAdmin` (`DependencyInjection.cs:440-446`) | SuperAdmin + SupportAdmin | SuperAdmin + SupportAdmin | **none** — verify only |
| ContentAdmin on either lookup | not in either policy | 403 | not allowed | **none** — verify only |

**Critical implementation constraint:** `CaregiverReviewAdmin` is shared with
`AdminCaregiversController.cs` and `AdminCaregiverRatingsController.cs`, and
`OperationalReadAdmin` is shared with families/care-records/subscriptions/
feedback/feedback-ratings controllers. Never edit the shared policies; the
caregiver change must be a new lookup-scoped policy (or equivalent
route-scoped requirement) touching only the lookup route. Prior SupportAdmin
access on the caregiver lookup is removed explicitly and stated in docs.

## Tasks

This brief is edited only by the GLM prep/Task-A worker. Other workers return
handbacks to Mastermind/parent; their results are recorded here by the brief
owner. Workers never edit this brief.

| Task | Owner | Scope (exact) | Acceptance | Status |
|---|---|---|---|---|
| A1 lookup role matrix | River GLM 5.3 Flash `#high` (this session) | New lookup-scoped SuperAdmin+FinanceAdmin policy for the caregiver lookup (SupportAdmin explicitly removed); wire only `AdminCaregiverLookupController`; elderly/Content behavior verified unchanged; focused policy/role tests (extend `tests/Sanad.UnitTests/API/AdminNameIdLookupContractTests.cs` and the house authorization-test pattern); docs `docs/admin/caregivers-review.md` (incl. verifying the line-3 phrase "ContentAdmin … plus caregiver lookups" — correct if drifted), `docs/admin/families.md` (elderly section), `docs/admin/admin-overview.md`; minimal API-contract corrections and contact idempotency retry-doc wording (`docs/app/public/partner-contact.md`, `docs/app/public/landing.md` if drift proven) | Strict build 0 warnings/0 errors; lookup role tests green and state the exact matrix; shared-review-surface access unchanged (existing tests green); docs match verified behavior | `ready_for_review` (authored; gates deferred until BOTH source workers frozen — see A delivery) |
| A2 doc typo | River GLM 5.3 Flash `#high` (this session) | `docs/app/public/site-settings.md:12` `**Success:** `200**` typo → `200`; verify `/partner-contact` (public landing form) vs `/support/contact` (separate authenticated surface) wording | Docs-only; matches source routes | `ready_for_review` (delivered with A1 — see A delivery) |
| B1 notification language | River DeepSeek V4.1 Flash `#high` (separate worker) | Verify current notification producers: readable text, no raw routing IDs in display copy, language from recipient's saved account language; bounded fixes/tests/docs only where verification proves a current defect. NO historical inbox-row rewrites; NO live events by default (inspect side effects — email/push/booking state — before any event; isolated tests preferred) | Evidence distinguishes source/authored tests vs real runtime; no historical rows touched; defects fixed only with demonstrated proof | `completed by worker handback; accepted` — verification found NO source defect: current producers resolve the recipient's saved account language with a defined fallback and keep ids off the human copy; two new pins (`ReadableNotificationTests` onboarding producer, `CareHomeBookingNotificationTests` English-owner decision) + existing chat pins green in the focused gate; no historical rows touched, no live events fired |
| B2 money matrix + Paymob test prep | River DeepSeek V4.1 Flash `#high` (separate worker) | Prepare an accessible updated money-action matrix (based on `docs/internal/work/frontend-report-2026-10-26.md` §B, current-state verified; no private credentials, no stale DEPLOYED-SHA preconditions) + English response built from the approved draft; inspect/prepare minimal actual-Paymob test-refund verification: max TWO test records, reuse existing rows first, no new framework/account seeds. Runtime provider actions SERIAL only after parent review of the exact safe protocol AND source-fix freeze; verify effective test config first; permissible testing account only; secrets never printed; no broad unseamed refunds | Matrix current and accessible; provider steps either not run (prep) or run under the exact reviewed protocol with full sanitized evidence | `in_progress` (worker handed evidence; NOT executed, NOT closed). Worker first proposed executing refunds on existing captures `560118491`/`560726538` + a duplicate-charge claim/approve "duplicate verification" — **rejected in review as unverified** (a valid single sale is never a duplicate without proven duplication). Corrected inventory: `care_homes.bookings` = 26 rows, one possible REAL capture txn `556970084` on booking `01a1559c-10e3-76b1-ba13-7753b1b7de77` (11,500, checked out 2026-10-19) — **not refund-eligible** via existing APIs; other care-home money fixtures synthetic seam; the two existing real subscription captures are valid single sales, not duplicates. Therefore **no existing eligible provider-refund capture is evidenced**. Proposed next stage (under parent review, NOT authorized for execution): ONE new funded TEST care-home booking through the real flow — demo family `01a17542-1983-7ba5-9b29-30b5980ddad7`, owner `01a17542-12d2-7bd4-bd85-59d13a5bd98f`, facility `01a17542-29e5-7fdd-a55f-d2465be8ad63` (room type `01a17542-2c5c-729d-a211-0f3c238db413`), existing facility owner `01a17542-16ee-70f5-9305-7f2d174ab28c`; re-check approval/doc validity/capacity/effective TEST provider config first; use the CURRENT checkout request DTO (not the worker's partial example); flow = family checkout → payment intent → real test hosted checkout 3DS → verified callback PaidAwaitingDecision → owner reject before check-in → legitimate full refund; no fabricated captured rows, no manual paid state, no duplicate claims; count = max TWO new designated business records (test bookings; existing family/facility not counted), required normal payment/audit dependent rows explicitly accounted; no extra accounts. Hosted 3DS may need Owner interaction — no automatic-completion promise, no fabricated evidence. Execution resumes only after this release (serialized) on parent review |
| C integration/release | separate worker, after A+B review | Strict 0/0 build; focused regressions on actual current contracts; docs parse/link/secret-scan check; exact staging excluding private/debris (never stage `docs/internal/Mastermind_Handoff.md` delta, `Mastermind_Handoff_Historical_2026-10-23.md`, debris files); commit+push; publish+deploy the same Staging `72.62.92.144` preserving seam/env/unit/storage; verify 9/9 heads (0 pending — none expected), manifest, new policy tests, smoke | Release verified and recorded; receipt; phase `accepted`→`completed` only by Mastermind after verification | `pending` |

Dependencies: C after A and B review. B2 provider runtime after parent-reviewed
protocol; otherwise A and B owners are independent (keep overlapping files and
runtime serialized).

## Gates

- Every `dotnet` build/test gate: successful exit, zero warnings.
- Integration: strict Release 0/0; focused regression set for the changed
  policy/behavior; full suite only if cross-cutting risk appears.
- Docs-only subtasks: docs/script validation only, no product build.
- Never invent results; distinguish authored vs executed checks and
  source-verified vs live-runtime evidence in every report.

## Release protocol (Task C, per approved plan)

1. Green gates above + Mastermind review of the actual diff.
2. Exact-stage: changed code/tests/public docs + this brief (under
   `docs/internal/work/`); NEVER the private handoff delta, the historical
   handoff, or debris.
3. Commit, push `origin/main`, publish, deploy the same Staging rehearsal
   (72.62.92.144, unit `sanad-api`, `Staging`, DB `SanadDb`), preserving the
   FixtureRefundSeam block, `/etc/sanad/sanad.env`, unit file and unit storage.
4. Verify: live revision match, 9/9 migration heads 0 pending/0 applied
   (no migrations expected), manifest, new policy tests green, anonymous smoke
   `GET /api/v1/lookups/services` 200, config fingerprints unchanged.
5. Record release revision + verification once in this brief and update the
   private handoff current-active/next-action minimally. Send the accessible
   money-action matrix + English response to the frontend team only after
   verified deployed outcomes.

## Safety

- Never expose credentials, stage private handoffs or debris, or print secrets.
- No infrastructure changes (CORS, domains, proxy, deployment topology) — deferred.
- No new caps/content/account creation/legal authoring. No historical rewrites.
- No real charges/refunds outside the exact approved protocol; seam ≠ blanket
  sandbox safety; the rehearsal stays `Staging` with the seam intact.
- Contract approval ≠ release approval beyond the plan-scope authorization
  recorded above; report and stop at each handback.
- Model: Task A / orchestration River GLM 5.3 Flash `#high`; Task B River
  DeepSeek V4.1 Flash `#high`. No model switch; quota errors reported, not
  auto-switched.

## A delivery (Task A/A2 handback, 2026-10-27)

Authored at worktree HEAD `812493d9`; **no build, no tests run, no commit/push,
no provider/auth/DB/env action** — dotnet gates are deferred until BOTH source
workers are frozen, per parent direction.

- **Code (4 files):** `AuthorizationPolicies.cs` adds `CaregiverLookupAdmin`
  (Normal + SuperAdmin/FinanceAdmin, doc-commented); `DependencyInjection.cs`
  registers it beside — never touching — the shared `CaregiverReviewAdmin`;
  `AdminCaregiverLookupController.cs` switches its attribute to
  `CaregiverLookupAdmin` and its doc comment (SupportAdmin removal stated);
  `AdminCaregiverLookups.cs` query doc comment updated. No handler/runtime
  behavior change; elderly lookup code untouched (target already matched).
- **Tests (authored, NOT run):**
  - `AdminNameIdLookupContractTests.cs` — caregiver policy fact renamed to
    `Caregiver_lookup_uses_the_dedicated_caregiver_lookup_policy` and pinned to
    `CaregiverLookupAdmin`; new
    `Caregiver_review_surfaces_keep_the_shared_caregiver_review_policy`
    (AdminCaregiversController + AdminCaregiverRatingsController stay on
    `CaregiverReviewAdmin`); existing route/payload/contract facts unchanged.
  - NEW `AdminNameIdLookupRoleMatrixTests.cs` — real role matrix over the
    app's **registered** policies via the isolated `PortalApiMetadataHost`'s
    real `IAuthorizationPolicyProvider` + `IAuthorizationService`: caregiver
    lookup admits Normal SuperAdmin/FinanceAdmin and denies
    SupportAdmin (the explicit removal), ContentAdmin, Family and
    restricted tokens; elderly (OperationalReadAdmin) admits
    SuperAdmin/SupportAdmin and denies ContentAdmin/FinanceAdmin; shared
    review policy stays SuperAdmin/SupportAdmin.
  - NEW `AdminNameIdLookupApiAuthorizationTests.cs` — real HTTP through the
    isolated host: caregiver lookup 401 anonymous, 403 Family/ContentAdmin/
    SupportAdmin; elderly lookup 401/403 for the denied roles and 200 empty
    pages for SuperAdmin/SupportAdmin. Honest limitation recorded: the
    caregiver lookup's admitted-role 200 cannot be served on the in-memory
    host (raw PostgreSQL-specific projection), so those facts assert
    "not 401/403 — passed auth+policy and reached the handler" and admission
    itself is proven by the registered-policy matrix facts.
- **Docs/Postman:** `caregivers-review.md` line-3 drift fixed (ContentAdmin
  never had lookup access) + lookup section rewritten to the exact new
  matrix/policy; `claims-and-policies.md` `CaregiverReviewAdmin` entry
  corrected + new `CaregiverLookupAdmin` section; `site-settings.md:12`
  `200**` typo fixed; `landing.md` adds the public-form `/partner-contact` vs
  authenticated `/support/contact` clarification; `partner-contact.md` adds a
  "Retries" section documented from the production handler semantics (replay
  checked before the rate limit — same-key retry never consumes the 5/hour
  budget; same key = replay, new submission = new key; no runtime policy
  change, no new frontend architecture); `Sanad.Admin.CaregiverNotes`
  Postman lookup item description updated to the new policy. `families.md`
  and its Postman item verified already accurate (SuperAdmin/SupportAdmin;
  ContentAdmin/FinanceAdmin 403) — unchanged. Link check across the edited
  docs: no broken relative links. Envelope/pagination names
  (`items/page/pageSize/totalCount/totalPages`) as implemented, no invented
  limits (search has no max length; the docs now say so).
- **Proposed gate filters when unfrozen:** build strict Release 0/0;
  `--filter "FullyQualifiedName~AdminNameIdLookup"` (contract + matrix +
  HTTP tests) plus `--filter "FullyQualifiedName~AdminCaregiverReviewAuthorizationTests|AdminOperationalReadAuthorizationTests"` style
  shared-policy regression set; existing caregiver review test set must stay
  green unchanged.

## Release receipt (2026-10-27, Owner-authorized)

Release `cf065d4bb479669462d026e58be6a9a744d3fd3f` (18 files, +1118/−27)
pushed `origin/main` (`812493d9..cf065d4b`), staged-diff secret scan clean
(1,136 added lines, 0 findings, values never printed). Package: manifest
revision = pushed SHA, 9/9 required-migration heads (none added; startup
confirmed **9/9 contexts `0 pending / 0 applied`**), zip sha256
`c2b15c59…a97ae81f7` verified byte-identical on target before extraction.
Deployed to the rehearsal (`srv1604711`, 72.62.92.144, unit `sanad-api`,
Staging, loopback 5001, DB `SanadDb`): live `47329bf5` → `cf065d4b`; binary
backup `/root/sanad-api-backup-pre-cf065d4b` (110 top-level entries) taken
before stop; host `appsettings.json` + `appsettings.Staging.json` excluded
from replacement; service active, `NRestarts=0`. Config fingerprints
byte-identical before/after: `/etc/sanad/sanad.env` `fad7e7fc…`,
`appsettings.json` `200332b4…`, `appsettings.Staging.json` (7-id seam block
present) `fd96e26d…`, unit `c018192e…` — the seam's explicit startup line was
not re-emitted this start (recorded honestly); effective seam configuration is
proven unchanged by the byte-identical fingerprint.
`Verify-SanadDeployment.ps1` **exit 0** over public 8091 (revision match,
required/applied 9/9, smoke `GET /api/v1/lookups/services` 200). Feature
smoke: anonymous `GET /api/v1/admin/caregivers/lookup` **401 (not 404)**;
both lookup paths present in public OpenAPI. No provider call, no DB write,
no auth login, no provisioning run (provisioner sources unchanged — tool NOT
refreshed, per instruction). Team artifacts shipped in the same commit:
[frontend-landing-response-2026-10-27.md](frontend-landing-response-2026-10-27.md)
+ [frontend-money-actions-2026-10-27.md](frontend-money-actions-2026-10-27.md).
Limitations: no full-suite rerun this phase (focused contract per direction:
strict build 0/0, focused 146/146/0 skipped, architecture 1/1); caregiver-role
admission is runtime-proven by the registered-policy matrix, not by an
authenticated live call (no login performed, per constraint); notification
defect-absence is source+test-proven, no live events fired. **Refund
verification: pending, not executed, requirements NOT closed** — the
proposed one-funded-test-booking protocol awaits parent review; max two real
test refund records; serialized after this release; existing real captures
are valid single sales, not duplicates; the `556970084`/`01a1559c…` care-home
capture is not refund-eligible via existing APIs.

## Current result

Preparation complete at `812493d9` (2026-10-27); Task A/A2 delivered
authored-not-run (see A delivery above) and handed back `ready_for_review`.
Tasks B1/B2 (DeepSeek) and C pending; base correction to deployed `47329bf5`
recorded above. No business behavior changed beyond the caregiver lookup
policy; elderly and shared surfaces verified unchanged.

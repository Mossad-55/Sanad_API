# Chat Plus — remaining worker prompts and VPS shipping path

Owner direction: ignore Bruno completely. Do not author/run Bruno, read its
collections, or treat Bruno coverage as a blocker. Preserve historical evidence
as history; this work uses automated tests, Postman mapping, an optional browser
harness, and developer/device reports.

Owner confirmation: keep AllowAnyOrigin, AllowAnyMethod and AllowAnyHeader
unchanged for now. This is intentional configuration, not an open gap. No worker
may add API/proxy origin restrictions without a new owner instruction.

Planning baseline: current main includes VPS-readiness integration and Prompt 1
session-security integration; see the current internal handoff for its SHA and
verification. Existing deploy/publish-out manifest is 41f9c91: stale and not a
Chat Plus release artifact. No VPS inspection, package build, provider send,
database write, migration application, or deployment occurred here.

## Shipping decision

The integrated code is build/unit verified, not verified gap-free for deployment.
For an isolated development VPS, use Staging, a separately named test database,
one API instance, TLS, protected tester access, persistent keys/storage and
provider-safe configuration. Never set public VPS environment to Development.
Staging startup applies all pending module migrations including Finance, so
host/database/backup authorization is necessary even for a test deployment.

Prompts 5 deployment-preparation, 1 session/push security, and 2 storage cleanup
work are integrated. Prompt 3's Care Homes processor translation correction is
also integrated. Integrate each delivered branch before the next worker consumes
its changes. Prompt 4 is optional: a
browser testing page must not delay a safe staging deployment. Prompt 6 hardens
remaining pre-production processor predicates; Prompt 7 follows authorized VPS
deployment. Physical-device push proof is mandatory for the
production push claim, not for testing REST chat on an isolated staging VPS.

The mastermind owns planning/review and any separately authorized VPS deployment.
Workers do not make VPS changes. Follow the root governance's current main-branch
instruction for worker tasks. Do not start overlapping changes across shared
auth/API/docs.

## Prompt 1 — session authorization and final-send push privacy (completed)

This prompt is complete and integrated. Do not rerun it. The selected rollout
is Chat-first: on the first Chat 401 from a legacy unbound token, mobile clients
refresh once and retry once; if that fails, require sign-in without looping.
See `reports/session-security-integration.md`. Prompt 3 is integrated; Prompt 4
is optional and may be skipped.

```text
Work in D:\Sanad_API. Read AGENTS.md, docs/internal/governance/AGENTS.md,
docs/internal/Mastermind_Handoff.md, and docs/internal/work/chat-plus/
{plan.md,contracts.md,tasks.md,handoff.md,reports/integration.md,worker-prompts.md}.
Ignore Bruno entirely. The mastermind owns planning, integration and main push.
You are not alone; preserve other contributors' changes.

Create worker/chat-plus-session-security from current main after inspecting
status/base revision. Never overwrite an existing branch or work on main.

Own the bounded Identity token/session integration, Chat hub/connection audience
authorization, and Notifications push eligibility paths needed for these fixes.
Pin the exact manifest first. Existing JWTs have no session ID; do not invent
logout guarantees by checking only expiry, current user status or any live session.

Implement verified session-bound Normal access tokens issued after session
creation, bound refresh/rotation, and shared session ownership/revocation checks.
Inspect every supported login/refresh/issuance path. Preserve restricted-token
verification behavior, response shapes, multi-device isolation and expired-token
handling. Define legacy token rollout explicitly; do not silently break all
existing accounts or permanently accept unbound tokens for sensitive Chat access.

Chat connection metadata must carry the verified session, not a client-selected
session header. Revalidate join, hints and every message/receipt/presence dispatch.
Logout-current and revoke-one affect only that device; logout-all, password-reset,
reuse detection and account disable affect the appropriate sessions. Evict joins
and hints, stop dispatch to revoked sockets, and close affected sockets or prove
the documented bounded invalidation interval. Keep expiry closure enabled.

Immediately before each queued chat push provider send, recheck current recipient
session/device ownership, account status, chat visibility, block policy and
chatMessages preference. Preserve established directional message/block rules;
do not invent a different recipient policy. Suppress ineligible work durably.
Do not send provider notifications during implementation tests.

Test two devices, current/all logout, revoke-one, refresh, reset/reuse, expired
tokens, disabled accounts, queued push followed by block/hide/preferences/logout,
and crash/retry paths. Use fake providers and meaningful relational tests where
authorized. Update affected auth/chat guides and Postman examples. Author any
needed migrations; do not apply without exact-target authorization.

Run zero-warning build and focused tests. Report branch/base/final SHA, manifest,
compatibility policy, invalidation timing, actual tests and limitations in
reports/remaining-security.md. Commit on your branch. Do not merge/push main,
deploy, alter live credentials or widen unrelated auth requirements. Bring a
concrete unresolved product/compatibility decision to the planning mastermind.
```

## Prompt 2 — race-safe orphan cleanup and storage bounds (completed)

This prompt is complete and integrated. Do not rerun it. The code includes a
mastermind correction for production DI, validated sweep limits and path safety.
The authored migration has not been applied. Staging filesystem/soak verification
remains a Prompt 7/operator task. See `reports/storage-integration.md` and the
worker report at `../../../../reports/remaining-storage.md`. Prompt 3 is integrated.

```text
Work in D:\Sanad_API; read repository governance and the current Chat Plus plan,
contracts, tasks, handoff, integration report and worker-prompts.md. Ignore Bruno.
The mastermind owns planning/integration/main push. Preserve others' edits.
Create worker/chat-plus-storage from current integrated main; pin base/manifest.

Own only Chat unbound attachment lifecycle, private-storage cleanup abstraction,
bounded cleanup service, concurrency tests and its operating guide.

Current lazy expiry excludes old unbound uploads from logical quota but leaves
disk occupied. Implement the documented 24-hour unbound expiry, hourly bounded
sweep (100 objects/pass), retry/recovery and safe filesystem/database coordination.
A file bound to a message can never be deleted by a racing cleanup transaction.
Use atomic claim/bind exclusion or another proven strategy; avoid a check-then-
delete race. Bound uploads/storage per user while deletion is pending or failing;
do not permit unlimited physical growth through successive logical expiry cycles.

Delete only exact proven expired-unbound objects below the configured private
attachment root. Never delete bound files, messages, reports, general uploads,
directories or historical evidence. Missing blobs/deletion failure/crash between
steps must be recoverable. No ad hoc database edits or broad recursive deletion.
Pin clock and effective configurable limits; expose sanitized backlog/bytes metrics.

Test bind-vs-cleanup races on an authorized relational test target, boundary
expiry, restart, missing blob, failed deletion and repeated upload quota behavior.
Use temporary storage roots, not existing user files. Author migrations if needed;
no application, reset or runtime fixture writes without exact-target authorization.

Run zero-warning build/focused tests; update operations/contracts to actual behavior
and report branch/base/final SHA, manifest, executed checks, recovery guarantees
and remaining limitations in reports/remaining-storage.md. Commit your branch;
no main merge/push or deployment.
```

## Prompt 3 — bounded Care Homes processor correction (completed)

Delivered on `worker/care-home-review-processor-fix` and integrated into main at
`a869bb0`; see [integration evidence](reports/processor-integration.md) and
[worker findings](reports/remaining-care-home-processor.md). The worker brief
below is retained as the historical scope record; do not rerun it.

```text
Work in D:\Sanad_API. Read AGENTS.md/governance, current handoff and
docs/internal/work/chat-plus/reports/phase-7-release.md section 5.
Ignore Bruno. Preserve other work; mastermind owns planning/integration/main push.
Create worker/care-home-review-processor-fix from current main; pin manifest.

Investigate the reported recurring CareHomeReviewNotificationProcessor PostgreSQL
translation failure involving strongly typed u.Id.Value comparisons. Confirm the
actual query/path first; do not assume every .Value access is defective.
Own this processor and directly relevant regression tests/documentation only.

Fix provider-safe ID comparisons/projections without changing recipient roles,
notification content, review state or idempotency. Preserve bounded processing,
retry behavior and privacy. Add a PostgreSQL translation regression where
authorized; do not present an InMemory-only test as PostgreSQL proof.

Run zero-warning build/focused tests. Do not start API, apply migrations, create
fixtures or send messages/providers against a live target without authorization.
Report branch/base/final SHA, actual root cause, checks and runtime limitations
in reports/remaining-care-home-processor.md. Commit on the task branch only;
no main merge/push, general Care Homes expansion or deployment.
```

## Prompt 4 — optional browser testing page and safe fixture workflow

```text
Work in D:\Sanad_API; read repository governance and current Chat Plus app guides,
contracts, tasks, handoff and worker-prompts.md. Ignore Bruno completely.
Preserve others' edits; mastermind owns integration/main push.
Create worker/chat-plus-test-page from current integrated main.

Build a small local or tester-protected browser page using the actual REST API
and official SignalR client. Own only a clearly separated testing-page folder,
its runner, verification checks and developer instructions; no public admin/backdoor
endpoints or unrelated product frontend. Prefer plain HTML/JS or existing repo
tooling; no separate hosted SaaS project is needed.

Use two independently isolated actor sessions (separate browser profiles or an
explicit test harness) and supported authentication endpoints. Keep tokens in
memory only; no localStorage, URL credentials, exported secrets or logged payloads.
Configurable allowlisted test base URL; default local, conspicuous test-only label.

Controls: open/reopen, send Text/File/Location, upload/download, history, monotonic
read, unread, hide, block/unblock, report/paged moderation, sync cursors,
privacy/presence, typing, socket disconnect/reconnect and REST-only recovery.
Render untrusted names/text with safe text APIs, not innerHTML. Use one coherent
sync/reconciliation implementation; deduplicate events and persist only nonsecret
test metadata. Respect rate limits/backoff rather than blasting the endpoint.

Add pricing filter/response inspection and a readiness readback for fixtures.
Test-data creation uses supported APIs or an explicitly authorized disposable
fixture path. Do not fake Paid/Completed via raw SQL or add product bypasses.
For five Hourly + five EightHour attributable completed/paid samples, use an
authorized loopback payment fixture/provider stub; stop that dependent scenario
if the supported payment path is unavailable. Other tests can still proceed.

Demonstrate >100 missed messages plus receipt-only changes, initial transport
failure, active socket kill, complete pagination, restart, cursor handling,
logout/revocation after Prompt 1, and browser-origin behavior. Fixtures, reset,
provider calls and API startup require a named target and authorization.

If a simple page cannot be completed safely within this scope, produce a precise
manual developer test pack instead; do not block staging shipping over the page.
Record expected/actual status, scenario, sanitized correlation ID/time, failing
request shape and logs needed for diagnosis, never tokens/messages/user secrets.
Provide a report template for developers to return reproducible defects.

Commit your branch and report base/final SHA, start instructions, implemented
controls, checks actually run, target/fixture effects and gaps in
reports/remaining-test-page.md. No main merge/push or production deployment.
```

## Prompt 5 — priority VPS preparation and release tooling

```text
Work in D:\Sanad_API. Read governance, current main handoff, Chat Plus operations,
integration report, worker-prompts.md, docs/internal/operations/publish.md and
deploy-runbook.md. Ignore Bruno entirely. Mastermind owns integration, final
publication, main push and separately authorized deployment. Preserve others' edits.
Create worker/chat-plus-vps-readiness from latest main; pin SHA/owned manifest.

Own the bounded release verifier/publish workflow corrections, production-style
DataProtection/storage configuration, sanitized Linux service/proxy templates
and deployment readiness report. No live VPS changes or credentials in this task.

Resolve these evidenced gaps:
1. Existing deploy/publish-out manifest is 41f9c91 (before Chat Plus). Treat it as
   stale, not as a release. After your branch is integrated, the mastermind publishes
   the exact clean pushed main revision with all nine current migration IDs.
2. Preserve the owner's confirmed AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader
   policy exactly. Do not add origin restrictions in the API or proxy, and do
   not list the confirmed configuration as a readiness gap.
3. Chat sync cursors use DataProtection; fixtures enable ephemeral keys. Pin a
   persistent Linux key ring/application discriminator outside versioned releases,
   permissions/protection and restart behavior. Never use fixture seeding or
   ephemeral keys on the publicly reachable VPS; verify old cursor recovery.
4. Persist public/private uploads outside release folders with least-privilege
   service access. Do not delete/move shared files during deploy or rollback.
5. Verify-SanadDeployment currently checks a local manifest and one lookup HTTP
   response; it does not prove remote revision or migration application. Add a
   safe remote revision/health mechanism and actual nine-context migration evidence
   using startup logs or authorized read-only queries. Do not expose DB details.
6. Release contract mapping must check required Postman coverage only; remove Bruno
   from this release gate/scan path so it cannot fail or delay publication. Update
   active publish/runbook instructions accordingly; do not invent passing exits.

Prepare single-instance .NET 10 Linux service and reverse-proxy templates for TLS,
WebSocket/SSE/Long Polling, idle timeouts, 52 MiB multipart, unchanged CORS,
no query-token/body/header logging, health, restart and durable volume permissions.
Specify all commands as reviewable templates, not shell commands containing secrets.

For a development/testing VPS use Staging and a separate named test database.
Never publish Development to the public host. Staging startup applies all pending
module migrations including Finance and may invoke startup seeders: enumerate
actual side effects/configuration, verify App:TestUserSeed disabled, bootstrap
admin behavior and provider-safe settings. Never assume a .env file is loaded by
ASP.NET automatically; systemd/host must inject its actual environment.

Require backup, exact host/database/service/revision, migration ordering, restore
procedure and rollback compatibility before deployment. Application rollback alone
cannot undo database changes. Do not perform lossy down-migrations on populated
production data. Validate templates/scripts and proxy/key persistence controls
with focused tests; zero-warning build if code changes.

Report READY or NOT READY for isolated staging and production separately, naming
every unresolved target/environment/secret/device dependency. Commit branch and
reports/remaining-vps-readiness.md, give base/final SHA and manifest. No merge/push
main, VPS login/write, package deployment, migrations, seeding or provider sends.
```

## Prompt 6 — recipient-query PostgreSQL translation hardening (completed)

This prompt is complete and integrated on current main. Both predicates were
proven untranslatable by Npgsql SQL generation, fixed with typed `UserId`
comparison, and covered by translation regressions; see
`reports/processor-integration.md`. Continue with Prompt 7 authorization.

```text
Work in D:\Sanad_API. Read AGENTS.md, docs/internal/governance/AGENTS.md,
docs/internal/Mastermind_Handoff.md, Chat Plus tasks/handoff, and
docs/internal/work/chat-plus/reports/processor-integration.md. Ignore Bruno
entirely. Preserve all other changes. Work directly on current `main` per the
repository's standing instruction; record the starting SHA and owned manifest.

Goal: resolve and prevent the two concrete, unverified EF/Npgsql recipient
predicate risks reported after Prompt 3:
1. CareHomeBookingNotificationProcessor: targets.Contains(x.Id.Value)
2. PushOutboxProcessor: user.Id.Value == row.RecipientUserId (both known sites)

First inspect each exact production query and its callers. Reproduce current
Npgsql SQL-generation behavior with ToQueryString() without opening a connection.
Do not assume every .Value is defective: Prompt 3 established that typed-value
projections can translate; the concern is member access inside the predicate.
Correct only predicates proven untranslatable, using provider-safe strongly typed
comparison/query composition and client-side unwrapping after materialization
where needed. Preserve recipient selection, active/role rules, token eligibility,
notification/push content, idempotency, ordering, batching, retries, privacy and
failure behavior exactly. Do not touch CareHomeLicenseExpiryMonitor solely for
its recipient projection; it is not a confirmed defect.

Own only the two processor files, directly relevant focused tests, and a concise
report/handoff update. Add Npgsql-provider translation regressions over the exact
production query composition for every changed predicate (including both
PushOutboxProcessor sites if both use it). These tests must generate SQL without
connecting to PostgreSQL. Add focused mapping/selection tests for any changed
client-side mapping; label InMemory as mapping coverage only, never PG proof.
Do not run a test fixture that calls EnsureDeleted against the configured target.
No API startup, live DB, migrations, provider sends, VPS work or deployment.

Acceptance: query translation tests fail on the old query and pass on the fixed
query; focused semantics tests preserve recipients and behavior; full solution
build succeeds with zero warnings/errors; relevant focused tests and architecture
tests pass; NuGet audit remains clear; git diff --check is clean. If a provider
query cannot be proven, report the precise blocker rather than claiming it fixed.

Update processor-integration.md, Chat Plus tasks/handoff, and this prompt status
with exact base/final SHA, changed manifest, commands/counts, and limits. Work
on current main per repository instructions, commit and push only the scoped
implementation/docs, and report the exact pushed SHA. Do not deploy or perform
live runtime verification. The mastermind reviews the change and owns the next
release handoff.
```

## Prompt 7 — post-deploy verification and developer/device handoff

```text
Read repository governance, the current Chat Plus plan/contracts/operations,
worker-prompts.md and actual integrated-main release reports. Ignore Bruno.
This is verification, not permission to change a VPS, DB or provider configuration.
The mastermind owns integration, main push and defect triage.

Target input must name authorized host URL, Staging/test database, deployed SHA,
service, fixture permission and permitted provider/device tests. Inspect these
before requests. Use the Prompt 4 page if available; otherwise direct supported
API/SignalR clients and the developer test pack. Do not wait for a page to test.

Verify actual remote SHA, all nine migration histories, anonymous lookup smoke,
Normal/auth/eligibility rules, two-party send/read/file/location, unread, blocks,
hide/report moderation, >100-message + receipt-only sync, typing/privacy/multi-
device presence, server restart, persistent cursor keys, token expiry/revocation,
owner-confirmed unchanged CORS and WebSocket/REST fallback. Respect rate limits.

On a separately authorized payment fixture, compare pricing API with direct SQL
for >=5 Hourly and >=5 EightHour attributed completed/paid samples, thin pools,
window/rounding and exclusion rules. If unavailable report that exact gap; never
change paid status manually or call real payments by accident.

Mobile developers/owner: validate Firebase project/APNs bundle/key/environment,
physical Android+iPhone foreground/background/taps, permissions, token refresh,
logout, preferences, blocking and retries; capture no credentials/device tokens.
Mark live-provider proof unrun until actual authorized device evidence exists.

Stop on an unexpected failure, retain sanitized request/response/log evidence,
and return the smallest reproducible defect to the mastermind. Do not broaden
fixtures, reset DB or repeatedly replay consuming workflows. End by logging out
only created sessions and stopping only processes explicitly owned by the test.

Return deployed SHA, exact scenarios/statuses, pass/fail/skip counts, fixture
effects, device evidence, blockers and proposed correction manifest. If adding
repo evidence, update only sanitized `reports/remaining-vps-verification.md` on
current main per repository instructions. No automatic code fixes or unapproved
runtime changes; return defects to the mastermind for a separate scope.
```

## Final mastermind deployment order (not authorized by these prompts)

After Prompt 6 is reviewed and its clean main revision is pushed, publish that
exact revision using the corrected workflow. Confirm package SHA/migrations/no secrets. Obtain
the exact authorized VPS host, URL, service/port, environment, database and
backup/restore boundary, plus startup migration/seed permission. Deploy only
that artifact, verify remote revision/health/migration counts, then run Prompt 7.
No default inference that the current live VPS/database is disposable.

Do not describe this as gap-free until the readiness report and corresponding
runtime/device evidence establish it. A development release can document staged
limitations without claiming production security/storage/push completeness.

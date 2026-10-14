# Care Homes processor integration — 2026-10-14

## Integration

Integrated `worker/care-home-review-processor-fix`, based on `329186d`, worker
implementation `2a9387b`, report tip `dc3d43c`. The branch was reviewed and
fast-forwarded without conflict into local `main` through `a869bb0`; the
integration commit adds a central transitive pin for the patched Pkcs dependency.
No unrelated worker files were included. At integration, `main` was based on
`origin/main` `329186d`. Documentation closeout `aee2490` was pushed to
`origin/main`; local and remote main were verified to match at that revision.

## Change and review

The repeated notification batch failure came from the owner recipient predicate
`x.Id.Value == ownerId`, which Npgsql cannot translate for the converted
`UserId` value object. Production now compares `x.Id` to a typed `UserId` and
projects typed `ReviewRecipientRow` values, unwrapping identifiers/email only
after query materialization. The admin query's role and active-user rules,
recipient content/state, idempotency, batch bound, retry behavior, and privacy
remain unchanged. Two Npgsql `ToQueryString()` tests cover the exact production
query composition; mapping behavior is separately covered with InMemory tests.

The worker reported four NU1902/NU1903 restore-audit warnings for transitive
`System.Security.Cryptography.Pkcs` 10.0.0, including an advisory published
2026-10-13. Rather than suppress the warning or leave the release gate dirty,
`Directory.Packages.props` now centrally pins the transitive package to patched
10.0.13. The official GitHub advisory lists 10.0.13 as patched for the Windows
CMS decode denial-of-service advisory
([GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7)); the
NuGet audit also confirms no vulnerable packages remain in the API graph. The
second high-severity advisory is reported by NuGet as cleared at that version.

## Verification performed on integrated `main`

- `dotnet build Sanad.slnx -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0,
  zero warnings/errors (after package restore).
- `CareHomeReviewNotificationTranslationTests` — 5/5; includes provider SQL
  generation only, no PostgreSQL connection.
- `FullyQualifiedName~Sanad.UnitTests.CareHomes` — 209/209.
- Notifications + Identity tests excluding `IdentityPostgresIntegrationTests`
  — 663/663. Excluded because the configured local PostgreSQL fixture invokes
  `EnsureDeleted`; it was not safe to run without explicit disposable-target
  authorization.
- Architecture tests — 1/1.
- `dotnet list src/API/Sanad.API/Sanad.API.csproj package --vulnerable --include-transitive`
  — no vulnerable API packages.
- `git diff --check` — clean before documentation closeout.

No full unit suite, live PostgreSQL dispatch, API startup, migration, provider
send, VPS operation or deployment was performed. This fix does not establish
runtime delivery health. The two sibling recipient predicates called out below
were resolved by Prompt 6 (see next section); `CareHomeLicenseExpiryMonitor` has
`.Value` in its recipient projection, not a predicate, and projections translated
in the repaired processor, so it was deliberately left untouched. CORS remains
unchanged per the owner's explicit instruction; Bruno was entirely ignored.

## Prompt 6 — recipient-query hardening — 2026-10-14

Worked directly on `main` per the standing instruction. Starting SHA `0f625e6`
(clean tree, matching `origin/main`); implementation commit `ba48afb`, pushed to
`origin/main` as `0f625e6..ba48afb` (this SHA-record commit follows docs-only).

Owned manifest (only these files changed):

- `src/API/Sanad.API/CareHomesIntegration/CareHomeBookingNotificationProcessor.cs`
- `src/API/Sanad.API/NotificationsIntegration/PushOutboxProcessor.cs`
- `tests/Sanad.UnitTests/CareHomes/CareHomeBookingNotificationTranslationTests.cs` (new)
- `tests/Sanad.UnitTests/Notifications/PushRecipientTranslationTests.cs` (new)
- `docs/internal/work/chat-plus/reports/processor-integration.md` (this section + handoff sync)
- `docs/internal/work/chat-plus/tasks.md`, `handoff.md`, `worker-prompts.md` (Prompt 6 status only)

Reproduction first (Npgsql `ToQueryString()`, no connection opened): the old
`targets.Contains(x.Id.Value)` and both `x.Id.Value == row.RecipientUserId`
predicates throw `InvalidOperationException` (could not be translated), while
typed `UserId[]`-`Contains(x.Id)` and `x.Id == typedUserId` generate SQL. Both
reported risks were therefore proven defects, not assumptions. The scratch
reproduction was deleted; only the regressions below are committed.

Fixes (provider-safe comparison/query composition, client-side unwrap after
materialization; same pattern as Prompt 3):

- Booking processor: `ResolveRecipientsAsync` is now public and delegates to
  `TargetRecipientRows` (`Where(x => targets.Contains(x.Id))` over `UserId[]`)
  and `OperationalAdminRecipientRows` (unchanged predicate, exposed so the exact
  composition is covered), projecting typed `BookingRecipientRow` and mapping to
  `BookingRecipient` after materialization. Event branching (admins-only /
  owner-only / family / family+owner), role/active rules, copy, idempotency,
  ordering (none imposed, as before), batching, retries and privacy are unchanged.
- Push processor: both preference reads (`DeliverAsync` non-chat gate and the
  `IsChatPushEligibleAsync` final-send recheck) now compose the single public
  `RecipientUsers` query (`Where(user => user.Id == recipientId)`); the owned-type
  `NotificationPreferences` projection is unchanged. Token eligibility, block/
  visibility/session checks, suppression vs retry vs sent transitions, and failure
  behavior are unchanged. `DeviceTokens.Where(x => x.UserId == ...)` compares plain
  `Guid`s and was never defective; untouched.

Verification on `main` (no API startup, live DB, migration, provider send, VPS or deployment):

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, 0 warnings / 0 errors.
- New translation/mapping tests — 7/7 (`CareHomeBookingNotificationTranslationTests` 5/5:
  2 Npgsql SQL-generation facts over the exact production compositions + 3 InMemory
  selection/mapping facts; `PushRecipientTranslationTests` 2/2: 1 Npgsql fact over the
  shared composition used by both sites + 1 InMemory selection fact). InMemory facts are
  mapping coverage only, never PostgreSQL proof.
- Focused `FullyQualifiedName~CareHomes` — 214/214 (includes the 5 new facts and the
  pre-existing end-to-end booking-notification semantics).
- `(FullyQualifiedName~Notifications|FullyQualifiedName~Identity)&(FullyQualifiedName!~IdentityPostgresIntegrationTests)`
  — 704/704. `IdentityPostgresIntegrationTests` excluded because its fixture calls
  `EnsureDeleted` against the configured target (verified excluded via `--list-tests`;
  no `EnsureDeleted` fixture ran).
- Focused `FullyQualifiedName~ChatPlus` — 72/72 (includes the `IsChatPushEligibleAsync`
  session/preference/block eligibility facts, preserving final-send semantics).
- Architecture tests — 1/1.
- `dotnet list src/API/Sanad.API/Sanad.API.csproj package --vulnerable --include-transitive`
  — no vulnerable API packages (Pkcs pin from Prompt 3 holds).
- `git diff --check` — clean.

## Remaining work / handoff

- Optional Prompt 4 tester page/manual developer pack may be skipped; it is not
  a staging prerequisite.
- Prompt 7 requires an explicitly authorized isolated VPS/test database,
  exact deployed artifact, backup/restore readiness and permitted fixture/provider
  checks. Staging and production remain NOT READY until their gates are evidenced.
- High priority before production: resolved by Prompt 6 above (both recipient
  predicates fixed with exact-query Npgsql SQL-generation regressions). Safe
  PostgreSQL execution remains unauthenticated and unrun; do not claim live
  dispatch proof. Do not infer failure solely from
  `CareHomeLicenseExpiryMonitor`'s `.Value` projection.
- Prompt 6 report SHA `c29339b` was verified as the pushed `origin/main` before
  this separate mastermind closeout commit.

## Mastermind final audit — 2026-10-14

Reviewed Prompt 6 on pushed `main` at `c29339b` (worker SHA-record commit;
implementation is `ba48afb`). Starting from that tree, I fixed the identity
test fixture's null-dispose when PostgreSQL tests skip. Re-ran the full solution build with
`/p:TreatWarningsAsErrors=true`: 0 warnings, 0 errors. Ran the full Unit suite
with `ConnectionStrings__IdentityIntegrationDatabase` and
`ConnectionStrings__FamiliesIntegrationDatabase` blanked for the test process
only, so all PostgreSQL integration cases were skipped and no configured
database could be reset: 3,120 passed, 0 failed, 8 skipped. Of the skips, 5 are
PostgreSQL integration cases (2 Identity, 3 Chat); 3 are existing Families
skips (2 legacy conflict-mapping facts and 1 legacy concurrency fact). Architecture
passed 1/1. `git diff --check` passed.

The first full-suite run exposed a harness-only null-dispose error when the
Identity PostgreSQL fixture correctly skipped initialization without a target.
`LocalPostgresIdentityFixture.DisposeAsync` is now null-safe. The full suite was
rerun after the fix and exited cleanly without fixture cleanup errors. This
test-only correction does not change application behavior. No database
connection, API startup, migration, provider, VPS or deployment action occurred.

Code and automated test gates are green. Environment readiness remains open:
there is no named/authorized staging host, URL/service/port, isolated DB,
host-injected secrets/settings, verified backup/restore boundary, or fresh
publish package from final `main`. Prompt 4 is optional. Prompt 7 is deployment
runtime/device verification and must wait for exact-target authorization.
Production-only evidence still open includes Android/iPhone push, paid-pool API
versus SQL equality, edge TLS/WebSocket checks, live session revocation, and
attachment-cleanup observation/soak. No production-ready claim is made.

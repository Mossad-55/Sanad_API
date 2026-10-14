# Care Homes processor integration — 2026-10-14

## Integration

Integrated `worker/care-home-review-processor-fix`, based on `329186d`, worker
implementation `2a9387b`, report tip `dc3d43c`. The branch was reviewed and
fast-forwarded without conflict into local `main` through `a869bb0`; the
integration commit adds a central transitive pin for the patched Pkcs dependency.
No unrelated worker files were included. At integration, `main` was based on
`origin/main` `329186d`; publication status is recorded in the final handoff.

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
runtime delivery health. Sibling typed-ID query smells in `CareHomeBookingNotificationProcessor`,
`PushOutboxProcessor`, and `CareHomeLicenseExpiryMonitor` remain out of scope and
should be reviewed as a separate bounded slice. CORS remains unchanged per the
owner's explicit instruction; Bruno was entirely ignored.

## Remaining work / handoff

- Optional Prompt 4 tester page/manual developer pack may be skipped; it is not
  a staging prerequisite.
- Prompt 6 requires an explicitly authorized isolated VPS/test database,
  exact deployed artifact, backup/restore readiness and permitted fixture/provider
  checks. Staging and production remain NOT READY until their gates are evidenced.
- Before closing the sibling processor risk, inspect exact Npgsql query shapes
  and add provider translation regressions; do not infer failure solely from
  `.Value` projections, which translated in this fix.
- Final handoff verifies the pushed `origin/main` revision after the documentation
  closeout is committed.

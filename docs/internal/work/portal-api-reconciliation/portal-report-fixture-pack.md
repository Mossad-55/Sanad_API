# W5 portal report fixture pack — host and operator guide

Status: **authored, offline-verified and runtime-verified on 2026-10-24**
(the runtime receipt is
`docs/internal/work/portal-api-reconciliation/reports/w5-portal-fixture-runtime-2026-10-24.md`).
The bounded host compiles clean (Release, `TreatWarningsAsErrors`), its wrapper
passes 17/17 offline tests, and on the guarded local target it has since run the
real lifecycle: preflight passed for all nine module contexts, all twelve groups
were provisioned with persisted readbacks over three consecutive idempotent
runs, authenticated family and admin HTTP readbacks succeeded against the
loopback listener, and `Cleanup` produced a report-only classification with no
delete. No provider was contacted and no real money moved.

| Artifact | Path |
|---|---|
| Bounded test-only host | `tools/Sanad.PortalFixtureHost/` (`Sanad.PortalFixtureHost.csproj`, `Program.cs`, `ScriptedPaymobClient.cs`, `PortalFixtureProvisioner.cs`, `PortalFixtureTargetGuard.cs`) |
| Safe lifecycle wrapper | `tools/Invoke-PortalReportFixturePack.ps1` |
| Offline tests | `tests/Sanad.UnitTests/PortalReportFixturePackTests.cs` |
| Inventory | "W5 fixture inventory" in `docs/internal/work/portal-api-reconciliation/worker-prompts/portal-report-follow-up-2026-10-24.md` (baseline `eb218a002b598e66d3e62013d2c1d198d66c3768`) |

The host project is intentionally **absent from `Sanad.slnx`**: it is a local
tooling artifact, not part of the shipped solution. It is the only new project
and it adds no package; the wrapper and the guide are reworked in place.

## Audience

- **Portal reconciliation reviewers** who need repeatable, portal-browsable
  LOCAL evidence for the W5 portal-report scenarios without touching Production
  or Paymob.
- **The operator** who owns the private runtime credentials, runs the lifecycle
  on a verified local machine and reads the redacted manifest afterwards.
- **Future maintainers**: the twelve group ids are a single pinned list shared by
  the host, the wrapper, the tests and this map; a group that cannot be
  provisioned from the confirmed domain surface is reported unresolved rather
  than faked.

This pack is not a load test, an integration suite, a migration mechanism or a
substitute for the W1–W4 focused suites and their full-suite gates.

## What the host guarantees

1. **Real controllers, real handlers, loopback only.** The host composes the
   real `Sanad.API` registrations and pipeline
   (`WebApplication.CreateBuilder` + `AddSanadApi` + `UseSanadApi`), so it serves
   the real MVC routes and MediatR handlers. `ConfigureTestServices` and
   `WebApplicationFactory` are deliberately **not** used: this is a real Kestrel
   listener, bound to a loopback address (`localhost` / `127.0.0.1` / `::1`)
   only. `--address` is refused unless it parses to an absolute HTTP(S) loopback
   URL, and there is no remote fallback.
2. **Provider isolation is structural, not config-derived.** The host removes
   and replaces the provider-capable seams in DI — Families/CareHomes
   `IPaymobClient`, Finance `ISubscriptionRefundGateway`, Identity `ISmsSender`
   and `IEmailSender`, Notifications `IPushSender` and `IDateTimeProvider` — and
   then **proves by resolution** that each seam is the explicit double and that
   no `Sanad.*` background writer survived removal, *before* the listener or the
   provisioner runs. A live key in appsettings, user secrets or the environment
   cannot make this host call a provider; any ambient live-key/integration
   observation is reported for the operator but never decides pass/fail.
3. **Target guard before any stateful startup.** All nine module `DbContext`s
   must target exactly `localhost:5432/SanadDb` and report **zero pending
   migrations**, the environment must be `Development`, and the storage root must
   be a local absolute path. An unreadable target or an empty/unreadable
   pending-migration set is a failure, never a silent pass. `Preflight` opens a
   read-only connection to verify this and then exits without listening; the host
   never applies a migration, never creates or drops a database and never runs
   the production seeder.
4. **Fail closed.** Any failed check, unprovisionable group or refused credential
   file stops the run with a concrete reason. `Start` provisions **before** it
   listens and refuses to listen when provisioning is incomplete unless
   `--allow-partial` is passed.
5. **Secrets are private.** Credentials arrive only through `SANAD_W5_*`
   environment variables in the operator's own shell. The wrapper validates the
   approved local target and minimum secret lengths, requires the credential
   file to resolve **outside the repository**, creates it **empty**, proves a
   current-user-only ACL (Windows: protected ACL; POSIX: `0600`, verified) and
   only then writes the secret bytes — an ACL failure deletes the file and stops
   the run, so a secret is never written into an unprotected file. It never
   prints, logs or manifests a secret value. **Every mode** streams *both*
   stdout and stderr through the redactor, which scrubs bearer/basic tokens,
   password-like keys, `Host=`/`Server=` connection-string fragments, the
   `credentials-<tag>.json` name and `SANAD_W5_*` surfaces; a failure in any
   mode reports a redacted reason and a non-zero exit. The host reads the file,
   validates it again, and never echoes it. Windows detection is correct on
   Windows PowerShell 5.1 and PowerShell 7+ (no silent ACL skip), and the
   offline suite pins all of the above.
6. **No provider call, and no fabricated provider movement.** The scripted
   payment doubles are registered and DI-proven, but the ambiguity/failure/
   evidenced-refund states are constructed at the **domain level** (see below);
   provisioning issues no gateway call at all. Every synthetic reference carries
   the `sanad-portal-fixture-synthetic` prefix so no reader can mistake it for
   provider evidence.

## Wrapper lifecycle

The wrapper writes no fixture data itself and never talks to a provider. It
drives the host and fails closed on stray switches per mode.

| Mode | Does | Requires |
|---|---|---|
| `Plan` (default) | Deterministic host contract + the twelve groups. No credential, process, database or listener. | — |
| `Preflight` | Writes the credential file, then runs the host's read-only preflight: builds the container, proves the target and the DI isolation, prints JSON, exits. **Read-only: never listens, never writes, never migrates.** | `SANAD_W5_*` |
| `Start` | Writes the credential file, then runs the host in start mode: preflight, bounded provisioning, then the loopback listener (blocking). | `-ConfirmStart`, `SANAD_W5_*` |
| `Reuse` | Probes an already-running listener at `-BaseUrl` and reports whether it is THIS host for the pinned `-Revision`. An unknown listener is refused. | loopback `-BaseUrl` |
| `Stop` | Confirms the listener is THIS host via the identity probe, resolves the owning process, refuses anything that is not the fixture host, then stops only that process. | `-ConfirmStop`, `-BaseUrl` |
| `Cleanup` | **REPORT ONLY.** Reads a run manifest and classifies its fixture-owned records. The tool has no delete surface: it constructs no SQL, opens no database and deletes nothing. | `-RunManifestPath` |

The host exposes one extra read-only route the wrapper uses to bind a listener to
this pack: `GET /__portal-fixture/identity` returns
`{ marker: "sanad-portal-fixture-host", revision, runTag, provisionPassed, manifestPath }`.
No secret is ever returned. A pre-existing listener that does not answer with
this marker is never reused or stopped.

### Read-only preflight (the safe first command)

```powershell
# No credential, process, database or listener: prints the contract.
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-PortalReportFixturePack.ps1 -Mode Plan

# Proves the local target and the DI isolation, then exits without listening.
$env:SANAD_W5_CONNECTION_STRING   = 'Host=localhost;Port=5432;Database=SanadDb;Username=<you>;Password=<you>'
$env:SANAD_W5_JWT_SIGNING_KEY     = '<at least 32 characters, private>'
$env:SANAD_W5_FIXTURE_PASSWORD    = '<at least 12 characters, private>'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-PortalReportFixturePack.ps1 -Mode Preflight
```

### Side-effect-free host self-check

```powershell
# Runs the SAME domain drivers the provisioner uses, in memory only.
dotnet tools\Sanad.PortalFixtureHost\bin\Release\net10.0\Sanad.PortalFixtureHost.dll --mode selfcheck --instant 2026-10-24T10:00:00Z
```

`--mode selfcheck` executes `PortalFixtureDomainChecks` — the exact sequences the
provisioner calls to reach each money state (`DrivePendingToConfirmed`,
`DriveVisitToCompleted`, `DriveConfirmedToFailedRefund`, `RequireCompletedChronology`)
— against the real domain aggregates and prints
`{"passed":true,"checks":[…]}`, then exits 0/1. It opens no credential, no
configuration, no `DbContext`, no listener and no provider call, and it persists
nothing. The offline test suite spawns it, so a chronology or refund-state
regression in the shared drivers fails the suite without a database.

## Private runtime inputs

| Variable | Used for |
|---|---|
| `SANAD_W5_CONNECTION_STRING` | The approved local target; must resolve to `localhost:5432/SanadDb`. Never printed. |
| `SANAD_W5_JWT_SIGNING_KEY` | The host's local JWT signing key (≥ 32 chars). Never printed. |
| `SANAD_W5_FIXTURE_PASSWORD` | The synthetic password for the fixture identities this run creates (≥ 12 chars). Never printed. |

The credential file is written to
`%TEMP%\opencode\sanad-portal-fixture\credentials-<runTag>.json` (or under
`-WorkRoot`) with a current-user-only ACL. That path **must resolve outside the
repository**: `Assert-ArtifactOutsideRepo` refuses an in-repo artifact, the file
is created empty and ACL-hardened before any secret byte is written, and an ACL
failure deletes the file and stops the run. The run manifest is written by the
host to `portal-fixture-manifest-<runTag>.json` in the same root and contains no
secret.

## Scenario map

The twelve group ids are pinned in `PortalFixtureGroups.All` (host), in
`Get-PortalFixtureGroups` (wrapper) and in the offline tests, and are the
`--provision-groups` values, the manifest keys and this table. Every record a
group creates is tagged with the run tag so ownership is machine-checkable and a
rerun reuses rather than duplicates.

| Group id | W5 inventory item | Provisioned state (local, synthetic) |
|---|---|---|
| `caregiver-refund-ambiguity` | Caregiver refund ambiguity | Paid + caretaker-accepted Families booking with the refund-ambiguity marker raised; persisted refund state resolves `Pending` (uncertain, never `Failed`). |
| `caregiver-refund-failure` | Caregiver refund failure | Paid + accepted Families booking driven through claim → release → `CancelledByCaregiver`; persisted refund state resolves **`Failed`** on a closed-without-refund status, so the portal offers the retry (the retained claim id/award are the durable evidence). |
| `caregiver-completed-booking` | Completed paid caregiver booking / payout | Completed paid Families booking (monotonic confirmed→started→completed) plus a recorded `CaregiverPayout` with a run-unique `transferReference` and synthetic evidence. |
| `carehome-paid-awaiting-decision` | PaidAwaitingDecision (accept/reject) | **Two** run-owned PaidAwaitingDecision slots (one to accept, one to reject) plus a persisted rejected example driven to `RefundPending`, so the refund read path is exercised without consuming either live decision slot. |
| `carehome-refund-ambiguity` | CareHome refund ambiguity | Accepted paid CareHome booking cancelled by the facility with the refund-ambiguity marker raised. |
| `carehome-refund-failure` | CareHome refund failure | Accepted paid CareHome booking with the refund marked failed after a synthetic refusal. |
| `carehome-evidenced-external-refund` | Evidenced external refund | Accepted paid CareHome booking with the refund initiated and completed from synthetic external evidence. |
| `carehome-booking-extension` | Booking extension | An accepted paid segment plus a `CareHomeBooking` extension linked by `ExtensionOfBookingId`. |
| `carehome-checkout-dispute` | Checkout without family confirmation / dispute | Checked-in CareHome booking with no family confirmation and an open `CareHomeCheckInDispute`. |
| `subscription-and-duplicate-claim` | Current paid subscription / shutdown + new duplicate-claim review | Current paid Premium `FamilySubscription` with a succeeded, linked `SubscriptionPaymentAttempt` and a duplicate-charge claim read back **`Submitted`** awaiting Support; **plus** a successful paid service-shutdown refund on a **separate** run-owned family/subscription/capture — prorated award, `RefundLedgerEntry` and budget claim all read back — so shutdown and the duplicate claim never compete for one capture's budget. |
| `complaint-received` | Complaint Received | A `ComplaintReview` in `Received`, linked to the fixture's **own** duplicate-charge `RefundCaseId`. |
| `complaint-investigating` | Complaint Investigating | The same fixture-owned `ComplaintReview` moved to `Investigating` (never pulled back if manually advanced). |

Base fixture shared by every group (bounded, fixture-owned): a family owner, a
medical caregiver in full availability, an elderly member, an approved CareHome
facility with one room type/room/bed, a SuperAdmin, a Support admin, published
Free/Premium plan versions and the Finance charge rule. All identities are
load-or-create by synthetic email/phone and **no existing password or account
state is ever reset**, so a manually progressed case survives a rerun.

### Fixture feature rows (bounded inventory)

One row per W5 inventory item. "Readback" is a persisted, post-provision query
(the group fails, and is reported unresolved, if the readback does not match);
the twelve group ids alone are not evidence. Ten rows, no more.

| # | W5 inventory item | Group id(s) | Persisted readback asserted | Claimed semantics |
|---|---|---|---|---|
| 1 | Caregiver refund ambiguity | `caregiver-refund-ambiguity` | `Bookings` row: ambiguity marker set; `BookingRefundStates.Resolve == Pending`. | Uncertain refund is portal-visible; **never** `Failed`. |
| 2 | Caregiver refund failure | `caregiver-refund-failure` | `Bookings` row: closed-without-refund status, released claim retained; `Resolve == Failed`. | Failed + retryable, consistent with shipped refund rules. |
| 3 | CareHome refund ambiguity | `carehome-refund-ambiguity` | `Bookings` row: facility-cancelled, ambiguity marker set. | Uncertain CareHome refund is portal-visible. |
| 4 | CareHome refund failure | `carehome-refund-failure` | `Bookings` row: refund marked failed after synthetic refusal. | Failed CareHome refund is portal-visible. |
| 5 | PaidAwaitingDecision accept/reject | `carehome-paid-awaiting-decision` | Three `Bookings` rows read back: two `PaidAwaitingDecision` (accept + reject slots) and one `RefundPending` (rejected example). | Both live halves exist; the reject read path is proven without consuming a slot. |
| 6 | Evidenced external refund | `carehome-evidenced-external-refund` | `Bookings` row: refund initiated **and** completed from synthetic evidence. | Completed synthetic external refund, prefixed so it cannot read as provider evidence. |
| 7 | Checkout without family confirmation / dispute | `carehome-checkout-dispute` | `Bookings` row: checked-in, no family confirmation; open `CareHomeCheckInDispute` row. | Dispute is portal-visible. |
| 8 | Completed paid caregiver booking / payout | `caregiver-completed-booking` | `Bookings` row: `Completed` with monotonic confirmed→started→completed; `CaregiverPayout` row persisted. | Completed booking **and** payout, not just one. |
| 9 | Booking extension | `carehome-booking-extension` | Extension `Bookings` row with `ExtensionOfBookingId` pointing at the fixture's own accepted/paid parent. | Extension ownership is fixture-verified. |
| 10 | Current paid subscription / shutdown + duplicate-claim review | `subscription-and-duplicate-claim` | `FamilySubscription` current+Premium; `SubscriptionPaymentAttempt` succeeded; duplicate claim `Submitted`; **separate** shutdown subscription/capture with `RefundLedgerEntry` + budget claim read back; two `ComplaintReview` rows in `Received`/`Investigating` linked to the fixture's own `RefundCaseId`. | Shutdown refund is **persisted, not just reachable**, on its own capture; the duplicate claim is ready for Support; complaints are fixture-owned. |

### The no-provider-call approach

Refund ambiguity/failure and evidenced external refunds need a movement that a
local pack must not fake as a Paymob settlement. The provisioner therefore
constructs those states at the **domain level**:

- Families bookings: `ClaimRefundAttempt` → `MarkRefundOutcomeAmbiguous` /
  `ReleaseRefundAttempt`.
- CareHome bookings: `CancelByFacility` → `TryClaimRefund` → `MarkRefundFailed`
  / `MarkRefundInitiated` + `MarkRefundCompleted`.
- Duplicate-charge claim: submitted through the real
  `SubmitDuplicateChargeClaimCommand` (which awards nothing and calls no
  gateway).
- Service-shutdown refund: executed through the real
  `RefundSubscriptionServiceShutdownCommand`, whose handler resolves the
  current-period capture and records the prorated movement through the Finance
  `ISubscriptionRefundRecorder` seam (budget claim + append-only
  `RefundLedgerEntry` + idempotency `SubscriptionRefundRequest`). That seam is
  money-*accounting*, not a provider call; the provider-facing
  `ISubscriptionRefundGateway` double is registered and DI-proven but is **not
  invoked**.

The scripted doubles (`ScriptedPaymobClient`, `ScriptedSubscriptionRefundGateway`)
exist so no provider-capable registration survives in DI, and their resolution is
proven before the listener runs — but provisioning itself issues no gateway call,
and every synthetic reference is prefixed so none can read as provider evidence.

## Honest coverage and clock limits

- **Clock.** `IDateTimeProvider` is overridden on this test host only, with an
  explicit instant (`--instant`). This does **not** control all time in the
  process: domain setters in Families/Finance call `DateTime.UtcNow` directly,
  so audit timestamps written by those paths are real wall-clock values. The
  fixture writes its own explicit instants where the domain surface accepts
  them; it does not backdate stored timestamps it does not own and touches no
  production clock.
- **Unresolved groups.** The host reports `passed: false` and lists every group
  in `unresolvedGroups` when it cannot provision it, and refuses to listen
  unless `--allow-partial` is passed. A group is never partially faked. All
  twelve groups are implemented against confirmed domain/MediatR surface, but
  their semantics are **unverified until a runtime slot on the approved local
  `SanadDb`**; the first `Start` is what proves them.
- **Target.** The approved target is the persistent local `SanadDb`; a disposable
  target would need separate Owner approval for exact-target creation and
  migration. The pack never creates, migrates or drops a database.

## Cleanup

`Cleanup` is report-only by construction: the wrapper classifies each manifest
record (`report-only-money-free` for duplicate-charge claims and complaint
reviews; `retained-operator-decision` for everything money-adjacent) and
**constructs no SQL**. There is no `Get-PortalFixtureCleanupSql` function, no
`DELETE` statement and no `psql` invocation anywhere in the tool; the offline
test suite asserts that. Actually removing a fixture row remains a separate,
explicitly authorized action.

## What has run and what has not

**Ran (offline only, 2026-10-24):**

- `dotnet build tools/Sanad.PortalFixtureHost/Sanad.PortalFixtureHost.csproj -c Release -p:TreatWarningsAsErrors=true`
  → Build succeeded, 0 warnings, 0 errors.
- `dotnet build tests/Sanad.UnitTests/Sanad.UnitTests.csproj -c Release -p:TreatWarningsAsErrors=true`
  → Build succeeded, 0 warnings, 0 errors.
- `dotnet test tests/Sanad.UnitTests -c Release --no-build --filter FullyQualifiedName~PortalReportFixturePackTests`
  → Failed: 0, Passed: 17, Skipped: 0 (includes the `--mode selfcheck` spawn test).
- `dotnet test tests/Sanad.UnitTests -c Release --no-build --filter "FullyQualifiedName~PortalReport|…~BookingRefund|…~SubscriptionRefund|…~DuplicateCharge|…~Complaint"`
  → Failed: 0, Passed: 282, Skipped: 0 (focused offline/domain regression).
- `dotnet tools/Sanad.PortalFixtureHost/bin/Release/net10.0/Sanad.PortalFixtureHost.dll --mode selfcheck --instant 2026-10-24T10:00:00Z`
  → exit 0, `{"passed":true,"checks":["families.booking.completed","families.refund.failed","families.refund.ambiguous","carehome.reject.refundpending"]}`.
- `powershell … -File tools\Invoke-PortalReportFixturePack.ps1 -Mode Plan` (display
  run) and the host `--mode plan` (display run): both side-effect free.

**NOT RUN (explicitly):**

- No listener has been started; `--mode start` has never executed.
- No fixture row has been written; the provisioner has never run against a
  database.
- No migration has been applied, created or dropped; the production seeder has
  never been invoked by this pack.
- No request has been sent to Paymob or any external provider; no production or
  remote database has been touched.
- `Reuse`, `Stop` and `Cleanup` have never run end to end.

The first real `Start` is the operator's bounded next action on a verified local
target, following the lifecycle above.

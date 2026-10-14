# Chat Plus storage — Prompt 2 worker report (race-safe orphan cleanup)

Branch: `worker/chat-plus-storage`. Base: main `b46728a` (clean tree at intake).
Final SHA: tip of `worker/chat-plus-storage` (single commit on top of base; see `git log`).
No main merge/push or deployment occurred.

## Owned manifest

Product code (Chat unbound attachment lifecycle + cleanup only):
- `src/Modules/Chat/Domain` — unchanged (Attachment entity untouched).
- `src/Modules/Chat/Application/.../Attachments/UploadAttachmentCommandHandler.cs`
  — quota now counts ALL unbound bytes per user (expired or not).
- `src/Modules/Chat/Application/.../Messages/SendMessageCommand.cs`
  — bind loss to a racing sweep surfaces 404, not a silent orphan.
- `src/Modules/Chat/Application/.../Attachments/ChatAttachmentCleanupOptions.cs`
  (new) — `ChatPlus:AttachmentCleanup` (sweep 1 h, batch 100).
- `src/Modules/Chat/Application/.../Attachments/ChatAttachmentDeletionOutcome.cs`
  (new) — Deleted / Missing / Failed.
- `src/Modules/Chat/Application/.../Attachments/IChatAttachmentCleanupStorage.cs`
  (new) — private-storage cleanup port.
- `src/Modules/Chat/Application/.../Attachments/IChatAttachmentCleanupMetrics.cs`
  (new) — sanitized backlog/result records + port.
- `src/Modules/Chat/Application/.../Attachments/ChatAttachmentCleanupMetrics.cs`
  (new) — DB-computed backlog, logged sweep aggregates.
- `src/Modules/Chat/Infrastructure/.../Attachments/ChatAttachmentCleanupService.cs`
  (new) — bounded sweep (claim → delete blobs → delete rows).
- `src/Modules/Chat/Infrastructure/.../Storage/LocalDiskChatAttachmentCleanupStorage.cs`
  (new) — deletes only `private/chat-attachments/…` keys below the private root.
- `src/Modules/Chat/Infrastructure/.../Persistence/Configurations/AttachmentConfiguration.cs`
  — IsBound is a concurrency token; index on (IsBound, UploadedOnUtc).
- `src/Modules/Chat/Infrastructure/.../Persistence/Migrations/20261013171755_AddAttachmentCleanupIndex.cs`
  (new, authored only, NOT applied) + snapshot sync.
- `src/Modules/Chat/Infrastructure/.../DependencyInjection.cs`
  — cleanup storage/service/metrics/options registration.
- `src/Modules/Chat/Infrastructure/.../*.csproj`
  — BuildingBlocks.Infrastructure reference (LocalStorageOptions).
- `src/API/Sanad.API/ChatPlus/ChatAttachmentCleanupHostedService.cs`
  (new) — hourly sweep loop in a fresh scope.
- `src/API/Sanad.API/DependencyInjection.cs`
  — hosted-service registration.

Tests (temporary storage roots; separate disposable PG database):
- `tests/Sanad.UnitTests/Chat/AttachmentCleanupTests.cs` (new, 7 InMemory).
- `tests/Sanad.UnitTests/Chat/AttachmentCleanupPostgresTests.cs` (new, 3 PG race).
- `tests/Sanad.UnitTests/Chat/ChatPostgresCleanupFixture.cs` (new).
- `tests/Sanad.UnitTests/Chat/ChatPostgresCollection.cs` (new).
- `tests/Sanad.UnitTests/Chat/ChatPostgresFactAttribute.cs` (new).

Docs (actual behavior):
- `docs/app/chat/operations.md` — sweep, exclusion, quota, metrics.
- `docs/internal/work/chat-plus/contracts.md` — quota/exclusion wording.

## Actual behavior (verified)

- Expiry: unbound attachments older than 24 h (`ChatPlus:RateLimits:UnboundAttachmentLifetime`)
  are sweep-eligible. A send binding an expired unbound upload reads 404.
- Sweep: every 1 h (`ChatPlus:AttachmentCleanup:SweepInterval`), at most 100
  (`ChatPlus:AttachmentCleanup:BatchSize`) expired unbound objects per pass.
  The claim is `SELECT … FOR UPDATE SKIP LOCKED`; the row delete is conditional
  `DELETE … WHERE IsBound = FALSE`. Only exact `private/chat-attachments/…` keys
  below the configured private root are deleted.
- Bind exclusion: the send's `UPDATE … WHERE IsBound = FALSE` (IsBound concurrency
  token) and the sweep claim serialize on the row lock. A bound file is never
  deleted; a claimed file is never silently bound (send fails 404).
- Quota: ALL unbound bytes per user count against 100 MiB
  (`ChatPlus:RateLimits:UnboundQuotaBytesPerUser`), expired or not, because a DB
  row exists exactly while its blob is occupied. Bound history never counts.
- Metrics: `GetBacklogAsync` (expired unbound count + bytes, no PII) and
  `RecordSweep` (deleted count/bytes + failures, logged). No user ids, keys,
  conversation ids, or payloads.

## Executed checks

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo`: exit 0,
  0 warnings / 0 errors.
- `dotnet test … --filter FullyQualifiedName~Chat`: 239 passed / 0 failed.
  Includes 7 new InMemory (sweep deletes, ignores live/bound/foreign, boundary
  cutoff, missing blob, failed-then-retry, 100-per-pass bound, expired-bytes quota)
  and 3 new PostgreSQL (concurrent bind-vs-sweep invariant, sweep-first 404,
  send-first preservation) on the authorized local target
  (`ConnectionStrings__IdentityIntegrationDatabase`, separate
  `SanadChatCleanupTestDb`, temp storage roots, schema EnsureCreated, dropped after).
- `dotnet test tests/Sanad.ArchitectureTests`: 1/1.
- `dotnet ef migrations has-pending-model-changes` (Chat): clean.
- Migration `20261013171755_AddAttachmentCleanupIndex` is authored only;
  never applied to any database.

## Recovery guarantees

- Crash between blob delete and row delete: the row (still expired unbound) is
  re-claimed on the next pass; the blob is already gone (Missing) and the row
  is deleted. Proven by the Missing-blob test.
- Failed blob delete: the row is kept; the next pass retries it. Proven by the
  Failed-then-retry test.
- Crash between passes / restart: the sweep is stateless (no durable cursor);
  the next pass re-evaluates expiry from the pinned clock. Proven by the
  restart-recovery path (Missing blob after manual delete).
- Racing bind: the row lock serializes the sweep claim and the send bind;
  the loser observes the winner's committed state (sweep skips a bound row;
  send 404s on a deleted row). Proven by the concurrent PG race (invariant:
  no message references a missing blob).

## Remaining limitations (not gaps in this worker's scope)

- The sweep's FOR UPDATE SKIP LOCKED claim is PostgreSQL-specific. Other
  relational providers (SQLite/SQL Server) are not supported by the claim SQL;
  InMemory uses a plain query (no concurrency to prove). Production is
  PostgreSQL; no other provider is claimed.
- The `= ANY(@ids)` row-delete parameter relies on Npgsql array mapping;
  proven on PostgreSQL only.
- Long-window (multi-hour) runtime observation of the hourly sweep was not
  performed; the interval/batch behavior is unit- and PG-proven, not
  soak-proven.
- Live production migration application, VPS file-system verification, and
  operator backlog alerting (beyond logs) remain with the release owner.
  No API start, migration application, fixture write, or provider send
  occurred in this worker.

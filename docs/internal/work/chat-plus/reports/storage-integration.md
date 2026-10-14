# Chat Plus storage cleanup integration

Worker branch: `worker/chat-plus-storage`, based on clean main `b46728a`.
Worker implementation/report commit: `814b669`; mastermind integration
corrections: `f38b158` and `8097edb` (fast-forwarded/published). Documentation
closeout follows these code commits on main.

## Integrated behavior

- Expired unbound attachments are physically swept hourly in batches of at most
  100. PostgreSQL `FOR UPDATE SKIP LOCKED` claims rows while the blob is removed;
  conditional row deletion and the `IsBound` concurrency token serialize the
  sweep against a send binding the attachment.
- Failed deletion retains its row for retry; missing blobs allow stale rows to
  be removed on a later pass. Deletion is limited to normalized
  `private/chat-attachments/…` keys contained under the configured private root.
- Per-user upload quota counts every unbound row, including expired uploads,
  until physical deletion and row removal complete. Bound historical files do
  not consume this temporary-upload quota.
- Backlog and sweep logs use aggregate counts/bytes. No attachment IDs or
  storage keys are logged.
- Migration `20261013171755_AddAttachmentCleanupIndex` adds the cleanup index and
  is authored only; it has not been applied.

## Mastermind review corrections

Review found `ChatAttachmentCleanupService` required `Func<DateTime>` but no
production registration existed. The hosted sweep would therefore fail to
resolve its service at runtime. Chat infrastructure now registers the UTC clock,
and a production-DI regression confirms the cleanup service resolves.

Cleanup configuration now validates a positive sweep interval and a batch size
between 1 and 100 at application startup. The unbound-attachment lifetime and
per-user quota must also be positive. The hosted service uses singleton options
directly instead of creating an undisposed scope for each delay. Path resolution
is inside the per-blob failure boundary and verifies the resolved path remains
under the private root; a test confirms foreign and escaping keys are refused.
Per-attachment warning logs no longer include an attachment ID. The PostgreSQL
delete parameter array was made non-nullable to satisfy the strict compiler gate.

## Verification

Mastermind ran on the final integrated code before documentation closeout:

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, zero warnings/errors.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter FullyQualifiedName~AttachmentCleanupTests --nologo` — 9/9 passed, including production DI resolution and storage-key guards.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter "FullyQualifiedName~Chat&FullyQualifiedName!~AttachmentCleanupPostgresTests" --nologo` — 238/238 passed.
- `dotnet test tests/Sanad.ArchitectureTests/Sanad.ArchitectureTests.csproj --no-build --no-restore --nologo` — 1/1 passed.
- `git diff --check` — clean before commit.

The worker reports a zero-warning build, Chat tests 239/239, architecture 1/1,
EF model alignment, and 3/3 PostgreSQL race tests using a separate disposable
`SanadChatCleanupTestDb` with temporary storage roots; that test database was
dropped after its run. The mastermind did not repeat those PostgreSQL tests:
the current environment has a configured integration target, and the fixture
uses `EnsureDeleted` for that separate database. No database operation was
performed during this integration. The PostgreSQL results above remain
worker-reported evidence, not a mastermind rerun.

## Remaining verification

No full solution suite was run. No API was started, migration applied, VPS
filesystem inspected, or provider contacted. Staging still needs an authorized
deployment of a fresh package, confirmation that persistent private storage is
mounted and writable by the service account, observed hourly sweep behavior,
backlog alerting, and a soak long enough to demonstrate recovery and disk bounds.
The PostgreSQL claim SQL is specific to the production PostgreSQL provider.

Prompt 2 is complete. Next implementation prompt is Prompt 3, the bounded Care
Homes notification processor translation correction. Bruno remains excluded;
owner-confirmed CORS remains unchanged; no VPS or production database operation
is implied by this integration.

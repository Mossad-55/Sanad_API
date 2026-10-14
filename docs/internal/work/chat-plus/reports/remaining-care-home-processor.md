# Prompt 3 — bounded Care Homes processor correction (worker report)

Branch: `worker/care-home-review-processor-fix`.
Base: `329186dd482fd7f6754675b9c0768053be5f06cd` (clean `main` at intake; `git status` clean, tree matches `origin/main`).
Code-final SHA: `2a9387bfe87e60f99336e116c9dd6c3b4cc9d4a6` (build + all gates ran on that tree; `main` untouched, not merged/pushed).
This report is committed docs-only on top; it changes no code, so the gates below remain valid for the branch tip.

## Exact manifest (owned files only)

- `src/API/Sanad.API/CareHomesIntegration/CareHomeReviewNotificationProcessor.cs` (fix only; recipient roles, content, state, idempotency, bounds, retry, privacy unchanged)
- `tests/Sanad.UnitTests/CareHomes/CareHomeReviewNotificationTranslationTests.cs` (new; PostgreSQL translation regression + recipient-mapping facts)
- `docs/internal/work/chat-plus/reports/remaining-care-home-processor.md` (this report)

No other files touched. No API start, migration, fixture, provider send, or live-target operation occurred.

## 1. Actual root cause (confirmed first, not assumed)

Reported symptom (phase-7-release §5): recurring EF LINQ translation failure logging
`u.Id.Value` comparison in `CareHomeReviewNotificationProcessor` on PostgreSQL.

Confirmed by Npgsql-provider `ToQueryString()` probes (no connection opened, no live target):

- FAILING path: `NotifyAsync` owner lookup —
  `identity.Users.Where(x => x.Id.Value == ownerId)` throws
  `System.InvalidOperationException: The LINQ expression 'DbSet<User>().Where(u => u.Id.Value == @ownerId)'
  could not be translated.` EF Core cannot translate member access `.Value` on the
  value-converted `UserId` struct (`HasConversion(id => id.Value, value => new UserId(value))`,
  `UserConfiguration.cs:21-26`) inside a query predicate. This is the recurring failure:
  the owner lookup runs per event inside `NotifyAsync`, so its exception aborts that
  notification (owner AND admin copy for submission events) and the whole batch pass,
  then `ExecuteAsync` logs and retries every 20 s — matching the observed recurrence.
- NOT defective (verified translating on the same provider, left byte-identical in behavior):
  `.Select(x => new { Id = x.Id.Value, Email = x.Email == null ? null : x.Email.Value, ... })`
  projections translate fine (`SELECT u.id, u.email, u.ui_language`), as does the admin
  `Where` (`Status` + `Accounts.Any(...)`). Per the brief, only the predicate was broken.
- In-memory lines `facility.Id.Value` / `facility.OwnerUserId.Value` (`:41,49`) operate on
  already-materialized entities, not LINQ-to-Entities — unaffected.

Fix (same proven pattern as finance commit `5edd341`: compare/project strongly-typed IDs,
unwrap `.Value` client-side): the owner predicate is now `Where(x => x.Id == ownerKey)` with
`ownerKey = new UserId(ownerId)`; both recipient queries project typed
`ReviewRecipientRow(UserId, Email?, UiLanguage)` server-side and map to `ReviewRecipient(Guid, string?, UiLanguage)`
after materialization. Recipient roles (exact owner; Active SuperAdmin/SupportAdmin only),
notification content/state, idempotency keys, Take(500) bound, 20 s retry, and projected
fields are unchanged. Sibling processors with the same smell (`CareHomeBookingNotificationProcessor:115`,
`PushOutboxProcessor:84,324`, `CareHomeLicenseExpiryMonitor:61`) are out of this manifest and untouched.

## 2. Checks actually run

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` → exit 0,
  0 errors, 0 compiler warnings. 4 pre-existing NU1902/NU1903 restore-audit warnings
  (`System.Security.Cryptography.Pkcs` vulnerability advisories on `Sanad.API`/`Sanad.UnitTests`
  package graphs) — present before this change (visible in pre-fix probe runs), no package
  touched in scope; strictly the zero-warning gate is therefore not clean, see §3.
- New `CareHomeReviewNotificationTranslationTests`: 5/5 pass —
  2× Npgsql `ToQueryString()` translation proofs over the exact production query composition
  (`OwnerRecipientRows`, `OperationalAdminRecipientRows`; no connection opened), plus
  3× InMemory recipient-mapping facts (exact owner + language; null-email → null;
  only active Super/Support admins, ContentAdmin/owner/suspended excluded). The InMemory
  facts prove mapping only; PostgreSQL proof rests solely on the Npgsql facts.
- Focused `FullyQualifiedName~CareHomes`: 209/209 pass, 0 failed.
- Focused `FullyQualifiedName~Notifications|FullyQualifiedName~Identity`: 704/704 pass, 0 failed.
- Scratch Npgsql probes used for root-cause isolation were deleted; only the regression file above is committed.

## 3. Runtime limitations (nothing hidden)

1. No live PostgreSQL execution: translation is proven by Npgsql SQL generation, behavior by
   InMemory mapping. Live end-to-end dispatch (real outbox → inbox/email rows on a disposable
   target) was not run — no target authorization in this prompt.
2. Build gate caveat above: 4 pre-existing NU1902/NU1903 audit warnings remain; upgrading the
   flagged package is out of scope and intentionally not attempted.
3. Same-defect siblings listed in §1 remain unfixed by design (separate manifest/owner decision needed).
4. Bruno ignored per owner direction; no Bruno authored, run, or read.

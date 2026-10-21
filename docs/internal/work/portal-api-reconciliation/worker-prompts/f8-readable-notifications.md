# F8 — Readable notifications

Owner-approved follow-up scope; continued execution/release authorized 2026-10-21.
Base: `1e91eca`; local E06 changes are owned by Mastermind and read-only to F8.
Phase: accepted; task: complete; release: authorized after Mastermind acceptance.
Worker B: `river/deepseek-ai/DeepSeek-V4.1-Flash#high`.

## Pinned behavior

Correct confirmed GUID-bearing Care Home booking/review/transfer/license bodies and
chat bodies, plus English-only help-request, medication-late, SOS, negative-check-in,
admin account-status/password and support-reply titles. Use recipient persisted
UiLanguage (Arabic=1, English=2); absent language uses existing Arabic default.
Batch lookup where recipients are plural. Preserve recipient eligibility, preferences,
delivery, retries, categories/types, deduplication and structured destination IDs.
Chat is generic: never message text, conversation GUID or message GUID in human copy.
Legitimate room/bed labels and effective dates remain; Arabic labels are Arabic.
Support replies keep the administrator-authored body byte-for-byte; localize title only.
No historical rewrite, schema, migration, new delivery workflow or provider calls.
F5 save-first and independently best-effort email/inbox behavior is already released
and must remain intact. Earlier chat GUID-body assertions are superseded only for
presentation, never for privacy or routing.

## Exact writable manifest

- src/API/Sanad.API/CareHomesIntegration/CareHomeBookingNotificationProcessor.cs
- src/API/Sanad.API/CareHomesIntegration/CareHomeReviewNotificationProcessor.cs
- src/API/Sanad.API/CareHomesIntegration/CareHomeTransferNotificationProcessor.cs
- src/API/Sanad.API/CareHomesIntegration/CareHomeLicenseExpiryMonitor.cs
- src/API/Sanad.API/NotificationsIntegration/SupportReplyNotificationGateway.cs
- src/API/Sanad.API/DependencyInjection.cs (language registration and existing status publisher only)
- src/API/Sanad.API/NotificationsIntegration/NotificationLanguageGateway.cs (new)
- src/Modules/Notifications/Application/Sanad.Modules.Notifications.Application/Abstractions/Recipients/INotificationLanguageGateway.cs (new)
- src/Modules/Notifications/Application/Sanad.Modules.Notifications.Application/Notifications/NotificationTextCatalog.cs (new)
- src/Modules/Notifications/Application/Sanad.Modules.Notifications.Application/Notifications/NotificationCommands.cs
- src/Modules/Notifications/Application/Sanad.Modules.Notifications.Application/Notifications/CreateChatMessageNotificationCommand.cs
- tests/Sanad.UnitTests/Chat/ChatMessageNotificationTests.cs
- tests/Sanad.UnitTests/Notifications/CheckInAlertNotificationCommandTests.cs
- tests/Sanad.UnitTests/Notifications/HelpRequestNotificationCommandTests.cs
- tests/Sanad.UnitTests/Notifications/MedicationLateAlertNotificationTests.cs
- tests/Sanad.UnitTests/Notifications/SosNotificationTests.cs
- tests/Sanad.UnitTests/Notifications/ReadableNotificationTests.cs (new)
- docs/app/notifications.md
- docs/admin/notifications.md
- docs/admin/support-inbox.md
- docs/postman/Sanad.Auth.postman_collection.json
- tools/Invoke-F8LocalHttpVerification.ps1 (new)
- This brief.

Constructor compatibility edits in other test files require precise amendment request.
No general Admin Postman, Identity/Families source, E06 files, shared fixture tools,
handoff, commits or deployments. Preserve unrelated dirty work.

## Acceptance and runtime ownership

Focused bilingual tests for changed copy plus unchanged destination, deduplication,
recipient/preference and F5 failure behavior. Actual new notifications must have no raw
IDs in generated copy. Existing rows and authored support bodies are exempt.
Strict Release build zero warnings/errors; focused regression classes; JSON/doc checks.
Author a minimal real localhost adapter on port 5251 using L1/F6 library helpers and
persistent INSERT-only owned fixtures; dedicated identities, fail-closed target guard,
no live providers, every created session logged out. Prove bilingual support-reply
generation through HTTP and recipient inbox/destination readback. Other producer
branches require focused tests; disclose which lack end-to-end HTTP evidence.
Never insert finished inbox rows and call that producer verification.

Mastermind currently holds the E06 runtime slot: author source/tests/docs/adapter only;
do not build, test, start API or mutate fixtures until explicit slot transfer.
No subagents, no new framework, no whole-suite run. Keep changes simple.

## Current result

Preparation handback (Worker B, `river/deepseek-ai/DeepSeek-V4.1-Flash#high`).
Session base `1e91eca`; HEAD `48f9864` (Mastermind E06 commits `a5977a6`, `90f02c7`,
`48f9864` on top). Runtime slot still Mastermind-held: authoring only, no build, test,
API start or fixture mutation performed; no runtime F8 evidence claimed.

Changed files (all in manifest unless marked):
- `src/API/Sanad.API/NotificationsIntegration/NotificationLanguageGateway.cs` (new) —
  batch `INotificationLanguageGateway`; Npgsql set-based `identity.users`
  `ui_language` lookup, exposed as `UserLanguageRows` for tests.
- `src/Modules/Notifications/Application/.../Abstractions/Recipients/INotificationLanguageGateway.cs`
  (new) — `Guid`-keyed batch seam; missing/null entry resolves to Arabic.
- `src/Modules/Notifications/Application/.../Notifications/NotificationTextCatalog.cs`
  (new) — chat, negative-check-in, help-request, medication-late, SOS, support-reply
  title and admin status/password copy; `ResolveLanguage`/`IsArabic`/
  `TryResolveInboundCopy`; no raw GUIDs in generated copy.
- `src/Modules/Notifications/Application/.../Notifications/NotificationCommands.cs` —
  check-in, help, medication and SOS fan-outs take the gateway as a required third
  dependency and do one batch lookup per fan-out; negative-check-in copy applied in the
  check-in handler with `TryResolveInboundCopy` falling back to caller-supplied copy.
- `src/Modules/Notifications/Application/.../Notifications/CreateChatMessageNotificationCommand.cs`
  — gateway as required second ctor parameter; generic copy (no message text,
  conversation GUID or message GUID); ids stay on destination/idempotency keys.
- `src/API/Sanad.API/NotificationsIntegration/SupportReplyNotificationGateway.cs` —
  title localized; administrator-authored body passed through byte-for-byte.
- `src/API/Sanad.API/CareHomesIntegration/CareHomeBookingNotificationProcessor.cs`,
  `CareHomeReviewNotificationProcessor.cs`, `CareHomeTransferNotificationProcessor.cs`,
  `CareHomeLicenseExpiryMonitor.cs` — localized, id-free bodies; room/bed labels and
  effective dates preserved; `Copy(...)`/`ResourceLabelsAsync` statics reachable by tests.
- `src/API/Sanad.API/DependencyInjection.cs` — gateway registration (~653) and
  `AdminUserStatusNoticePublisher` localizing in-app copy while keeping English
  email title/body/idempotency key unchanged.
- `tests/Sanad.UnitTests/Notifications/ReadableNotificationTests.cs` (new) — bilingual
  copy matrix plus public `FixedNotificationLanguageGateway`
  (`ArabicDefault`, `For`, `LookupCount`, `RequestedIds`).
- `tests/Sanad.UnitTests/Chat/ChatMessageNotificationTests.cs`,
  `tests/Sanad.UnitTests/Notifications/CheckInAlertNotificationCommandTests.cs`,
  `HelpRequestNotificationCommandTests.cs`, `MedicationLateAlertNotificationTests.cs`,
  `SosNotificationTests.cs` — bilingual assertions added; category/type/destination/
  dedup/preference checks unchanged.
- `docs/app/notifications.md`, `docs/admin/notifications.md`,
  `docs/admin/support-inbox.md`, `docs/postman/Sanad.Auth.postman_collection.json`,
  `tools/Invoke-F8LocalHttpVerification.ps1` (new) — see checks below.
Unrelated dirty work (E05/CMS/families files, `buildout*.txt`, `testout*.txt`,
`.opencode/`, `workflow-audit/`, `README.md`, governance `AGENTS.md`, other Postman
collections) untouched.

PASS (run now, slot-free):
- Adapter file is UTF-8 with BOM: first bytes `EF BB BF`, 62998 bytes.
- Adapter AST parse: `parseErrors=0`.
- `docs/postman/Sanad.Auth.postman_collection.json` parses: 13 folders, F8 samples and
  descriptions present, no stale `elderly-not-okay` category/type.
- Grep: every `CreateChatMessageNotificationCommandHandler` /
  `NotificationCommands` construction site outside the manifest is accounted for;
  `ChatPlusPushTests.cs:198` is the single exception below.
- Inspected `ChatPlusPushTests.cs:38-42/68-73` and
  `ChatPlusSessionSecurityTests.cs:389`: synthetic push fixtures carrying only
  `notificationId`/`entityId`/`entityKind`/`deepLink` (+ `conversationId`/`messageId`),
  no body assertions — correctly unchanged.
- Adapter fixture `identity.users` bare-required columns confirmed (id,
  arabic_full_name, english_full_name, phone_number, ui_language, password_hash), so
  the INSERT column list is complete.

NOT RUN (blocked by Mastermind-held runtime slot; no evidence claimed):
- Strict Release build / zero-warning check.
- All focused test classes, including the new `ReadableNotificationTests`.
- Every `tools/Invoke-F8LocalHttpVerification.ps1` check on port 5251: bilingual
  support-reply HTTP generation, inbox/destination readback, title cross-check,
  byte-for-byte body comparison, session logouts.

BLOCKER — one amendment needs Mastermind approval to compile the test project
(1 file outside the manifest, 1 line): `tests/Sanad.UnitTests/ChatPlus/ChatPlusPushTests.cs:198`
currently `var producer = new CreateChatMessageNotificationCommandHandler(notifications);`
must become
`var producer = new CreateChatMessageNotificationCommandHandler(notifications, Sanad.UnitTests.Notifications.FixedNotificationLanguageGateway.ArabicDefault);`
(fully qualified, so no new `using`; the file's own push-data assertions do not change).
Alternative if the amendment is declined: make the new ctor parameter optional with an
Arabic default inside the producer — rejected by default because a silent Arabic
fallback would let a future call site skip the language lookup unnoticed.
Until one is applied, no focused test can run.

Disclosures:
- `src/API/Sanad.API/ElderlyCheckInAlertGateway.cs` is outside the manifest, so its
  English-only negative-check-in copy is now superseded inside the owned check-in
  handler rather than at that call site; the unused call-site literal is left untouched.
- End-to-end HTTP evidence is planned only for support replies (per acceptance). Chat,
  negative check-in, help request, medication-late, SOS, admin status/password, and the
  four Care Home producers are covered by focused tests only and will be disclosed as
  such at slot transfer.
- No verification of historical rows, schema or migrations was performed; none changed.

Next action: on explicit slot transfer, run the strict Release build, the focused test
classes and `tools/Invoke-F8LocalHttpVerification.ps1` on port 5251, then replace this
result with actual outcomes. Awaiting Mastermind acceptance and continuation.

## Acceptance (2026-10-21, Mastermind)

Runtime slot transferred; the worker's `NOT RUN` list is now executed. Phase state:
**`accepted`** (not committed, not released).

- The blocking amendment was applied as proposed: `ChatPlusPushTests.cs:198` now passes
  `Sanad.UnitTests.Notifications.FixedNotificationLanguageGateway.ArabicDefault` as the
  handler's second argument (fully qualified, no new `using`, the file's own push-data
  assertions unchanged). The amendment is therefore the brief's single approved
  out-of-manifest edit, exactly one line.
- Mastermind-fixed test defects in the F8-owned files (the worker's revision did not
  compile under warnings-as-errors, and five assertions were over-specified):
  four `Assert.Single(x.Where(...))` sites (xUnit2031) rewritten to
  `Assert.Single(x, pred)` in `CheckInAlertNotificationCommandTests`,
  `HelpRequestNotificationCommandTests`, `ReadableNotificationTests` and
  `SosNotificationTests`; `ReadableNotificationTests` catalog expectations completed to
  the full Arabic sentences the catalog actually produces; the capturing sender double
  now returns `Result<Guid>.Success(Guid.NewGuid())`; and the `LookupCount`
  expectations pinned per handler (2 for the first call plus idempotent replay in
  help/SOS/medication, 1 for check-in alert, which asserts before its replay).
- Adapter fixture correction: the three F8 identities moved from the taken
  `+201000000081/.082/.083` (E06's range, which made the INSERT-only provisioning fail
  on the `phone_number` unique key) to the free `+201000000096/.097/.098`.

Gate evidence actually run on the accepted tree:

- Strict Release `dotnet build Sanad.slnx -c Release /p:TreatWarningsAsErrors=true` →
  **succeeded, 0 warnings, 0 errors**.
- Focused tests `FullyQualifiedName~Notifications|FullyQualifiedName~Chat` →
  **323/323 passed**.
- `tools/Invoke-F8LocalHttpVerification.ps1 -BaseUrl http://localhost:5251` →
  **exit 0, 13/13 checks passed**, against persistent `SanadDb`, with the target guard
  green (all nine module heads, 0 pending) and every created session logged out.

Carried limitations (unchanged from the handback): only the support-reply producer has
end-to-end authenticated HTTP evidence; chat, negative check-in, help request,
medication-late, SOS, admin status/password and the four Care Home producers remain
focused-test only. No live SMTP/FCM claim is made. The adapter appends two tickets, two
replies and two notifications per run. Release state: **`not_requested`** — the
Production release is code-only and expected to apply 0 migrations.

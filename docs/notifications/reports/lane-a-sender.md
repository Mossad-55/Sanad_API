# Lane A — push sender

- **Task:** `IPushSender`, Firebase Admin sender pinned to 3.7.0, Development no-op, config-gated and fail-closed behavior.
- **Branch:** `codex/notifications-lane-a`
- **Base SHA:** `fe91d64d0f8b753d336f2fe8d3dc6b35e1a1e82b`
- **Final SHA (implementation):** `12b8611f45c7dc465946bbdab65806843366402a`
- **Changed files:** `Directory.Packages.props`; `src/Modules/Notifications/Application/Sanad.Modules.Notifications.Application/Abstractions/Push/IPushSender.cs`; `src/Modules/Notifications/Infrastructure/Sanad.Modules.Notifications.Infrastructure/Push/FcmPushSender.cs`; `src/Modules/Notifications/Infrastructure/Sanad.Modules.Notifications.Infrastructure/Push/DevelopmentNoOpPushSender.cs`; `src/Modules/Notifications/Infrastructure/Sanad.Modules.Notifications.Infrastructure/Sanad.Modules.Notifications.Infrastructure.csproj`; `src/API/Sanad.API/DependencyInjection.cs`; `tests/Sanad.UnitTests/Notifications/PushTests.cs`; sender setup documentation in `docs/app/notifications.md`.
- **Verification:** `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PushTests|FullyQualifiedName~FanoutTests' -m:1 --nologo --logger 'console;verbosity=minimal'` — exit 0, 21 passed, 0 failed, 0 skipped. `dotnet build Sanad.slnx --no-restore -m:1 --nologo` — exit 0, 0 warnings, 0 errors. `dotnet test Sanad.slnx --no-build --no-restore -m:1 --nologo --logger 'console;verbosity=minimal'` — exit 1: architecture 1 passed; unit 2,653 passed, 3 skipped, 1 failed. `git diff --check` — exit 0. `python -m json.tool docs/postman/Sanad.Auth.postman_collection.json > $null` — exit 0.
- **Skipped/blocked checks:** The single full-suite failure is the unrelated Finance active-version assertion in `PlatformChargeRuleTests.Writer_requires_strictly_increasing_versions_and_preserves_future_schedule`; 3 PostgreSQL-dependent tests skipped. No live Firebase or test-device send was run. The EF metadata-removal attempt reached a dummy localhost DB and failed authentication (`28P01`); no migration was applied and no database changed.
- **Dependencies confirmed in main:** Firebase Admin package was absent; version 3.7.0 and its required `Google.Apis.Auth` dependency are pinned for the sender. Existing API configuration and hosted-service composition are used.
- **Remaining owner decisions:** Mobile team confirmation of the `sanad://` deep-link prefix. Live provider verification needs explicit per-run owner authorization and a test device.
- **Effective model/runtime:** GPT-6; local Windows PowerShell.

Reports use the implementation SHA by owner direction. A final docs-only closeout commit follows these reports; the branch tip is recorded in `docs/notifications/handoff.md`.

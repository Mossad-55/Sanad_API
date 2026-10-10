# Lane A — docs and Postman

- **Task:** Sync app/admin notification documentation and Postman device requests to the Lane A contract.
- **Branch:** `codex/notifications-lane-a`
- **Base SHA:** `fe91d64d0f8b753d336f2fe8d3dc6b35e1a1e82b`
- **Final SHA (implementation):** `12b8611f45c7dc465946bbdab65806843366402a`
- **Changed files:** `docs/app/notifications.md`; `docs/admin/notifications.md`; `docs/postman/Sanad.Auth.postman_collection.json`.
- **Verification:** `python -m json.tool docs/postman/Sanad.Auth.postman_collection.json > $null` — exit 0. `git diff --check` — exit 0. `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PushTests|FullyQualifiedName~FanoutTests' -m:1 --nologo --logger 'console;verbosity=minimal'` — exit 0, 21 passed, 0 failed, 0 skipped. `dotnet build Sanad.slnx --no-restore -m:1 --nologo` — exit 0, 0 warnings, 0 errors. `dotnet test Sanad.slnx --no-build --no-restore -m:1 --nologo --logger 'console;verbosity=minimal'` — exit 1: architecture 1 passed; unit 2,653 passed, 3 skipped, 1 failed.
- **Skipped/blocked checks:** The full suite failure is the unrelated Finance active-version assertion in `PlatformChargeRuleTests.Writer_requires_strictly_increasing_versions_and_preserves_future_schedule`; 3 PostgreSQL-only tests skipped. The matrix documents Chat as unavailable in main and calls out the deep-link confirmation. No Firebase send. Migration is authored only; an EF metadata-removal attempt failed on dummy DB authentication (`28P01`) and did not mutate any database.
- **Dependencies confirmed in main:** Existing app routes and notification preferences were documented; device routes and delivery-state details added. Postman collection is `Sanad.Auth.postman_collection.json`.
- **Remaining owner decisions:** Mobile team to confirm deep-link prefix. The full-suite Finance failure should be handled by its owning area; no Lane A docs/Postman check was blocked by it.
- **Effective model/runtime:** GPT-6; local Windows PowerShell.

Reports use the implementation SHA by owner direction. A final docs-only closeout commit follows these reports; the branch tip is recorded in `docs/notifications/handoff.md`.

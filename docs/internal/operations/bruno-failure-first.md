# Bruno: failure-first execution and recovery

Optional runbook. Use it only when the task's risk/contract or the owner calls for a Bruno check. Bruno is not required for every endpoint or every task, and an unavailable fixture/runtime alone does not block a commit when the focused automated checks pass.

## Fixture readiness before authoring

Before writing Bruno requests, list the required actor roles and exact data states for each scenario. Mark which requests mutate/consume state and whether a fixture is replayable. The Mastermind checks existing data read-only first. If data is missing, seed only the minimum records on the already-authorized disposable target using supported APIs or an existing seeder; read back each state and confirm it is eligible before authoring/running the request. Give each consuming operation its own fresh entity/state. Do not reuse a cancelled booking for another cancellation case, replay onboarding creates, reset the database, or build a general-purpose fixture framework. If the target or required writes exceed existing authorization, stop before those writes and ask for that specific approval.

## If a live Bruno check is selected

1. Pin the primary checkout `D:/Sanad_API`, branch `main`, HEAD and relevant local delta. Routine Care Homes work is done in this checkout; the dedicated `care-homes-main` worktree has been removed. Keep edits/builds/API binaries/collections/fixtures on the same revision. Do not modify Git's global safe.directory to work around a path issue.
2. Launch the API only when a Bruno check is selected, using the approved local Development command/target recorded for that task. Do not invoke a fixture helper that regenerates identities unless needed and authorized. Reuse an already-running listener only after verifying its revision/database. Do not assume historical ports are available; changing ports does not isolate a shared database.
3. Record the sanitized database host/port/name, API PID and executable/build path, launch command and authorized startup side effects, fixture run ID, environment name, ordered request manifest and CLI version. Never record passwords, connection strings, tokens, HMAC secrets or private medical payloads. Revalidate this fingerprint after a restart or revision change. A generic 200 readiness response does not prove the correct binary/database.
4. Check whether an API already owns the intended listener. Reuse only if its worktree/build/database are verified and it is authorized; otherwise stop with the exact conflict. Never kill another worktree's process, cycle random ports or rewrite global configuration to force a run. If the target DB is shared, coordinate exclusive stateful access; two different ports do not make separate fixtures.
5. If running Bruno, use [offline preflight](../tools/Invoke-BrunoPreflight.ps1) with only the selected request paths when the manifest, target, or tooling changed. It checks static selection/target/tooling, not database safety, listener ownership, complete Bruno syntax, fixtures or readiness. It does not execute its returned command. Runtime verification remains the Mastermind's responsibility.

Reuse owner authorization already granted for the same target and actions. Ask again only for new authority, such as an unapproved reset or changed migration scope. Do not make each diagnostic retry a new permission loop.

Use the installed Bruno CLI 4.2.0 with explicit request paths, `--bail` and `--reporter-skip-body`. In the current PowerShell environment, `bru.ps1` is policy-blocked and `bru.cmd` previously stalled; the verified invocation was the direct Node `bru.js` entrypoint. Do not spend time retrying wrappers, discovering launchers, upgrading the CLI, or changing machine execution policy. Use the verified invocation recorded in the active handoff. Do not launch an entire collection tree to debug one endpoint.

## First failure: diagnose before environmental changes

At the first unexpected failure, stop the sequence and record a short sanitized failure card:

```text
Worktree / revision / relevant dirty delta:
API PID + build path / base URL / DB host:port:name / fixture run ID:
CLI version / ordered requests / first failed request:
Expected status + approved contract source:
Actual status + error code (redacted):
Correlated API log/exception or client parse/transport error:
Hypothesis / one smallest discriminating check:
Correction / rerun result / next action:
```

| Evidence | First check/correction | Not a justified response |
|---|---|---|
| CLI parse/launcher failure, no request sent | Selected file syntax, direct launcher, installed version | Restarting API or resetting DB |
| Connection refused/bind failure | Verified listener owner, PID, URL, existing process exit log | Random ports, killing unrelated processes |
| 415, empty body or malformed JSON | Actual wire Content-Type/body encoding and request template | DB reset or changing API validation to accept bad requests |
| 401/403 | Token/role/environment provenance against approved policy | Weakening authorization or changing expectation merely to pass |
| Unexpected 400/404/409 or assertion | Approved contract, request values and existing fixture state; locate controller/handler/validator | Assuming corrupt database from HTTP status alone |
| 500 | First matching server exception and responsible code/query | Full setup replay before inspecting logs |
| Missing/consumed fixture | Read existing entity state and prerequisite dependencies | Replaying destructive creates or dropping the whole DB |

The Mastermind routes a concrete code defect directly to the existing implementer (or applies a bounded integration fix), and a request/assertion defect to the tester. Choose the approved contract as authority; neither server nor assertion is automatically correct. An ambiguous business contract gets one focused question. Do not restart scout/reviewer phases for an ordinary correction.

### Known local multipart trap

For the verified CLI 4.2 requests, the HTTP method block needs `body: multipartForm`; the field block stays `body:multipart-form { ... }`. The historical `body: multipart-form` method selector sent an empty URL-encoded request and yielded 415. Inspect the rendered request before touching the database. Do not generalize this workaround to an unverified future CLI version or change every unrelated collection blindly.

## Tight correction loop

- Repair one evidenced cause. Rerun the failed request plus only prerequisite reads/logins needed to recreate its variables and state. Bruno variables may not survive between invocations; include those dependencies explicitly.
- An assertion/Bruno-only edit needs no API restart. API code fixes require rebuilding and restarting only the verified owned process from the same worktree; preserve its database/port/fixtures. Prove the new binary is running. Re-run gates invalidated by the actual code change.
- The fixture startup helper is NOT a stateless restart command: each launch generates a new run ID/password and rewrites `local-fixtures.bru`, and startup can seed/migrate. `-SkipTestUserSeed`/`-SkipFinanceMigrations` do not prove all module startup mutations are disabled. Inspect and authorize actual effects; do not infer read-only startup from those flags.
- Do not rerun the entire onboarding workflow against an already-created/approved facility. It has no general delete API. Resume from verified existing state with the smallest valid prerequisite sequence. If no supported restart preserves the required fixture identity, report that exact tooling gap rather than experimenting with flags or editing seed rows.
- Keep the same DB/port/fixture during diagnosis. Any reset needs a demonstrated fixture/schema problem that cannot be repaired in scope, exact disposable-target verification and separate owner authorization. A status-code mismatch is not that evidence. Do not reset to recover from a query translation, transport, parser or assertion defect.
- After two attempts with the same failure and no new evidence, or ten minutes without new evidence, stop rerunning. Report the exact limitation and continue based on focused automated evidence; an optional Bruno gap does not hold the slice by itself. New evidence-driven fixes may be tried when there is a concrete correction.
- Bound each request/command using supported runner/tool timeouts. Stop a proven stalled owned command; never kill all Node/dotnet processes. A hang must produce a launcher/process/log diagnosis, not another launcher lottery.

## Finish the selected check

After a concrete fix, rerun only the affected request and the minimum prerequisites. Do not replay a full collection by default. If the check was selected, report what actually ran, its result, and any skipped request; do not describe an unrun request as passed.

If Bruno is selected, its author/scout identifies the short ordered request list, prerequisites, fixture states, and which mutations consume state before requests are written. The Mastermind verifies/seeds the authorized disposable data and reads it back before execution. Reviewer checks only the changed contract and directly relevant coverage. Required build and focused automated checks must pass; full suite and Bruno are selected by risk/owner request, not required for every change. Do not repeat valid historical gates absent invalidated inputs.

Keep one current handoff checkpoint containing the pinned target, last valid gates, first open failure and next action. Put detailed dated history in the owning task evidence; label older entries superseded. Never alternate between stale “pending” and newer “passed” notes to restart finished work.

## Current tooling validation

Track implementation and offline tests in [Bruno reliability tasks](Bruno_Reliability_Tasks.md). No DB/API/Bruno runtime operation is required to validate documentation or the offline preflight itself. A preflight pass is not an endpoint test pass.

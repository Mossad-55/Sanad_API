# Bruno: failure-first execution and recovery

Mandatory for every Mastermind and worker. This refines recovery inside the existing worker sequence; it does not add phases or weaken unit/Bruno coverage or final build/test gates.

## Stable target before the first request

1. Pin the actual Git worktree root, HEAD and relevant local delta. In the current Care homes work, that is `D:/Sanad_API/.codex/worktrees/care-homes-main`, NOT the older detached root checkout. All edits/builds/API binaries/collections/fixtures must come from the same worktree. Do not modify Git's global safe.directory to work around this; use a command-local exception for the verified path if needed.
2. Launch the API with the already-approved, ordinary `dotnet run` command for this slice. Do not invoke a fixture helper that regenerates identities unless the active handoff explicitly requires and authorizes that setup. Use the already-pinned live listener when verified. Port `55819` is historical and was unavailable in the latest checkpoint; the latest verified Care Homes target is `http://localhost:5235`. A port change does not isolate a shared database.
3. Record the sanitized database host/port/name, API PID and executable/build path, launch command and authorized startup side effects, fixture run ID, environment name, ordered request manifest and CLI version. Never record passwords, connection strings, tokens, HMAC secrets or private medical payloads. Revalidate this fingerprint after a restart or revision change. A generic 200 readiness response does not prove the correct binary/database.
4. Check whether an API already owns the intended listener. Reuse only if its worktree/build/database are verified and it is authorized; otherwise stop with the exact conflict. Never kill another worktree's process, cycle random ports or rewrite global configuration to force a run. If the target DB is shared, coordinate exclusive stateful access; two different ports do not make separate fixtures.
5. Run [offline preflight](../tools/Invoke-BrunoPreflight.ps1) with explicit request paths. It checks static selection/target/tooling, not database safety, listener ownership, complete Bruno syntax, fixtures or readiness. It does not execute its returned command. Runtime verification remains the Mastermind's responsibility.

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
- After two attempts with the same failure signature and no new evidence, OR ten minutes without new diagnostic evidence, stop blind reruns. Report the failure card, one next hypothesis and any exact missing authority/dependency. Continue useful bounded diagnosis or independent authorized tasks; this is a checkpoint, not permission to skip Bruno or abandon the slice. New evidence-driven fixes are not arbitrarily capped at two attempts.
- Bound each request/command using supported runner/tool timeouts. Stop a proven stalled owned command; never kill all Node/dotnet processes. A hang must produce a launcher/process/log diagnosis, not another launcher lottery.

## Finish with the real gate

When the minimal reproducer passes, rerun the complete affected slice/changed-endpoint manifest with required success/failure/auth cases and disposable fixture readback/cleanup. A single fixed request is not endpoint closeout. Check selected/request/assertion counts and explicitly report skipped/parser-failed files; a selected request that did not run is a missing gate. Unrelated scanner skips are recorded and scoped, not silently called green coverage.

The test author supplies the ordered manifest, prerequisites, fixture-state expectations, replay-safe versus one-time mutations and cleanup plan with the tests—not after failures. Scout identifies runtime prerequisites early, and reviewer checks contract/coverage. Mastermind retains build → focused tests → full suite with successful exit and zero warnings, plus successful changed-endpoint Bruno. Do not repeat valid historical gates absent invalidated inputs.

Keep one current handoff checkpoint containing the pinned target, last valid gates, first open failure and next action. Put detailed dated history in the owning task evidence; label older entries superseded. Never alternate between stale “pending” and newer “passed” notes to restart finished work.

## Current tooling validation

Track implementation and offline tests in [Bruno reliability tasks](Bruno_Reliability_Tasks.md). No DB/API/Bruno runtime operation is required to validate documentation or the offline preflight itself. A preflight pass is not an endpoint test pass.

# Codex and OpenCode workflow

Follow [repository instructions](../governance/AGENTS.md). Start with the
[Mastermind handoff](../Mastermind_Handoff.md), inspect the worktree, and resume
the recorded active phase. The phase plan is the source of truth for scope and
status.

## One worker per phase, with a dependency gate for overlap

1. **Mastermind locks the current contract.** Confirm the owner's existing application
   behavior and supplied UI/evidence, then agree scope, permissions, exclusions,
   owned files, dependencies, acceptance criteria, and only the verification
   relevant to the change. Get explicit owner approval of the complete contract.
2. **Mastermind prepares the handoff.** Save one complete worker prompt with the
   base revision, exact file manifest, acceptance criteria, fixture/target limits,
   report path, and stop conditions. Give the owner a short kickoff with the
   prompt path and recommended effort. Do not paste the full prompt into chat.
3. **Dispatch one OpenCode session for the current phase.** Record the phase,
   base revision, prompt path, and `job_id` in the phase checkpoint before
   dispatch. Keep one active implementation session per phase. The worker owns the approved files and
   completes the whole assigned phase, including focused tests and feature
   docs/Postman when listed in its manifest. Do not add separate scout, test
   author, or documenter sessions when they would repeat that work; use them
   only for a specific independent need.
4. **Mastermind waits on that worker.** Do not message it, send progress nudges,
   launch a replacement, or edit its owned files. Wait on the same `job_id` until
   its explicit terminal report or blocker arrives. Startup text, progress,
   tool handoffs, timeouts, or ambiguous status are not completion. If the
   bridge treats any assistant text as completion, that is a bridge defect:
   mark the job status unknown, recover/check the same session, and do not
   dispatch a replacement until the defect is fixed and terminal state is
   confirmed.
5. **While waiting, assess only the next approved plan.** After the current
   worker is dispatched, the Mastermind may read the immediately following
   already-approved phase contract/file manifest and compare its owned files,
   stateful targets, outputs, and dependencies with the active phase. Do not
   scout, edit, or prompt the next phase as a way to manufacture independence.
   If any dependency, shared file/state, output handoff, or integration boundary
   is unclear, keep phases sequential and continue waiting for the current
   report. If independence is established, present the proposed overlap and its
   file/state boundary to the owner; only after the owner approves the next
   contract may a second OpenCode session start. The two workers then remain
   isolated and are each left alone until report/blocker. Never let them share
   files or stateful targets. The owner may instead direct sequential work.
6. **Worker stops at report or blocker.** The worker returns one concise report
   listing status, changed files, checks and results, assumptions, fixture/data
   effects, limitations, and any owner decision needed. It does not commit,
   push, deploy, or start the next phase. A blocker ends the run until the owner
   provides the missing decision/input.
7. **Mastermind reviews once.** Inspect the actual diff and evidence against the
   contract. Give the owner concrete findings. If a correction is needed, the
   owner routes the bounded correction to that same worker session; wait for its
   updated report, then review only the changed scope and affected evidence.
   Do not restart the phase or repeat unaffected review/checks.
8. **Owner approves release separately.** After acceptance, present the final
   evidence and ask once for the applicable commit/push/package/deployment
   authorization. Contract approval alone is not release approval. Do not
   re-request an authorization already given for the same phase and target.
9. **Mastermind closes the phase.** After release authorization, commit and push
   the accepted phase on `main`, package the pushed revision, deploy only to the
   authorized target, verify deployment, write one receipt, update the phase
   status and short handoff, then stop. Do not silently begin a later phase.

## Evidence and gates

- Select checks from the contract and risk: build the final code once, run
  focused automated tests for changed behavior, and use Bruno only when the
  live HTTP/stateful boundary adds necessary evidence or the owner requests it.
  Full Unit/Architecture suites are conditional on cross-cutting/high-risk
  scope or a phase's explicit release requirement. Do not run a fixed matrix for
  every endpoint.
- Treat a successful check as reusable evidence for the exact inputs it
  exercised: source tree, dependencies/toolchain, command/configuration, and,
  for stateful checks, target plus fixture state. If an input changes, rerun
  only the checks that depend on it. Record the evidence once with revision,
  command, result/counts, warnings/skips, and limitations. A failed/stalled run
  does not erase a different successful run with matching inputs.
- `git diff --check` belongs in final source review before commit. Do not repeat
  it during package/deployment after the pushed tree has been confirmed clean.
  Route/Postman mapping is required only when routes or API examples changed;
  it is static coverage evidence, not a runtime test.
- Packaging compiles the API. Do not run another solution build solely because
  packaging follows a successful build on unchanged inputs. The package step
  never runs tests. Confirm the package revision matches pushed `main`, all nine
  migration heads are source-derived, and no local settings/secrets are present.
- Deployment checks are not a second test suite: verify the exact target, keep a
  recoverable binary backup, start the service, check startup migration evidence,
  verify deployed revision and applied migration heads, run one agreed anonymous
  smoke, and compare protected host configuration fingerprints. Add target or
  feature-specific checks only when the phase changes that behavior. See
  [publish](publish.md) and [deploy runbook](deploy-runbook.md).
- Stop repeating a command when two attempts fail without new diagnostic
  evidence or it stalls. Report the precise blocker; do not launch concurrent
  retries that contend for test/build outputs.

## Role boundaries

- **Mastermind:** owner intake and contract, worker prompt, single-session
  coordination, bounded review, selected verification, release approval request,
  commit/push/release after approval, receipt, and handoff.
- **OpenCode phase worker:** implements the complete approved file manifest,
  adds/updates only contract-relevant tests and feature docs/Postman, reports
  evidence, then pauses. It does not set scope, make unresolved policy choices,
  release, or take the next phase.
- **Test author (optional):** independently designs tests only when the change
  needs substantial coverage design; does not duplicate tests the phase worker
  already authored.
- **Reviewer (one bounded pass):** reports concrete correctness, security,
  compatibility, and coverage findings. The Mastermind remains responsible for
  reviewing the actual changes.
- **Documenter (optional):** separate only when documentation is substantial or
  outside the phase worker's assigned files.
- **Owner:** approves the contract, receives reports and routes corrections,
  makes unresolved product/target decisions, and separately authorizes release.

For stateful checks, use an already authorized disposable target, verify the
minimal fixture before execution, and never run against production. Resets,
new targets, migrations, provider calls, and other persistent side effects need
specific authorization. See [Bruno failure-first runbook](bruno-failure-first.md).

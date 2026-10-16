# FUR-02 — Mastermind review (locally accepted; release pending)

Date: 2026-10-16 (Africa/Cairo). Status: **Locally accepted for release preparation; not committed, pushed or deployed. FUR-02 remains In progress, not Done.**

Baseline: `ba88f3c63120110dbf5e84744aaa46abbed01ad9` on `main`. The worker's FUR-02 delta remains local. Review compared the approved contract, source, migration, relevant tests, auth guides, Postman example, audience handoffs and worker delivery report. The worker's reported build/test/PostgreSQL results have not been independently rerun by Mastermind and are not release evidence yet.

## Bounded findings

1. **Provider timeout cancellation is misclassified.** `ResendOtpCommandHandler` rethrows every `OperationCanceledException` after `cancellationToken.ThrowIfCancellationRequested()`. When the HTTP request token is *not* cancelled, a provider/`HttpClient` timeout can also surface as `TaskCanceledException`/`OperationCanceledException`, and escapes the approved `502 Identity.Verification.ProviderDeliveryFailed` path. The current timeout test injects `TimeoutException`; the cancellation test injects `OperationCanceledException` with `CancellationToken.None` and expects propagation. Distinguish caller cancellation from provider cancellation/timeout and test both, including unchanged persisted state. Contract §2.2 requires timeout → 502 and caller cancellation propagation.
2. **The approved public message is not on the wire.** `ResendOtpErrors.ProviderDeliveryFailed` has the approved safe wording, but `ResultProblemDetailsMapper.Create` sets the HTTP `detail` for 502 to generic `The request could not be completed.` The error message is not otherwise serialized. The auth errors guide and Postman example reflect the generic detail, contradicting contract §2.2's public message. Expose the specified safe wording only for this error code, without exposing provider exception content; align mapper tests, guide and Postman. Also use the correct `Bad Gateway` title for 502 if changing this response mapping.
3. **Issue traceability is reversed.** The source audit identifies `#1` as phone OTP resend 500 and `#12` as signup verification recovery. The worker report and both dated audience handoffs label recovery as `FR-20261016-01` and resend as `FR-20261016-12`. Correct the headings, table rows and traceability links: `FR-20261016-01` = resend reliability; `FR-20261016-12` = lost-ID recovery. Do not assert a frontend code defect; that code was not inspected.

## Review disposition

Send [the bounded correction brief](../worker-prompts/fur-02-review-corrections.md) to the same worker. Do not start FUR-03, release FUR-02, or change production. After corrected delivery, inspect the delta and rerun invalidated focused gates plus accepted-phase release gates before requesting owner authorization for commit/push and separate publish/deploy authority.

## Follow-up review of R1–R3 delivery

The revised handler, problem-details mapper, mapper/controller tests, errors guide, Postman example, worker report and both audience handoffs now match R1–R3. The reported post-correction gates are strict build 0 warnings/errors, focused 156/0, guarded full unit 3457/0/12 skipped, architecture 1/1 and Postman 22/22 parse. These are worker-reported; Mastermind has not independently rerun them. The prior 4/4 disposable-PostgreSQL proof remains applicable because persistence code and migration did not change.

Two narrow verification-safety items remain before acceptance:

4. `docs/internal/tools/Test-SanadPortalGate.ps1` removes inherited `*IntegrationDatabase` targets in its actual test child, but not `ConnectionStrings__Fur02VerificationDatabase`. The four opt-in FUR-02 PostgreSQL tests skip only when that variable is absent; if inherited, a guarded full-suite run can migrate/write the matching local database. The current Mastermind shell has no such variable, but the guard must neutralize it in the child for future safe runs. Add that exact key to the child removal and fail-closed assertion without changing the explicit opt-in PostgreSQL proof path.
5. The new request-cancellation test cancels its token *before* calling the handler, so EF may throw during its first query and the test need not exercise cancellation at the provider send boundary or the new catch filter. Add a fake sender that confirms it was invoked, cancels the passed request token while sending, then throws `OperationCanceledException`; assert propagation and unchanged state/zero saves. Keep the existing pre-cancel test if useful.

The [second bounded correction brief](../worker-prompts/fur-02-review-safety-corrections.md) is for the same worker. FUR-02 remains In progress; no commit, push or deployment is authorized.

## Final local review of S1–S2

The gate script now removes `ConnectionStrings__Fur02VerificationDatabase` from the actual test-child environment and checks its absence before starting `dotnet`. The mid-send fake sender is entered, cancels the passed request token and throws; the test asserts propagation, unchanged Pending state, no replacement and zero saves. The earlier live-token provider-cancellation test still expects 502. No persistence or migration code changed after the 4/4 disposable-PostgreSQL proof.

Worker-reported final gates: strict build exit 0, zero warnings/errors; focused resend 19/0; guarded full unit 3458/0/12 skipped, including four FUR-02 PostgreSQL skips under an inherited sentinel; guarded architecture 1/1; `git diff --check` exit 0. Mastermind independently inspected the source and documentation, ran the strict solution build successfully with zero warnings/errors, parsed the Postman collection successfully and obtained `git diff --check` exit 0. A Mastermind guarded full-unit attempt aborted before test execution when `vstest` could not connect to `testhost` after 90 seconds; one normal-access attempt was stopped after remaining stalled without counts. Neither is presented as a code-test failure or passing test evidence. The worker's successful guarded run is the valid full-suite evidence, with this independent rerun limitation disclosed.

Disposition: **accept the complete local FUR-02 implementation and documentation for release preparation**. Do not mark Done until the separate owner-authorized commit/push and publish/deploy are completed and verified. Production migration `20261016133316_AddVerificationRequestVersion` remains unapplied; release preflight must verify exact target, backup/rollback, existing configuration, migration heads and safe provider posture before any publication or deployment.

# Worker workflow — team reports and refund policy

Use with the [active execution brief](team-reports-refund-policy-2026-10-22.md). This file contains reusable dispatch instructions, not a second task ledger.

## Start gate

The Owner requested plan files and explicitly told the Mastermind to wait for a signal before workers. The subsequent revision authorizes a plan with up to four concurrent workers, not immediate dispatch. Do not dispatch from the existence of these files. S0 is the first stage, split into four read-only investigations. Subsequent lanes need exact disjoint manifests and accepted dependencies.

The Owner subsequently approved all 30 recommendations recorded in the refund contract. These are locked, not open questions. Any earlier request below to identify decisions means only genuinely new material conflicts uncovered during source discovery. S0 maps approved behavior into bounded implementation manifests; routine technical design is reviewed by the Mastermind. Approval did not itself dispatch a worker or authorize release.

## Models and sessions

- Selected implementer: `river/deepseek-ai/DeepSeek-V4.1-Flash`, high effort.
- Selected independent reviewer: `river/zai-org/GLM-5.3-Flash`, high effort.
- **Owner-directed mix (2026-10-22): upcoming workers alternate between DeepSeek V4.1 Flash and GLM 5.3 Flash.** Assign implementation lanes alternately (lane 1 DeepSeek, lane 2 GLM, …); every reviewer is a fresh session of the model family OTHER than its implementer's. Verify available model IDs at each dispatch; never silently substitute.
- Verify currently available exact model references/variants at dispatch. Do not silently substitute if unavailable.
- Up to four concurrent native child sessions, including implementers and reviewers. Each implementation lane is owned by one Worker; each review uses a fresh independent session. Record session IDs/effective models in the active brief. No polling.
- A completed lane may enter review while independent lanes continue, provided its source and dependencies are frozen. Reviewer findings go to the same implementer. Do not count an independent review as optional to increase throughput.
- Same implementer handles bounded corrections. Never use its session for review. Follow governance after two unsuccessful correction rounds rather than looping indefinitely.

## Parallel waves and dependencies

The stage IDs S0–S8 still own scope in the brief. Lane IDs below divide that scope for concurrency; they are not additional product scope. Default to four ready lanes, not four workers regardless of conflicts. Start a downstream lane as soon as its specific dependencies are accepted; a whole-wave barrier is required only for shared verification/foundation changes.

| Wave | Concurrent lanes (maximum four) | Entry/dependency |
|---|---|---|
| W0: source/design | D1: M2 Paymob + subscription source map; D2: care/care-home/package/refund-version and ledger design; D3: contacts/OpenAPI/frontend smaller requests; D4: avatars/grants/discovery/media + rental capability inventory | Owner start signal. Source read-only; each returns its evidence in its own session. Mastermind combines S0 findings and resolves cross-lane contracts. |
| W1: independent fixes | A: S1 contact configuration/permissions; B: S2 proven Paymob defect; C: S3 caregiver/community identity, discovery and grant-aware reads; D: S1 contract/docs corrections plus isolated S3 CMS restore/validation/landing work | Each lane's S0 mapping accepted and exact file list disjoint. A and D cannot share HelpCenter controllers, tests, docs or Cms snapshot. B owns Paymob transport while active. |
| W2: financial foundation + remaining reads | F0: S4 shared policy versioning/money/refund-ledger interfaces and persistence foundation; R1: remaining S3 Draft finance/notification/orphan/gateway-status work; R2: remaining media/library work; V0: S7 fixture/test-plan authoring without execution | Accepted S0 financial design; F0 waits for any overlapping W1 Paymob changes. Other lanes start only if they do not touch F0 files/interfaces. V0 writes only isolated verification files against pinned contracts. |
| W3: domain implementation | F1: S4 care and multi-session policy; F2: S5 care-home rules/terms/extensions; F3: S5 subscription cancellation/refunds/listing subscriptions; F4: S6 rentals and remaining policy operations | Shared F0 interfaces/schema accepted and frozen. Split app-credit/refund-destination/support operation ownership explicitly into F0 or one lane, never duplicate it across domains. Same DbContext migrations are serialized. |
| W4: verification and handoff | S7 financial/lifecycle/replay checks and S8 consumer handoff/content readiness | Required feature lanes accepted. Verification/runtime writes use the sole runtime slot and approved target/action list; independent docs review may overlap. |

W1 C/D and W2 R1/R2 contain multiple bounded subjects. Mastermind dispatches only the subjects fitting that lane's exact manifest; the lane can hand back and resume the next bounded subject. Do not hand one worker an unbounded module rewrite. Already completed subjects are not repeated in later waves.

## Collision controls on shared main

- Work on main as required by repository governance; no automatic worktrees or worker commits. Preserve the pinned initial local delta and all unrelated edits.
- Before every overlapping dispatch, Mastermind records exact owned source/test/docs/Postman files and dependencies in the brief's reservation table. No shared write ownership, even for different sections of one file. Directory labels in the wave table are planning candidates, not sufficient reservations.
- Shared files have one owner: authorization constants/registration, dependency injection, ProblemDetails mapper, OpenAPI transformers, shared DTOs/interfaces, project files, Postman collections and test helpers. Queue each consumer's requested change with that owner. A file transfers only after its previous owner hands back and the Mastermind accepts the transfer.
- One migration/snapshot author per DbContext at a time. Assign predecessor/order centrally; two lanes using Families storage cannot independently generate competing snapshots. They may develop disjoint domain files concurrently, then take sequential persistence slots. Different contexts still require coordinated migration naming and one final manifest check.
- The active brief, refund contract and Mastermind handoff are Mastermind-owned during parallel execution. Workers return evidence in their session; Mastermind records it. This avoids concurrent ledger edits and duplicate status reports.
- Dependency changes freeze affected consumers until reconciled. A lane may not read a moving shared contract as if it were accepted. Split source work from dependent wiring if necessary.
- One build/test/runtime slot. Before a gate, pause all writes affecting compiled projects, referenced source, shared output directories and the reviewed dependency set; checkpoint the relevant diff, run the gate, record its input revision/local delta, then release the pause. Dotnet commands must not race with ongoing solution edits or other dotnet commands.
- Independent workers can keep analysing or authoring non-conflicting docs while gates run. Batch one strict build and focused tests over a frozen integrated wave when practical; attribute coverage to exact lanes. Rerun only checks invalidated by subsequent changes.
- Relational financial tests use verified disposable targets. Shared test-host fixture writes, real gateway operations, configuration changes and migration application remain separately authorized. Parallelism grants none of these permissions.
- A fourth lane runs only when truly independent; otherwise use three or fewer. Never prioritize utilization over correctness.

## S0 lane dispatch packet (ready after Owner signal)

> Read repository governance, Mastermind handoff, the active execution brief and refund contract. Execute only S0 lane [D1/D2/D3/D4] with the exact topics supplied by Mastermind. Baseline `4a066b341487c72dc3c56089e42b756eb470a763`; inspect current git status/diff and preserve unrelated work. This lane is fully read-only; return evidence in your session, not shared files. No source/docs/test/config/database edits, git writes, remote secret reads, gateway calls, migrations, publication or data repair. Reconcile your assigned report items against source and the 30 locked decisions. Return source-backed findings, precise next-stage file ownership, shared-file dependencies and focused acceptance checks. Financial lanes map every assigned policy clause and propose bounded architecture. Distinguish verified facts, team observations and hypotheses. Do not reopen settled decisions, weaken privacy/grants or guess new money policy. OTP and products excluded; rentals/listing subscriptions included. Hand back for independent review and Mastermind synthesis; do not start implementation.

## Implementation-stage dispatch template

Mastermind first replaces the lane's candidate ownership with exact files and acceptance in the brief reservation table and confirms no collision with active lanes. Pin current HEAD and local accepted delta; do not assume HEAD alone contains prior accepted uncommitted stages. Record predecessor lanes, shared-interface versions and any queued persistence/wiring slots.

> Execute only stage [ID], lane [ID], using its frozen disjoint file manifest and approved design. Own implementation, meaningful focused tests and reserved consumer docs/Postman. Request the build/runtime slot before commands; do not build/test against another lane's changing source. Queue shared-file edits with their owner; no out-of-manifest writes. Preserve unrelated changes. Report genuinely new material product ambiguity before guessing. No migration application, credential/environment reads, unapproved database mutation, external charges/refunds, git writes or deployment. Return exact changed files, commands/exit codes/test counts (mark unrun gates NOT RUN), migration predecessor, limitations, shared-file requests and consumer contract changes. Report ready_for_review in your session; Mastermind alone updates the shared ledger. Do not proceed to another lane/stage without direction.

## Independent review packet

> Review stage [ID] in a fresh session against the approved contract, frozen manifest and actual diff. Read-only; do not fix code. Confirm evidence is authored versus executed, permissions/privacy remain bounded, null/error/schema contracts are consistent, and financial amounts/versioning/idempotency/ledger behavior match the approved rules. Inspect real relational transaction semantics where applicable. Do not approve financial work with guessed policy choices or simulation described as live provider proof. Return ACCEPT or concrete findings with severity, path/line, violated requirement and smallest fix. Do not rerun already valid gates absent a specific concern; justify any additional gate.

## Mastermind integration and stop points

1. Owner signal → dispatch W0 lanes D1–D4; record worker/model/session and topic reservations. Read-only source discovery can run concurrently immediately.
2. Fresh review of completed evidence packets within the four-session cap → bounded corrections → Mastermind synthesis of S0 and exact file reservations.
3. Present confirmed financial differences and bounded architecture/manifests against the locked decisions. Escalate only newly discovered material conflicts before dependent implementation; do not re-ask approved choices.
4. Dispatch ready independent lanes according to the wave/dependency table, up to four sessions. Review frozen lane diffs independently; integrate at shared-contract and verification checkpoints before dependent lanes start.
5. Execute authorized fixture/replay verification only on the pinned target and action list; retain explicit unverified items.
6. Present accepted implementation and remaining Content dependencies to Owner for release authorization.
7. On separate authorization, release per runbook and record evidence; send reconciled replies to Owner for the teams.

## Evidence schema for each stage

`Stage/lane | Status | Dependencies | Worker session/model | Reviewer session/model | Baseline + local delta | Exclusive owned/changed files | Shared-file/migration reservations | Behavior evidence | Gate slot/commands/results | Target evidence | Findings/corrections | Limitations | Next action`

Statuses: pending → in_progress → ready_for_review → completed (Mastermind only). A blocked item carries its concrete dependency, not an invented success. No worker is in progress during planning.

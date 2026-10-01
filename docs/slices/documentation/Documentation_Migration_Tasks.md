# Documentation migration tasks

Status: Historical migration evidence; cleanup completed 2026-10-02. Owner withdrew the whole-project task expansion: plan Care homes only after application intake and owner discussion. This record preserves migration evidence, not authority to resume the former 103-file rollout. See `docs/Mastermind_Handoff.md` and `docs/care-homes/Care_Homes_Tasks.md` for current work. Everything below is historical, including unfinished-role/next-action instructions; do not resume them. The confirmed 118-file legacy cleanup was completed after replacement and link verification; retained current evidence and modified files were excluded.

## Scope and acceptance

Move handoff and task management under `docs`; preserve source evidence, unrelated changes, worker order, and original phase identities. Product priority is Care homes → Chat → Notifications → full application UI reconciliation → remaining roadmap. Family and Elderly inputs are supplied; Caregiver UI is outstanding. This migration does not authorize product implementation, deployment, database changes, commits, or deletion of legacy sources.

## Checklist

- [x] MIG-01 — Done — Extract legacy goals, phases, slices, decisions, and gaps; blueprint approved. Evidence: read-only scout and owner review in this conversation.
- [x] MIG-02 — Done — Delta scout verified HEAD `6089e8171ea51f6e85d0751f4aa4c299dacaa6e7`, existing changes, three repository-root calculations, and checker-test references.
- [x] MIG-03 — Done — Approved governance installed; four scripts copied to `docs/tools`, root calculations and test/deployment references updated. Originals retained. Implementer report and passing relocation checks verify the change.
- [x] MIG-04 — Done — Existing checker tests passed at the new path; four scripts parse successfully. Relocated mapping checker exited 0: 312 controller signatures, 352 Postman requests, 993 Bruno requests; zero missing/orphan mappings. Test-author role: no new API/unit/Bruno tests apply because no endpoint changed. Initial direct invocation was blocked by local script execution policy; process-scoped `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` ran the harness successfully without changing machine policy.
- [x] MIG-05 — Done — One review completed. Findings: inherited fixture admin-seed environment restoration gap and a legacy checker command reference. Mastermind corrected the new helper's restoration list and updated the matrix command; targeted restoration verification remains in MIG-07. Original helper is unchanged.
- [ ] MIG-06 — In progress — Documenter creates all indexed slice files and carries forward feature-specific decisions/evidence. Mastermind owns main goal, handoff, and source-to-destination mapping; original execution phase identities are retained.
- [ ] MIG-07 — Not started — Mastermind verifies manifests, checklists, links, source coverage, and required script checks; resolves concrete findings.
- [x] MIG-08 — Done — Deleted only the confirmed 118-file legacy manifest after replacement/link verification and owner confirmation; current evidence and modified files were excluded.

## Worker sequence

Scout → implementer → test author → reviewer → documenter. One worker at a time. Test author has no new API/unit/Bruno authoring work; existing script checks remain required. The mastermind owns final verification and handoff. Resume at the unfinished role; no duplicate review round.

## Evidence and next action

Existing changes on entry: Elderly audit, local fixture helper, VPS Bruno environment, private handoffs, UI assets, VAT briefs, and protected image fixtures. Read-only legacy extraction is complete; do not reread private history. Current role: documenter. Next action: finish feature-specific slice checklists, then mastermind validates the full manifest and links.

## Source-to-destination manifest

These mappings describe the migration of actionable rules, requirements, decisions, and relevant evidence. Superseded commands, credentials, and repetitive transcripts are not copied into public docs. Legacy files remain available until separate cleanup approval.

| Source | Destination / disposition |
|---|---|
| `AGENTS.md` | Root discovery pointer retained; substantive rules in `docs/governance/AGENTS.md`, approved additions in place |
| `docs/operations/codex-workflow.md` | Retained and aligned with governance; unchanged worker order |
| `Sanad_Master_Context.md` | Sanitized product rules in main goal and owning foundations/subscription/domain slices; private environment details are not published |
| `Sanad_Operations.md` | Current state in Mastermind handoff; dated completion/decision evidence in owning slices; superseded run histories are retired |
| `docs/goal-progress.md` | Owning slice checklists/evidence; original Elderly goal phase identities in main goal |
| `docs/next-goal-tasks.md` | Notifications/domain slices; original three notification phases in main goal; latest owner priority supersedes old next-goal pointer |
| `docs/operations/application-roadmap.md` | Main goal phase index and every phase-00 through phase-10 slice |
| `docs/operations/current-phase-todo.md` | Subscription slices, release readiness, and historical phase metadata; old release workflow is not reinstated |
| `docs/operations/family-ui-audit.md` | Family UI Audit plus owning foundation/domain/Library/Community/phase-04 slices; preserve supplied UI and explicit deferrals |
| `docs/operations/elderly-ui-audit.md` | Elderly UI Audit plus phase-02/03/06 slices; preserve existing dirty audit input and reconcile approved decisions |
| `docs/operations/endpoint-coverage-matrix.md` | API Contract Coverage and feature-local route/authorization/evidence references |
| `docs/operations/endpoint-coverage-tracker.md` | Feature-local historical runtime evidence and API Contract Coverage closeout; distinguish owner-confirmed provider checks |
| Seven `subscription-vat-tax/*.txt` briefs | Subscription tax contract preserved in `docs/slices/subscriptions/Subscription_Tax_Reference.md`; worker routing and correction notes are retired |
| Four `tools/*.ps1` sources | Current contents copied to `docs/tools` with repository-root adjustments and the reviewed five-variable environment restoration fix in the retained fixture helper; duplicate originals were removed after verification |
| Configuration, deployment, migrations, security, test-data guides | Retained under `docs/operations`; deployment references updated; instructions are reference material, not task queues |
| Public API guides, Postman, tests, UI and private fixtures | Retained; no endpoint contract change and no unnecessary collection rewrite |

## Exact cleanup completed — 2026-10-02

All paths below were relative to `D:/Sanad_API` and were permanently deleted individually after replacement verification and separate owner confirmation. No directory-wide deletion occurred; no UI, tests, fixtures, runtime configuration, or generated `tools/bin` / `tools/obj` files were included.

1. `Sanad_Master_Context.md`
2. `Sanad_Operations.md`
3. `subscription-vat-tax/worker-bruno-order-correction.txt`
4. `subscription-vat-tax/worker-tax-concurrency-test-retry.txt`
5. `subscription-vat-tax/worker-tax-concurrency-test.txt`
6. `subscription-vat-tax/worker-vat-tax-correction-01.txt`
7. `subscription-vat-tax/worker-vat-tax-implementation.txt`
8. `subscription-vat-tax/worker-vat-tax-test-correction-01.txt`
9. `subscription-vat-tax/worker-vat-tax-tests.txt`
10. `docs/goal-progress.md`
11. `docs/next-goal-tasks.md`
12. `docs/operations/current-phase-todo.md`
13. `docs/operations/application-roadmap.md`
14. `docs/operations/family-ui-audit.md`
15. `docs/operations/elderly-ui-audit.md`
16. `docs/operations/endpoint-coverage-matrix.md`
17. `docs/operations/endpoint-coverage-tracker.md`
18. `tools/Publish-SanadApi.ps1`
19. `tools/Start-LocalBrunoFixtureApi.ps1`
20. `tools/Verify-ApiContractMapping.ps1`
21. `tools/Verify-SanadDeployment.ps1`

The private handoffs and VAT briefs were untracked and are not recoverable through Git. The Elderly audit and fixture helper contained pre-existing local edits and were excluded from deletion. The separate cleanup proposal records the complete 118-file manifest and replacements.

## Verification evidence — 2026-10-01

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Verify-ApiContractMapping.Tests.ps1`: exit 0, focused checker tests passed using `docs/tools`.
- PowerShell parser on four `docs/tools/*.ps1` files: zero parse errors.
- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/tools/Verify-ApiContractMapping.ps1`: exit 0, 312 controller route-method signatures / 352 Postman requests / 993 Bruno requests; all missing/orphan counts zero. This checks static files only.
- Fixture cleanup regression: extracted only the helper's environment-name assignment and actual capture/restore loops through the PowerShell AST; asserted every literal assigned environment key is tracked, then verified all tracked keys with both pre-set sentinel values and absent initial values. Exit 0. Original child-process environment restored in finally; API launch, file writes, migrations, and seeding were not executed. Added five `Identity__AdminSeed__*` keys only to the new copy.
- The unchanged mapping checker gate remains valid after the fixture-only correction. No .NET or endpoint behavior changed, so full .NET/Bruno runtime runs are inapplicable for this migration.

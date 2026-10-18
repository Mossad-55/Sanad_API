# FUR-10 whole-phase Worker brief — Cross-phase integration and release handoff

## Authority and pinned baseline

The Owner approved the FUR-10 contract on 2026-10-18:
`docs/internal/work/portal-api-reconciliation/contracts/fur-10-cross-phase-integration-handoff.md`.
Pinned `main` HEAD: `ef50f690338b3a2ae68b697fc80fc1e2488e560c` (FUR-09 local API
evidence commit). The worktree has unrelated modified/untracked files, including
pre-existing edits in `docs/internal/Project_Main_Goal.md`. Preserve them; do not
reset, clean, stage, or overwrite unrelated changes.
The Owner selected OpenCode Zen model `opencode/longcat-2.5-preview-free`.
Verify that exact effective Worker model before dispatch; do not switch routes
automatically.
Work directly in the current `main` checkout; do not create/switch branches or
worktrees, or launch parallel phase work. E01–E08 have no approved contracts.

Read and follow `AGENTS.md`, `docs/internal/governance/AGENTS.md`,
`docs/internal/Mastermind_Handoff.md`,
`docs/internal/work/portal-api-reconciliation/plan.md`, the complete FUR-10
contract, `docs/internal/Project_Main_Goal.md`,
`docs/internal/operations/codex-workflow.md`, and
`.opencode/agents/sanad-worker.md`. Inspect current status/diff before editing.

## Objective

Complete only the documentation-only FUR-10 handoff/integration scope in the
contract. Read the phase contracts/reports/receipts for factual content; do not
repeat builds, tests, deployment verifications or database checks. Report exact
limitations and distinguish verified/deployed behavior from client adoption,
which is unknown because no frontend repository was inspected.

Create these dated audience reports, following the existing frontend-handoff
template and naming/audience conventions:

- `docs/frontend-handoffs/admin/care-home-owner/phase-05-owner-settlements/2026-10-18-owner-settlements.md`
- `docs/frontend-handoffs/admin/admin-portal/phase-06-caregiver-eligible-payouts/2026-10-18-caregiver-eligible-payouts.md`
- `docs/frontend-handoffs/admin/admin-portal/phase-07-community-moderation/2026-10-18-community-moderation.md`
- `docs/frontend-handoffs/app/shared/phase-07-community-report-intake/2026-10-18-community-report-intake.md`
- `docs/frontend-handoffs/admin/care-home-owner/phase-08-owner-disputes/2026-10-18-owner-disputes.md`
- `docs/frontend-handoffs/admin/admin-portal/phase-09-caregiver-integrity-disposition/2026-10-18-caregiver-integrity-disposition.md`

Update the frontend handoff index and current phase/status summary in
`docs/internal/Project_Main_Goal.md` within the approved contract. Preserve the
pre-existing unrelated Project_Main_Goal delta: only change its current phase
status and obsolete Codex/GPT-6 workflow sentence. Do not edit the plan or
Mastermind handoff; those are Mastermind-owned.

Create `docs/internal/work/portal-api-reconciliation/reports/fur-10-integration-release-handoff.md`
with a concise FUR-00–FUR-09 status/release/config/migration ledger, links,
limitations and explicit FUR-10 deployment N/A. Reconcile receipts; do not infer
target state beyond documented evidence. FUR-08 is deployed `14757cc`; FUR-09 is
local-only and its six Production rows remain unverified. E01–E08 are still
uncontracted/Not started; do not start them.

## Owned files

- Six new dated handoffs listed above.
- `docs/frontend-handoffs/README.md`.
- `docs/internal/Project_Main_Goal.md` (the precise limited edits above only).
- `docs/internal/work/portal-api-reconciliation/reports/fur-10-integration-release-handoff.md`.

Do not modify existing dated FUR-00/FUR-04 audience reports; link to them and
the newer handoffs instead. Do not modify API/source/tests/Postman/diagnostics,
phase contracts, governance, release receipts, or unrelated worktree changes.
If an additional material file is needed, stop and report the exact reason.

## Verification and stop conditions

- Validate every new/changed relative Markdown link and cross-check every phase
  status/commit/migration/config claim against its existing receipt/report.
- Run `git diff --check` on owned files. No .NET build/test, package, live API,
  Production/database access, provider, Bruno, commit, push, or publish.
- Never claim frontend code inspection, client adoption, or user forwarding.
  Preserve all historical evidence/limitations, especially the unverified FUR-09
  Production rows.
- Inspect file content before editing; avoid no-op/repeated edits. If the same
  unchanged validation failure occurs twice, stop and return BLOCKED with exact
  evidence. Otherwise return one concise COMPLETE/BLOCKED report and stop.

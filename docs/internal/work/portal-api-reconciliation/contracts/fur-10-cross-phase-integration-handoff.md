# FUR-10 contract — Cross-phase integration and release handoff

Approved by the Owner on 2026-10-18.

## Goal

Close the selected FUR-00–FUR-09 sequence with an accurate internal status,
release/configuration/migration ledger, and owner-forwardable audience handoffs.
FUR-10 is documentation-only; it does not claim the entire 19-phase roadmap is
complete.

## Scope

1. Reconcile current status and evidence for FUR-00 through FUR-09 using the
   canonical phase plan, approved contracts, worker/review reports and release
   receipts. Distinguish deployed phases from verification-only/docs-only work.
2. Produce a cross-phase ledger of release revisions, migration additions and
   applied outcomes, configuration deltas/fingerprint evidence, and explicit
   verification limitations. Derive conclusions from existing receipts and
   package manifests; do not query Production or databases again.
3. Add dated, audience-specific follow-up reports for phase outputs not yet
   indexed:
   - FUR-05 owner settlements — Admin / Care Home Owner.
   - FUR-06 eligible caregiver payout selector — Admin / Admin portal.
   - FUR-07 community moderation — Admin / Admin portal, and community report
     intake — App / shared Family-Caregiver-Elderly.
   - FUR-08 owner dispute readback — Admin / Care Home Owner.
   - FUR-09 caregiver integrity disposition — Admin / Admin portal.
4. Update `docs/frontend-handoffs/README.md` to link those reports and correct
   its stale current-status summary for FUR-04 and later phases. Treat the dated
   FUR-00/FUR-04 reports as historical snapshots: add new follow-ups and pointers;
   do not rewrite earlier observations as if they were made after release.
5. Update the current-slice/status summary in `docs/internal/Project_Main_Goal.md`
   and replace the obsolete Codex/GPT-6 worker workflow sentence with a pointer
   to current OpenCode governance. Preserve its existing unrelated local edits;
   change only the current phase/status and obsolete workflow text.
6. Create `docs/internal/work/portal-api-reconciliation/reports/fur-10-integration-release-handoff.md`
   as the final internal cross-phase closeout.

## Required truth and exclusions

- FUR-09 is done for its approved local synthetic API test/report scope only.
  The six reported Production caregiver rows and their cause remain unverified;
  no repair/quarantine is recommended or authorized.
- FUR-08 was deployed at `14757cc`; its release receipt records 9/9 heads matched,
  zero migrations applied, service active/NRestarts=0, and no live authenticated
  feature request/Bruno run.
- FUR-01–FUR-08 release status, migrations and configuration must be reconciled
  from their existing receipts. Do not repeat deployment, target or database
  checks, or invent a configuration/migration delta.
- E01–E08 remain Not started and individually uncontracted. FUR-10 does not
  approve or begin them. FUR-09's unresolved Production evidence remains an
  explicit external/open item, not a reason to claim data recovery.
- No source/API/test/Postman changes, production access, database/provider call,
  migration, package, deployment, commit or push are in the Worker contract.
- No frontend repository inspection or external report sending is authorized or
  claimed. Do not claim clients adopted, exercised, or approved any handoff.
- Do not delete historical reports or replace their dated evidence. Do not add
  new product contracts for E01–E08 or repair/quarantine.

## Acceptance

- All FUR-00–FUR-09 statuses and report/receipt links in the handoff index are
  accurate; existing dated reports remain intact.
- The six new audience reports trace the relevant original issue(s) to the
  approved contract, verified API behavior/source/test evidence, and deployment
  truth or explicit N/A/limitation. Audience mapping and wording match the
  Admin = Admin portal + Care Home Owner; App = Family/Caregiver/Elderly boundary.
- The final ledger accounts for runtime/configuration/migration evidence using
  the existing phase receipts, with no unsupported claim of complete production
  readiness or full-roadmap completion.
- E01–E08 remain clearly Not started/uncontracted; FUR-09 Production diagnosis
  remains unverified; FUR-10 is identified as documentation-only with deployment
  N/A.
- Validate all newly added/changed relative Markdown links and perform relevant
  documentation/consistency checks. No .NET build/test or live checks are needed.

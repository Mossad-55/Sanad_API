# Sanad API Mastermind Handoff (compact)

Active slice: none; E04 is released and verified.
Current checkpoint: 2026-10-19 E04 platform controls was committed, pushed, deployed and verified at revision `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9`. The E04 Finance migration was applied by the authorized deployment. E05/E06 scopes are approved under the Owner's direction and remain queued.

## Current state

- Worktree has many pre-existing modified/untracked docs, agent configs, tooling. Preserve unrelated edits.
- Cleanup Worker `ses_eadad29d5ffeuKB3nq1mCsdhzv` is terminal. Mastermind completed consolidation and the approved deletions after two correction rounds. Retained historical references require no routine updates; README cleanup remains deferred.
- [E04 brief](work/portal-api-reconciliation/worker-prompts/e04-platform-controls.md) owns the approved scope, task evidence and release state; the [E04 release receipt](work/portal-api-reconciliation/reports/e04-platform-controls-release.md) records the deployed revision, migration and verification. E04 is `completed` and released. [Governance](governance/AGENTS.md#phase-lifecycle-and-document-ownership) defines the lifecycle.
- E05 Family-home CMS and E06 daily wellness missed-check-in feed are **approved scope, queued/not started** in their [E05 brief](work/portal-api-reconciliation/contracts/e05-family-home-cms-proposal.md) and [E06 brief](work/portal-api-reconciliation/contracts/e06-missed-check-ins-proposal.md). Implementation/dispatch and release remain separate Owner gates. E07 caregiver review notes and E08 landing-media lifecycle remain uncontracted.
- FUR-01..FUR-10 Done; FUR-03, FUR-09, FUR-10 had deployment N/A. E01-E03 Done.
- Geography preserve-and-add complete: 27 governorates / 349 cities / 5714 areas; all original lookup records and five selections unchanged.
- Latest release: [E04 platform controls](work/portal-api-reconciliation/reports/e04-platform-controls-release.md) deployed/verified at revision `bdc06057caa2c4b4589565c8c7c456ea57bdf8e9`; Finance applied one migration, the other eight contexts applied zero.

## Unresolved issues / open actions

- Caregiver production-data verification: six reported Identity-mismatched rows remain unverified; no repair authorized.
- Chat outstanding checks: Android/physical-iPhone push; live session/socket invalidation and final-send privacy; edge TLS/WebSocket/redaction/limits; cleanup sweep/permissions/backlog and soak; live large-gap/receipt-only recovery and recipient dispatch. Automated or earlier builder evidence does not establish these live checks.
- Chat attribution verification: five paid/completed Hourly and five EightHour fixtures and API-versus-SQL comparison on an authorized disposable path. Voice remains excluded; single-instance scope remains.
- Platform controls (E04) is released: the control row is seeded in Production and the anonymous public status route is live. The SuperAdmin control routes, the maintenance path and the disabled-method path have automated evidence only; no authenticated Production request was made.
- Legacy route-mapping helper does not compute its missing/orphan sets. E04's brief requires explicit route-set comparison; do not treat the helper's exit 0 as coverage evidence.

## Next action

E04 is released and verified. Next: E05 Family-home CMS, then E06 daily wellness missed-check-ins. Each needs its own Worker model selection, dispatch direction and separate release authorization. E07 and E08 remain uncontracted.

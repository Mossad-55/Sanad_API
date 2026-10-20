# Sanad API Mastermind Handoff (compact)

Active slice: F5 (support inbox, includes the batch's first schema migration) + F6 (operational reads) implemented in parallel — coordinated directly by Mastermind.
Current checkpoint: 2026-10-20 F2 (`24ad602909b2f1f3ac9ecf6ca68332994a89be94`), F3 (`607d33fcb651bed25239050989cb94a8cb36de41`) and F4 (`4d3cab488bd6a57447352d265c9955378f1b243c`) are completed, deployed and verified on Production, all with 9/9 heads and zero migrations and an active service; receipts `ea3c3bb6b50b58b39bbea954767621d70029e763`, `3dba7b98357e8a72e07bdc1d580d4032bdb4e5e3` and `6839c48ce36f10b6e9773c196c2df15bf40bfd29` are pushed. Authenticated Production feature requests were not run for any of them. F5 (`ses_ea54fa5c4ffe0b9r0doie4Edu1`, Beam/high) owns the runtime slot and is the first phase in this batch to need a schema change (one additive Identity migration: ticket replies plus status-change actor/time columns); F6 (`ses_ea54f7a0bffeu65ZvoEt3nXvbr`, River/high) is read-only with no schema and stops at implementation handback until F5 releases the slot. Mastermind pre-added and committed the `SupportInboxAdmin` and `OperationalReadAdmin` policies (SuperAdmin+SupportAdmin) so neither phase depends on the other's edit. Luna unavailable due usage quota; Mastermind reviews directly or uses authorized fallback. Preserve unrelated edits; unanswered additions remain excluded.

## Current state

- Worktree has many pre-existing modified/untracked docs, agent configs, tooling. Preserve unrelated edits.
- Cleanup Worker `ses_eadad29d5ffeuKB3nq1mCsdhzv` is terminal. Mastermind completed consolidation and the approved deletions after two correction rounds. Retained historical references require no routine updates; README cleanup remains deferred.
- [E04 brief](work/portal-api-reconciliation/worker-prompts/e04-platform-controls.md) owns the approved scope, task evidence and release state; the [E04 release receipt](work/portal-api-reconciliation/reports/e04-platform-controls-release.md) records the deployed revision, migration and verification. E04 is `completed` and released. [Governance](governance/AGENTS.md#phase-lifecycle-and-document-ownership) defines the lifecycle.
- E05/E06 retain their approved behavior and remain queued; approved follow-up fixes take priority. E07/E08 now have bounded minimum scope in the follow-up contract and need active briefs before dispatch. All four are requested; none is removed. OTP is explicitly excluded. Unanswered frontend additions remain outside scope.
- FUR-01..FUR-10 Done; FUR-03, FUR-09, FUR-10 had deployment N/A. E01-E03 Done.
- Geography preserve-and-add complete: 27 governorates / 349 cities / 5714 areas; all original lookup records and five selections unchanged.
- Latest release: [F3 facility revision carry-forward](work/portal-api-reconciliation/worker-prompts/f3-facility-revision-carry-forward.md), deployed revision `607d33fcb651bed25239050989cb94a8cb36de41`; zero migrations across all nine contexts.

- F5/F6 briefs are prepared and queued for the next parallel wave: [F5 help/support inbox](work/portal-api-reconciliation/worker-prompts/f5-help-support-inbox.md) (Beam/high) and [F6 operational reads](work/portal-api-reconciliation/worker-prompts/f6-operational-reads.md) (River/high). F5 changes the Help Center owner audience, adds the ticket status/reply workflow and is the **first phase in this batch to need a schema change** (one additive Identity migration for replies plus status-change columns). F6 is read-only and adds no schema. Shared `AuthorizationPolicies.cs`/`DependencyInjection.cs` and `docs/postman/admins/Sanad.Admin.postman_collection.json` are F5-owned; F6 holds at implementation handback until F5's shared edit lands and F5 releases the runtime slot.

## Unresolved issues / open actions

- Caregiver production-data verification: six reported Identity-mismatched rows remain unverified; no repair authorized.
- Chat outstanding checks: Android/physical-iPhone push; live session/socket invalidation and final-send privacy; edge TLS/WebSocket/redaction/limits; cleanup sweep/permissions/backlog and soak; live large-gap/receipt-only recovery and recipient dispatch. Automated or earlier builder evidence does not establish these live checks.
- Chat attribution verification: five paid/completed Hourly and five EightHour fixtures and API-versus-SQL comparison on an authorized disposable path. Voice remains excluded; single-instance scope remains.
- Platform controls (E04) is released: the control row is seeded in Production and the anonymous public status route is live. The SuperAdmin control routes, the maintenance path and the disabled-method path have automated evidence only; no authenticated Production request was made.
- Legacy route-mapping helper does not compute its missing/orphan sets. E04's brief requires explicit route-set comparison; do not treat the helper's exit 0 as coverage evidence.

## Next action

Await the F3 release receipt, then the F5 runtime handback (review the diff, the migration, the evidence), then resume F6 for its single strict build + focused tests + real HTTP proof, then delegate each phase's serialized release. After that: F7+E06, E05+F8, E08+E07. Act on notifications, no polling. No routine Owner approval pauses; escalate only unresolved product policy, destructive data changes, target/scope mismatch or real blockers. The F5 commit must carry the two Mastermind-prepared authorization files; the F5 brief and F3 acceptance note are uncommitted by design.

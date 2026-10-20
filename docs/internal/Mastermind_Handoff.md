# Sanad API Mastermind Handoff (compact)

Active slice: F3 facility revision carry-forward — bounded runtime verification handback under River/high, directly coordinated by Mastermind. F4 is released.
Current checkpoint: 2026-10-20 F2 (`24ad602909b2f1f3ac9ecf6ca68332994a89be94`) and F4 (`4d3cab488bd6a57447352d265c9955378f1b243c`) are both completed, deployed and verified on Production; receipts `ea3c3bb6b50b58b39bbea954767621d70029e763` and `6839c48ce36f10b6e9773c196c2df15bf40bfd29` are pushed, 9/9 heads and zero migrations each, service active. Authenticated Production feature requests were not run for either phase. F3 implementation was delivered by Beam/high (`ses_ea5b2f615ffe4jh73wEuMDrd0L`), corrected twice, then fixed directly by Mastermind (compile collision, media ordering assertion); its focused run is green (54 passed) and a River/high verifier (`ses_ea583e853ffeTIcovs3SHLpugj`) owns the remaining tests plus a small local-HTTP adapter. [F2 brief](work/portal-api-reconciliation/worker-prompts/f2-booking-chat-read-completeness.md), [F3 brief](work/portal-api-reconciliation/worker-prompts/f3-facility-revision-carry-forward.md) and [F4 brief](work/portal-api-reconciliation/worker-prompts/f4-community-restriction-reads.md) own their evidence. Luna unavailable due usage quota; Mastermind reviews directly or uses authorized fallback. Preserve unrelated edits and unanswered additions remain excluded.

## Current state

- Worktree has many pre-existing modified/untracked docs, agent configs, tooling. Preserve unrelated edits.
- Cleanup Worker `ses_eadad29d5ffeuKB3nq1mCsdhzv` is terminal. Mastermind completed consolidation and the approved deletions after two correction rounds. Retained historical references require no routine updates; README cleanup remains deferred.
- [E04 brief](work/portal-api-reconciliation/worker-prompts/e04-platform-controls.md) owns the approved scope, task evidence and release state; the [E04 release receipt](work/portal-api-reconciliation/reports/e04-platform-controls-release.md) records the deployed revision, migration and verification. E04 is `completed` and released. [Governance](governance/AGENTS.md#phase-lifecycle-and-document-ownership) defines the lifecycle.
- E05/E06 retain their approved behavior and remain queued; approved follow-up fixes take priority. E07/E08 now have bounded minimum scope in the follow-up contract and need active briefs before dispatch. All four are requested; none is removed. OTP is explicitly excluded. Unanswered frontend additions remain outside scope.
- FUR-01..FUR-10 Done; FUR-03, FUR-09, FUR-10 had deployment N/A. E01-E03 Done.
- Geography preserve-and-add complete: 27 governorates / 349 cities / 5714 areas; all original lookup records and five selections unchanged.
- Latest release: [F4 community restriction reads](work/portal-api-reconciliation/worker-prompts/f4-community-restriction-reads.md), deployed revision `4d3cab488bd6a57447352d265c9955378f1b243c`; zero migrations across all nine contexts.

## Unresolved issues / open actions

- Caregiver production-data verification: six reported Identity-mismatched rows remain unverified; no repair authorized.
- Chat outstanding checks: Android/physical-iPhone push; live session/socket invalidation and final-send privacy; edge TLS/WebSocket/redaction/limits; cleanup sweep/permissions/backlog and soak; live large-gap/receipt-only recovery and recipient dispatch. Automated or earlier builder evidence does not establish these live checks.
- Chat attribution verification: five paid/completed Hourly and five EightHour fixtures and API-versus-SQL comparison on an authorized disposable path. Voice remains excluded; single-instance scope remains.
- Platform controls (E04) is released: the control row is seeded in Production and the anonymous public status route is live. The SuperAdmin control routes, the maintenance path and the disabled-method path have automated evidence only; no authenticated Production request was made.
- Legacy route-mapping helper does not compute its missing/orphan sets. E04's brief requires explicit route-set comparison; do not treat the helper's exit 0 as coverage evidence.

## Next action

Await the F3 verifier handback (`ses_ea583e853ffeTIcovs3SHLpugj`): review the exact diff, authored tests, the small local-HTTP adapter and its real results; then delegate the F3-only release under existing authority. Next wave after F3 is released: F5+F6 (disjoint manifests), then F7+E06, then E05+F8, then E08+E07. Act on notifications, no polling. No routine Owner approval pauses; escalate only unresolved product policy, destructive data changes, target/scope mismatch or real blockers.

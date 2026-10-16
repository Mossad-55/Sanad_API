# FUR-01 — phase deployment receipt

Date: 2026-10-16 (Africa/Cairo).
**Status: release pending; no FUR-01 deployment claimed yet.**

Standing owner order: after a complete phase passes Mastermind review and all
required gates, commit/push, publish/deploy and verify before advancing.
Local acceptance: [final review appendix](fur-01-mastermind-review.md).

Target preflight verified on the owner-designated existing VPS via SSH:
`http://72.62.92.144:8091`, service `sanad-api.service`, Production revision
`9167e7111c5321b063fa9d893001e77dd42a32da`, service active, database reachable,
all nine migration heads match the required unchanged identifiers. This phase
adds only API metadata; no config, environment, CORS, storage or migration delta.

Local release gates: strict build zero warnings/errors, guarded full unit
3421 passed / 0 failed / 8 explained skips, architecture 1/1. Actual route-key
comparison zero missing/orphaned. Raw owner intake remains local-only.

The actual pushed revision, archive hash, server backups, deployment operation,
restart evidence and verified deployed OpenAPI will be recorded after execution.
No business/provider/authenticated live test is part of this deployment smoke.

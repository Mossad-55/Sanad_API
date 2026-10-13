# Chat Plus — integration handoff

## Current VPS-readiness integration

Prompt 5 worker delivered 3c9918d from 1120434; corrected implementation
64d372e is fast-forwarded into main without conflicts. Build zero warnings/
errors; VPS-readiness 17/17; full unit 3093 passed, 0 failed, 3 existing PG
skips; architecture 1/1; Postman-only mapping 556 signatures/620 requests,
zero gaps, exit 0. Evidence: [VPS integration](reports/vps-integration.md).

Owner explicitly keeps AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader unchanged;
this is intentional configuration, not a gap. No CORS or proxy-origin restriction
was introduced. Bruno is entirely excluded from subsequent work.

Staging/production environment verdict remains NOT READY: exact host/URL/service/
test DB, secrets, backup/restore, fresh package and actual verification are still
required. No package/VPS/database/provider action occurred in this integration.
Next worker: Prompt 1 from the updated clean main; mastermind integrates its result.

## Prior Chat Plus implementation integration — 2026-10-13

Owner authorized mastermind integration into main and documentation sync.
Builder branch: `builder/chat-plus-production`, base `41f9c91`, delivered
tip `ded4dd1`. Main/origin main were `41f9c91` at intake; tree clean.
Correction commit `9ca63b3` is integrated into main by conflict-free fast-forward.
Final code gates: build 0 warnings/errors; Chat Plus 64/64; full unit 3076
passed, 0 failed, 3 existing PostgreSQL skips; architecture 1/1. Postman
mapping 555 signatures, 0 missing/orphans; mapping script exits 1 because
148 optional Bruno mappings are absent (risk-based exemption documented).
Phases 0–6 and the disposable-target Phase 7 evidence are in [tasks.md](tasks.md)
and [reports/phase-7-release.md](reports/phase-7-release.md).

Integration review found and corrected actual runtime wiring gaps:
token expiry closure, active-account checks in hub calls/realtime audiences,
active-user/configured-options injection into the production dispatcher,
unimplemented-backplane flag bypass, and stale publish migration identifiers.
Final verification and merge/push evidence belong to
[reports/integration.md](reports/integration.md).

## Remaining release work

- Owner/mobile team: Firebase/APNs Android + physical iPhone proof.
- Backend verifier: five paid/completed attributed Hourly and five EightHour
  fixtures on an authorized disposable payment path; API-versus-SQL equality.
- Operator: actual edge TLS/WebSocket/redaction/limits validation. CORS stays
  AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader by owner confirmation, not a gap.
- Backend planner: JWTs are not bound to device sessions. Token expiry now
  closes hub connections and disabled users are denied on hub calls/dispatch;
  immediate server-side per-session logout/revocation remains unimplemented.
  Define session-bound token/auth invalidation in a separately bounded task.
- Backend planner: physical orphan cleanup worker and bounded storage proof
  remain open. Lazy expiry does not reclaim disk or prove quota sustainability.
- Runtime verifier: builder socket-kill proof recovered five missed messages;
  >100-message/receipt-only recovery is automated evidence, not live proof.
  Rerun live transport/auth checks for the integration corrections before release.
- Adjacent slice: recurring CareHomeReviewNotificationProcessor SQL translation
  failure observed by the builder; not modified by this integration.

Three migrations were applied/rollback-tested only on the builder-authorized
disposable DB. No production migration/deploy/provider send is implied.
Builder runtime counts are reported evidence, not rerun in this integration.
Voice excluded; single API instance only; do not claim production release-ready.

## Next action

Owner wants VPS development shipping prioritized and Bruno ignored entirely.
[Worker prompts](worker-prompts.md) are ready: Prompt 5 is integrated; start
Prompt 1 from updated main, then 2, 3, optional 4, deployment and 6.
Prepare a fresh package from clean pushed main at release time. Existing
publish-out manifest is 41f9c91 (pre-Chat-Plus), not current main.
Development VPS means isolated Staging/test database, not public Development.
The testing page (Prompt 4) is optional; direct developer testing/reporting is
acceptable. Deploy only after exact host/database/startup-side-effect approval;
security/storage and production-device gates retain their documented owners.

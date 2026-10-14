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

Prompt 1 session-security work is integrated in main at `2d8af5a` (owner
branch tip, fast-forwarded). The mastermind selected the Chat-first rollout:
legacy Normal tokens remain valid for non-Chat APIs until their 15-minute
expiry; Chat REST/SignalR require a live session-bound token. Mobile behavior:
on the first Chat 401 from a legacy token, refresh once and retry once; if
refresh/retry fails, require sign-in without looping. See
[session-security integration](reports/session-security-integration.md).

Prompt 2 storage cleanup is integrated at current main `8097edb` from worker
base `b46728a`, plus mastermind integration fixes. The hourly bounded sweep,
bind exclusion, physical quota accounting, sanitized backlog logging and
authored-only index migration are now implemented. Mastermind corrected the
missing production clock registration, validates interval/batch configuration,
and guards path resolution. Worker-reported PostgreSQL race tests used and
dropped the separate disposable `SanadChatCleanupTestDb`; they were not rerun
here because the configured integration target must not be reset implicitly.
Mastermind verification: strict build zero warnings/errors, Chat tests excluding
the PostgreSQL destructive fixture 238/238, cleanup tests 9/9, architecture 1/1.
No VPS, production DB, migration, or provider operation occurred. See
[storage integration](reports/storage-integration.md).

Prompt 3 (Care Homes processor translation fix) is integrated into main at
`a869bb0`; see [processor integration](reports/processor-integration.md). The
owner-lookup predicate now uses typed `UserId` comparison, proven by Npgsql SQL
generation; recipient mapping and focused suites pass. Integration also pins
transitive `System.Security.Cryptography.Pkcs` to patched `10.0.13`, clearing
the NU1902/NU1903 audit warnings without suppression. Prompt 6 (recipient-query hardening) is complete on current main; see
[processor integration](reports/processor-integration.md). Both proven `.Value`
predicates now use typed `UserId` comparison (`targets.Contains(x.Id)` and the
shared `RecipientUsers` composition for both push preference reads), covered by
Npgsql SQL-generation regressions; recipient selection/mapping and focused suites
pass. `CareHomeLicenseExpiryMonitor` has a `.Value` projection only, not a
confirmed predicate defect, and was deliberately untouched.

Staging/production environment verdict remains NOT READY: exact host/URL/service/
test DB, secrets, backup/restore, fresh release package and actual verification
are still required. No VPS/production-database/provider action occurred.

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
- Runtime verifier: session-bound token/auth invalidation and final-send push
  privacy have automated evidence, but no live API/DB/socket/device run has
  verified them. Prompt 7 owns runtime verification.
- Runtime verifier/operator: run the integrated cleanup against the authorized
  Staging filesystem, observe hourly sweep/permission behavior and backlog
  metrics, and complete a soak. The Chat index migration remains authored only
  until an explicitly authorized deployment applies it.
- Runtime verifier: builder socket-kill proof recovered five missed messages;
  >100-message/receipt-only recovery is automated evidence, not live proof.
  Rerun live transport/auth checks for the integration corrections before release.
- Adjacent processor recipient predicates are hardened (Prompt 6); live dispatch
  proof remains separately scoped follow-up; see the processor integration report.

Three migrations were applied/rollback-tested only on the builder-authorized
disposable DB. No production migration/deploy/provider send is implied.
Builder runtime counts are reported evidence, not rerun in this integration.
Voice excluded; single API instance only; do not claim production release-ready.

## Next action

Owner wants VPS development shipping prioritized and Bruno ignored entirely.
[Worker prompts](worker-prompts.md) are ready: Prompts 5, 1, 2, 3 and 6 are
integrated; Prompt 4 is optional, then authorized deployment/runtime verification
(Prompt 7). Prepare a fresh package from clean pushed main at release time. Existing
publish-out manifest is 41f9c91 (pre-Chat-Plus), not current main.
Development VPS means isolated Staging/test database, not public Development.
The testing page (Prompt 4) is optional; direct developer testing/reporting is
acceptable. Deploy only after exact host/database/startup-side-effect approval;
security/storage and production-device gates retain their documented owners.

# Chat Plus — mastermind integration evidence

Date: 2026-10-13. Owner explicitly authorized integration into main,
documentation synchronization, and remaining-work handoff.

Delivered branch: builder/chat-plus-production; base main/origin main 41f9c91;
delivered tip ded4dd1. Intake working tree clean. Remote fetch showed no main
advance. The existing bounded Phase 6 review and Phase 7 evidence were read;
no duplicate whole-feature review was commissioned.

## Integration corrections

- Hub mapping now enables CloseOnAuthenticationExpiration. The builder guide's
  claim that this server option does not exist was incorrect.
- Hub calls and realtime message/receipt/typing/presence audience resolution
  consult the production active-user lookup. Suspended/blocked/deleted users
  cannot keep receiving or broadcasting solely through cached membership.
- Production dispatch loop now injects active-user lookup and bound
  ChatPlus:Dispatch options; previously manually constructing the dispatcher
  omitted its optional lookup and ignored configured tuning.
- Single-instance startup guard cannot be bypassed by declaring a configured
  but unimplemented backplane. Invalid instance counts are also rejected.
- Publish requiredMigrations updated for Identity, Families, and Chat.
- Regression checks cover deactivation after connect, audience suppression,
  and topology bypass. Historical builder evidence remains revision-scoped.

## Gates and integration

Executed on delivered ded4dd1 plus the scoped integration correction delta:

- dotnet build Sanad.slnx --no-restore -m:1 --nologo: exit 0,
  zero warnings/errors. A new regression test initially lacked its audience
  fixture (1 failure); fixture corrected, final build and gates below passed.
- dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build
  --no-restore --filter FullyQualifiedName~ChatPlus --nologo: exit 0, 64/64.
- Full Sanad.UnitTests with --no-build --no-restore: exit 0, 3076 passed,
  0 failed, 3 pre-existing PostgreSQL subscription skips (total 3079).
- Sanad.ArchitectureTests with --no-build --no-restore: exit 0, 1/1.
- Verify-ApiContractMapping.ps1: 555 signatures, 619 Postman requests,
  0 missing/0 orphan Postman; 1411 Bruno requests, 148 missing, 0 orphans.
  Script exit 1 is its unconditional missing-Bruno check, not a clean exit;
  optional risk-based Bruno coverage is exempt under current governance.
  No new Bruno requests were authored/run, and no passing script exit claimed.
- Twenty tracked .postman_collection.json files parse; updated-document links
  resolve; git diff --check clean. Publish manifest latest identifiers match
  all registered module migration inventories.

No new API/database/provider/deploy run. Final main merge/push evidence follows
in the integration closeout, after this verified correction commit.

## Remaining work and owners

1. Owner/mobile: Firebase/APNs Android and physical iPhone delivery matrix.
2. Backend verifier: authorized paid/completed attributable fixtures, at least
   five Hourly and five EightHour; compare API ranges with direct SQL.
3. Operator: real edge TLS/WebSocket/origins/query-token redaction/upload limits.
4. Backend planner: immediate device-session revocation needs a session-bound
   access-token contract; current JWTs contain no device-session claim. Expiry
   closure and active-user checks do not prove immediate logout revocation.
5. Backend planner: physical orphan deletion worker. Lazy expiry frees logical
   quota but leaves disk occupied, so sustained storage bounding is unproven.
6. Runtime verifier: integration fixes need live expiry/deactivation/dispatch
   checks; builder's live socket-kill run recovered five messages. >100-message
   and receipt-only replay have automated evidence, not that live proof.
7. Care Homes owner: recurring CareHomeReviewNotificationProcessor translation
   failure observed during the builder's disposable-target run, out of slice.

Other documented limits: single instance only; no Redis implementation; no
voice; preference/block change after inbox enqueue but before push send is
not rechecked for chat membership by the push processor. Treat dispatch-window
privacy as a remaining release review requirement, not a guaranteed no-push
property after logout/block. Twenty-four-hour lazy expiry is unit-covered;
no long-window runtime observation was claimed.

Three migrations were applied/rollback-proven on the builder's authorized
disposable target only. No production migration/provider delivery/deployment
is implied. Implementation integration is distinct from production release.

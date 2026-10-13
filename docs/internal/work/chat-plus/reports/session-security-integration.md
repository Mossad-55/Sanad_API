# Session-security integration closeout

Worker branch `worker/chat-plus-session-security` was based on clean main
`436973f` and delivered `2d8af5a` (implementation `4a97485`). The branch was
fast-forwarded into main. The mastermind selected the worker's Chat-first
compatibility option.

## Selected client and token behavior

- Newly issued Normal access tokens carry the device session ID (`sid`); refresh
  preserves it. Restricted-verification tokens retain their existing behavior.
- Normal JWTs with `sid` are checked against the owning, active, unrevoked,
  unexpired session and Active user. Chat REST and SignalR require a verified
  session-bound token.
- Legacy Normal JWTs without `sid` remain accepted by non-Chat APIs until their
  existing 15-minute access-token expiry. Chat endpoints reject them immediately.
- Mobile clients should, on the first Chat 401 caused by a legacy token, refresh
  once and retry that Chat request once. If refresh fails or retry remains
  unauthorized, clear the invalid session as appropriate and require sign-in;
  do not create an automatic retry loop. Login/refresh response shapes do not
  change. This is the chosen rollout policy, not an unresolved release decision.

## Mastermind review correction

Review found the chat preference was checked before the remaining final-send
eligibility queries. A preference change during those queries could therefore
still permit a push. The integration moves the current `ChatMessages` preference
read to the end of the eligibility check and repeats the full check immediately
before each device send, after device enumeration. If eligibility changes after
another device already received the item, remaining sends stop and the outbox
item is completed rather than retried and duplicating the earlier send; if no
device received it, the item is durably suppressed.

The focused security test now changes the persisted ChatMessages preference and
confirms the final eligibility result denies delivery. No production provider
send was made. This does not eliminate the unavoidable interval between the
last database read and an external provider accepting a send.

## Verification on the integrated tree

- `dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true` — exit 0, zero warnings/errors.
- `dotnet test tests/Sanad.UnitTests/Sanad.UnitTests.csproj --no-build --no-restore --filter FullyQualifiedName~ChatPlusSessionSecurityTests --nologo` — 8/8 passed.
- `dotnet test Sanad.slnx --no-build --no-restore -m:1 --nologo` — Architecture 1/1; Unit 3,101 passed, 0 failed, 3 existing PostgreSQL-only skips.
- Worker-reported Postman mapping: 556 signatures, 620 requests, 0 missing/orphans. No API routes or request/response shapes changed by this integration correction.
- No live API, database, SignalR socket, push provider, VPS, migration, or device test was run.

## Still open

Automated tests establish the code paths but not deployed behavior. Prompt 6 must
verify session revocation through REST/hub, reconnect and dispatch behavior,
push suppression with a safe test device/provider path, plus prior VPS/TLS,
pricing-pool, mobile-device and other release gates. The worker noted that
`HasAnyActiveSessionAsync` uses wall-clock time; consider injecting the clock if
deterministic time control becomes necessary. Per-request session checks also
add Identity database reads to authenticated Normal requests; monitor latency
and database load during staging before production.

Next implementation worker is Prompt 2 (physical orphan cleanup/storage bounds),
from updated main. CORS remains unchanged by owner decision; Bruno remains
excluded; voice remains excluded; single-instance support remains the only
supported topology.

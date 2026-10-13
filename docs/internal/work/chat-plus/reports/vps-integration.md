# VPS-readiness worker — mastermind integration

Owner returned Prompt 5 branch worker/chat-plus-vps-readiness, base 1120434,
implementation 873c15e, delivered tip 3c9918d. Intake tree clean; origin/main
still 1120434 after fetch. Worker evidence is
[remaining-vps-readiness.md](../../../../../reports/remaining-vps-readiness.md).

## Owner decision

Keep AllowAnyOrigin, AllowAnyMethod and AllowAnyHeader unchanged for now.
This is confirmed configuration, not a readiness gap. The CORS registration
was compared with base main with line endings normalized and is unchanged.
Planning/app guides and subsequent worker prompts now reflect this decision;
no new browser-origin restriction was added to the API or nginx template.

## Bounded integration corrections

- systemd Type=simple replaces notify: the app has no SystemdLifetime readiness
  integration. Start-limit directives moved to Unit. Actual HTTP/migration
  verification still establishes readiness after process start.
- Service writable paths and preparation include files-private, the actual
  private-storage sibling; read-only release deployment cannot prevent uploads.
- Loopback-trusted forwarded address/scheme is processed before HTTPS redirect
  for the TLS nginx -> HTTP loopback template. Untrusted proxies are ignored.
  This is proxy/TLS handling, not a CORS policy change.
- Manifest reader consumes UTF-8 BOM emitted by Windows PowerShell publish
  scripts; tests cover both BOM and BOM-free files using the real reader.
- Migration probe uses asynchronous EF reads and propagates cancellation;
  database error text is still withheld.
- Sanitized nginx access format excludes Referer/User-Agent as well as query
  and Authorization/body values. Stock request-bearing error output goes to
  /dev/null; diagnostics use sanitized access metrics/application logs.
- Added four regression cases (two encodings, trusted/untrusted proxy scheme).

## Verification

Final correction delta build: exit 0, zero warnings/errors with
dotnet build Sanad.slnx --no-restore -m:1 --nologo /p:TreatWarningsAsErrors=true.
VpsReadiness filter: exit 0, 17/17. Postman-only mapping: exit 0, 556 controller
signatures, 620 requests, zero missing/orphans.
Full Sanad.UnitTests (--no-build --no-restore): exit 0, 3093 passed, 0 failed,
3 existing PostgreSQL subscription skips, total 3096. Architecture suite:
exit 0, 1/1. All tracked Postman collections parse; current doc links and
git diff --check pass. Correction commit 64d372e fast-forwarded into main from
1120434 without conflict (switch and merge --ff-only both exit 0). This
documentation-only closeout follows on main; its final pushed SHA and remote
equality are reported to the owner after push. Gates are reused unchanged
because the fast-forward did not alter tested code.

No new API/database/provider/package/VPS operation. Templates are reviewed
statically, not claimed nginx/systemd-validated on Linux. Persistent-key recovery
and proxy behavior are automated in-process tests, not a live VPS run.

## Handoff

Prompt 5 implementation is integrated. Actual isolated Staging
environment remains NOT READY until host/URL/service/test DB, secrets, backup/
restore/startup effects and a fresh package are named/prepared and tested.
Production remains NOT READY. CORS is excluded from both gap lists.
Next worker: Prompt 1 on updated main. Prompts 2/3, optional Prompt 4, authorized
deployment and Prompt 6 follow the previously agreed order.

References: [Microsoft proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0),
[systemd hosting](https://devblogs.microsoft.com/dotnet/net-core-and-systemd/).

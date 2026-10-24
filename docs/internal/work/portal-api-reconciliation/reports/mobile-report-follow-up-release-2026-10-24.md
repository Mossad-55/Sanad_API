# Mobile report follow-up — release receipt and Card enrollment findings

Date: 2026-10-24 (Africa/Cairo). Status: **released and verified on Production.**
Card enrollment: **not enabled — blocked on merchant-account data, not code.**

## Released revision and gates

Released revision: `f790ff57275de7ace776273b98e9cd7108c8841a`, pushed to
`origin/main` as a normal fast-forward (`dd4eb19b..f790ff57`), commit
`feat(mobile-report-follow-up): post viewer capabilities and completion-notes alignment`.
Previous live revision: `c1081620`.

| Gate (Mastermind-run, whole solution) | Result |
|---|---|
| `dotnet build Sanad.slnx -c Release -p:TreatWarningsAsErrors=true` | 0 warnings / 0 errors |
| Full unit suite | 5,533 passed, 26 skipped, 0 failed (4 m 17 s) |
| Architecture tests | 1/1 |
| `git diff --check` | clean |
| Postman collections | valid JSON |

Excluded and preserved uncommitted: `docs/internal/Mastermind_Handoff.md`
(parent-owned) and debris (`buildout*.txt`, `testout*.txt`, `migout.txt`,
`e07-library-smoke.ps1`, `.opencode-audit-tmp/`, `workflow-audit/`,
`Mastermind_Handoff_Historical_2026-10-23.md`).

## Delivered

- **Viewer capabilities:** `isOwn`/`canEdit`/`canDelete` on `CommunityPostResponse`
  across feed, detail, recommendations, favorites and create/edit, through one
  `ResolveCapabilities` rule. Foreign viewer false on all three; anonymous author
  keeps true flags with identity still hidden; mutation authorization, Admin
  moderation DTOs and routes unchanged.
- **Completion notes:** caregiver Postman sends `notes`; docs separate the
  optional request `notes` from the booking-detail readback `caregiverNotes`;
  tests prove persistence through both detail projections plus omission → null,
  with an OpenAPI pin on both names.

## Package and cutover

`Publish-SanadApi.ps1 -Configuration Release` exit 0 at the pushed revision.
Manifest revision matches the push; nine source-derived heads unchanged (Finance
head `20261024133842_AddDuplicateChargeClaims`). Archive
`deploy/sanad-api-f790ff57.tar.gz`, SHA-256
`ed57a47b4ab31c52f084df91c772dd21bfc3915e0171377ec31b0701b37438ba`
(12,481,832 bytes; 119 entries); no `.env`, no `appsettings.Development.json`, no
key/cert material; checksum identical on the target after transfer.

Backup in `/root/sanad-releases/portal-report-f790ff57-20261024192228/`:
`database-before-portal-report-f790ff57.dump` (custom format, 808,490 bytes,
**907 TOC entries**, validated with `pg_restore -l`), `app-before.tar.gz`
(12,499,316 bytes), pre/post config fingerprints.

Service `active`/`running`, NRestarts=0, **CONFIG_UNCHANGED**. Startup log records
all nine context checks with **0 pending** migrations (this slice is code/docs
only). `Verify-SanadDeployment.ps1` exit 0: remote revision equals
`f790ff57…`, Production, 9/9 required vs applied heads, anonymous smoke
`GET /api/v1/lookups/services` HTTP 200. Bounded anonymous smokes: community
feed/detail, admin community, family refund claim and admin claim routes all
401; no Production write beyond the startup migration check.

## Card enrollment findings (M3, read-only; owner approved remediation)

New, decisive evidence gathered on the live host with the configured credentials
(no secret printed, no charge, test-mode key only):

1. **The Production host is configured with a test-mode Paymob key**
   (`egy_sk_test_…`), not a live key. No real payment can succeed on this
   deployment; any checkout evidence is sandbox evidence.
2. The configured Card integration is valid: a plain intention returns **201**
   with a `client_secret` and `payment_keys[].integration = 5897206`.
3. Every mapped provider subscription plan is **invalid for this account**:
   intentions with `subscription_plan_id` `6755`, `6205` and a bogus control id
   all return **404 `invalid subscription plan id`**. The local plan → provider
   plan mapping therefore cannot work today even if the public flag were true.
4. `Paymob__Card3dsIntegrationId` is absent, which is why
   `subscriptionCardEnrollmentAvailable` is false
   (`PlatformControlCommands.cs:156`, `DependencyInjection.cs:849-851`).
5. Subscription-plan administration is not reachable with the host secret key
   (`GET` on the subscription/plans surfaces → 401 with both auth styles), so
   provider plans cannot be created or listed from the deployment.
6. Previously recorded, still true: plan id `6755` is shared by two monthly and
   one annual local plan, `gold` and the 2000-EGP annual plan have no mapping,
   the webhook is `http` (no TLS) with `Paymob__RedirectionUrl` absent, and no
   renewal scheduler exists — recurrence depends on Paymob charging the provider
   subscription and remains unproven.

**Why the flag was not switched on:** setting `Paymob__Card3dsIntegrationId`
alone would advertise enrollment that then fails at Paymob with `invalid
subscription plan id`. Enabling a broken paid path is worse than failing closed,
and the contract forbids forcing the flag. Nothing was changed in the merchant
account, host configuration or database.

Required to finish (merchant-account access is the blocker, not code):

1. Merchant dashboard (or an `api_key` that the subscription API accepts):
   create one provider subscription plan per sold term and capture its id.
2. Replace the local plan mappings with those ids via new immutable plan
   versions — never rewrite sold terms or historical subscriptions.
3. Set `Paymob__Card3dsIntegrationId` to the account's Card 3DS integration and
   restart; verify the public flag flips by itself.
4. Decide test vs live keys for Production, and `https` webhook +
   `RedirectionUrl` (needs a domain/certificate).
5. Only then run the sandbox end-to-end proof (checkout → 3DS → verified
   callback → persisted provider subscription → renewal → duplicate callback).
   Production recurrence still needs one real charge under separate authority.

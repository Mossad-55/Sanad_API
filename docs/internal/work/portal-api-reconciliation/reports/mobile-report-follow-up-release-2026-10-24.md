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

## Merchant-account work performed (owner supplied the api_key, 2026-10-25)

The owner supplied the merchant API key, which unblocked the account side. The
key was used only on the host, was never printed or committed, and no charge or
webhook was sent.

Account identified: merchant `1224628`, currency EGP, two integrations — both
**test** (`is_live: false`): `5897206` (VPC, `online` = the configured
`Paymob__CardIntegrationId`) and `5897245` (UIG, `online_new` = the wallet
integration). The intention `payment_keys[].integration` confirms `5897206`.
There is **no MOTO integration** and no separate 3DS integration object.

`GET /api/acceptance/subscription-plans` returned an **empty catalog**: the
previously mapped `6755`/`6205` never existed on this account, which is why every
subscription intention returned `404 invalid subscription plan id`. (Note the
route: `subscription-plans`, hyphen. `/api/acceptance/subscriptions/plans` does
not exist, and the plans namespace wants `Authorization: Bearer <auth token>`,
not the secret key.)

Five provider plans were created, one per distinct sold term, `plan_type: rent`,
`use_transaction_amount: false`, `number_of_deductions: null`, `reminder_days: 2`,
`retrial_days: 2`, `integration: 5897206`, `webhook_url` = the Sanad webhook.
`amount_cents` is the **tax-inclusive** recurring gross from the live charge rule
(platform fee 10% + tax 5%) applied to the tax-exclusive catalog price:

| Provider plan id | Name | frequency | amount_cents | Local plan version(s) |
|---|---|---|---|---|
| `12861` | Sanad Gold Monthly 199 | 30 | 22885 (199.00 → 228.85) | `gold` v1 |
| `12862` | Sanad Premium Monthly 199 | 30 | 22885 | `premium` v7 |
| `12863` | Sanad Premium Monthly 50 | 30 | 5750 (50.00 → 57.50) | `premium` v5 |
| `12864` | Sanad Premium Annual 3000 | 360 | 345000 (3000.00 → 3450.00) | `premium` v6 |
| `12865` | Sanad Premium Annual 2000 | 360 | 230000 (2000.00 → 2300.00) | `premium` v4 |

Validation: a subscription intention with each of the five ids returns **201**
with a `client_secret` (charge-free; intentions were never confirmed), where
`6755`/`6205` returned 404. The mapping is therefore genuinely usable.

## Defect found and fixed: subscription-management authentication

`PaymobClient.UpdateSubscriptionAmountAsync` (used by every plan-change amount
sync) authenticated the `PUT /api/acceptance/subscriptions/{id}` call with the
**secret key**. Proven against the live gateway: `Bearer <secret key>` → `401
{"detail":"Authentication credentials were not provided."}`, while `Bearer <auth
token>` → `404 {"message":"Subscription not found."}` (authenticated, id absent).
Every plan change for a provider-backed subscription would therefore have
failed with `Paymob.GatewayError`.

Fixed by adding `Paymob__ApiKey`, exchanging it at `POST /api/auth/tokens` for a
merchant auth token cached for 55 minutes, and using that bearer for the update.
No API key → the call fails closed with `Paymob.NotConfigured` and no request is
sent. Five new tests pin the contract (token used, not the secret key; token
cached; fail-closed without a key; fail-closed on a rejected exchange; gateway
error on a refused update).

## Intentionally NOT done, and why

- **Public enrollment flag left off.** The host still holds a test-mode secret
  key, so switching on `Paymob__Card3dsIntegrationId` would let a family complete
  a "payment" with test card data and receive a paid subscription for free. Test
  plans must not be mapped into Production for the same reason.
- **No live provider plans.** Minting live plans needs live integration ids,
  which the API cannot create; the test/live toggle and live onboarding are
  dashboard/support actions.
- **No Production plan-version changes.** Mapping is applied by authoring a new
  version, publishing it, and retiring the old one, and no admin credential is
  available here; it is also pointless until live ids exist.

Go-live sequence that remains: live onboarding (live integration ids + live
secret/public keys, dashboard) → create the same five plans against the live
integration → author new plan versions carrying those ids, publish, retire the
old ones → set `Paymob__Card3dsIntegrationId` and `Paymob__ApiKey`, restart → the
flag flips by itself → run the sandbox/live smoke, then one real charge under
separate authority for Production recurrence evidence.

## Second release: subscription-management auth fix (`03314442`)

Released and verified on 2026-10-25 as
`03314442b43b4b1b2afbec7031dabe585d7f1c0c` (`997d6056..03314442`, previous live
`f790ff57`). Gates: strict Release build 0/0, full suite **5,538 passed / 26
skipped / 0 failed** (5 new tests), architecture 1/1, `git diff --check` clean.

Deployment: backup `/root/sanad-releases/portal-report-03314442-20261024212550/`
(dump 808,490 bytes / 907 TOC entries validated, app tar 12,499,221 bytes),
archive SHA-256 `ef3edf0f…df3dc05` verified on target, service `active`,
NRestarts=0, 9/9 heads, 0 pending migrations.

One deliberate configuration change, confined to `/etc/sanad/sanad.env`:
`Paymob__ApiKey` was appended (mode `600 root:root`, one non-empty entry; the
pre-change copy is `sanad.env.before-apikey` in the release directory). The
unit file and `appsettings.json` fingerprints are unchanged, so the post-check
diff shows exactly that one file. The running process env contains the key
(one entry, verified by count without printing it), and the merchant catalog
still lists the five plans through it.

`subscriptionCardEnrollmentAvailable` **remains false** after the deploy, which
is the intended state; the new key does not unlock enrollment on a test-mode
secret key. Temporary credential copies were removed from both the host and the
build machine; the key now exists only in the service environment file.

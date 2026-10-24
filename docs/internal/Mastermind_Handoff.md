# Sanad API Mastermind handoff

## Active slice: media, care records, payments and Rentals removal

**Status: released and verified on Production 2026-10-24.**

Released revision `e29bc2c91e38c2ff76f2c1143ed943e416d9b13f` (commit
`e29bc2c`, docs receipt `3e5ece3`) is live on Production
`72.62.92.144:8091`, exactly matching its published manifest. Parent
independently confirmed via `GET /api/v1/deployment/info`: Production
environment, database reachable, required vs applied heads **9/9 match**;
anonymous smoke `GET /api/v1/lookups/services` **200**; former Rentals routes
`/api/v1/admin/rentals`, `/api/v1/family/rentals/bookings` and
`/api/v1/admin/rental-refund-cases` **404**; new care-record routes
`/api/v1/elderly/care-records` and `/api/v1/admin/care-records` **401**
(exist, auth-protected). Retained `rentals` schema/data preserved and empty.
Receipt:
[media-care-records-payments-release-2026-10-24.md](work/portal-api-reconciliation/reports/media-care-records-payments-release-2026-10-24.md).

Release team: River AI GLM 5.3 Flash worker `ses_e94978de6ffenpIPGkRfNmNcIL`;
Mastermind review corrected card-callback test disposal/cleanup and restored
mapping failure-path coverage before release. Gate evidence: FUR-02 4/4,
FUR-03 12/12, FUR-04 7/7, card-callback 6/6, storage 34/34, mapping 662/758
(0 missing / 0 orphan), Release build 0 warnings/errors, full suite 5,324
passed / 26 skipped / 0 failed. The `rnt_` cutover condition was cleared
read-only (all retained rentals/payment/refund tables 0 rows; no pending money
events). Validated backup with 884 TOC entries retained on-host.

Post-release limitations (recorded once, not passes): consumer-screen
integration remains unverified (no UI source tree; API-only smokes); genuine R1
replacement video content, wallet end-to-end (deferred by Owner direction), and
the fixed-name FUR-02/03/04 relational targets retain documented BLOCKED/NOT-RUN
status; the queued duplicate-charge Support-approval model is **not** implemented
or shipped; secret rotation of previously redacted doc text remains an Owner
follow-up. Sandbox Card checkout was proven end-to-end locally (hosted checkout,
HMAC-verified webhook, persisted paid booking) with no real charge and no live
keys; the Production Paymob webhook URL remains a cutover task. Real-disk storage
proof (34/34) uses synthetic MP4 bytes and does not prove genuine-content
playback.

Read the [execution brief](work/portal-api-reconciliation/worker-prompts/media-care-records-payments-2026-10-23.md),
then its [testing contract](work/portal-api-reconciliation/contracts/media-care-records-testing-2026-10-23.md).
These are the current scope and acceptance authority. Do not resume old worker
prompts or re-ask settled Owner decisions.

P0 read-only discovery completed in three parallel lanes. Owner locked the
remaining policy decisions in the active brief: caregiver 30-day completion grace;
caregiver/CareHome full CRUD on own-source records with audit; CareHome owner-only;
Admin SuperAdmin/SupportAdmin read-only; retain Rentals DB schema initially;
disable old external library video until upload; preserve conservative community
anonymity; fix F3 double-refund; EGP 500 cap already approved. Key findings:
care-record routes do not exist; media includes URL-only video and author-photo
projection/read mismatch; no family payment-attempt read exists. The P1
`rnt_` webhook path currently returns 503 and is covered by tests; provider retry
semantics and outstanding-event inventory gated production cutover, not code
authoring; both were cleared read-only before release (all retained rentals
tables 0 rows). W1/W2/W3/W4 handed back after bounded corrections; the active
brief records the final coordinated verification, and the slice was released
2026-10-24.

Next action: the queued portal-report follow-up plan is now the active
workstream. Pin revision `e29bc2c` as the released baseline, activate the queued
brief, and dispatch its Wave A lanes under the Owner's direction to start
immediately after release (the release gate is now satisfied).

## Queued next slice — release gate satisfied, activation pending Owner go

Owner locked the [portal report follow-up plan](work/portal-api-reconciliation/worker-prompts/portal-report-follow-up-2026-10-24.md)
on 2026-10-24, including Support approval for duplicate-charge claims. Prepared
parallel Wave A: booking discovery/outcomes, duplicate-charge workflow,
contract/documentation audit, identity/notifications; dependent Wave B: fixtures;
then serialized integration/review. Worker model is River AI GLM 5.3 Flash.
All tasks are pending; no new workers have started yet. The awaiting gate
(current-slice release and receipt verification) is **satisfied** as of
2026-10-24; activating the brief and dispatching Wave A is the next act on the
Owner's explicit start order. This is not authorization to deploy the follow-up
slice or seed Production.

Latest locked addition: Community authors can edit/delete only their own posts;
Community moderation Admins can delete any post. App ownership cannot be bypassed
by forged IDs. Required tests and cross-portal contracts are in the active brief.
This shipped with the 2026-10-24 release.

## Existing state to preserve

- Current runtime release: `e29bc2c91e38c2ff76f2c1143ed943e416d9b13f` live on
  Production (2026-10-24);
  [release receipt](work/portal-api-reconciliation/reports/media-care-records-payments-release-2026-10-24.md).
  Previous receipt: `edbccc153634cec7b942ae137871bb652f8ca1e2` (refund-policy
  wave, 2026-10-23); both deployed and superseded in order.
- Rentals runtime removal and care records shipped with the 2026-10-24 release;
  retained `rentals` schema/data remain on-host, preserved and empty.
- OTP remains excluded. No real charges/refunds or destructive database actions
  are authorized by preparation of the queued plan.

## History and cleanup

[Frozen previous handoff](Mastermind_Handoff_Historical_2026-10-23.md) retains
historical evidence and unrelated open items (including chat live-device checks
and caregiver data verification). They are not silently cancelled or added to
this slice. Use it only for a specific provenance question, not routine intake.

[Cleanup disposition](work/portal-api-reconciliation/reports/planning-cleanup-2026-10-23.md)
records what was consolidated and the exact legacy deletion candidates awaiting
confirmation. Release receipts, migrations, fixtures and paused work are not
disposable clutter.

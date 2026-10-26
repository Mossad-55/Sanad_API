# Sanad API Mastermind handoff

## Current next action — demo corpus slice D1–D4 completed and RELEASED on 2026-10-26 by explicit Owner order ("You have my approval so commit and push and also deploy"): release `verified`, phase `completed`. Remaining separately-authorized optional actions, NOT done and NOT approved: (i) the future Production environment restore on the rehearsal (it stays `Staging` with the seam enabled while seam-armed rows exist); (ii) the 7 regenerated corpus-keyed notifications are kept as intended corpus content per the approval wording (not approved for deletion)

The mobile-report slice is `completed`; release `verified` at live revision
`8913df42`. Commits: `456858d8` (implementation, 105 files, three migrations
applied on startup), `8913df42` (replay snapshot encoder parity), docs
`9d92b820`. Receipt:
[reports/mobile-report-release-2026-10-25.md](work/portal-api-reconciliation/reports/mobile-report-release-2026-10-25.md).
At release time (2026-10-25) the rehearsal VPS still ran `Production` with the
seam absent, and the release verifier independently reconfirmed: 9/9 migration
heads, 0 pending, active service zero restarts, config fingerprints unchanged,
byte-identical idempotent replay verified live (440 = 440 bytes). **Superseded on
2026-10-26:** the Owner answered the D3 seam decision, the rehearsal was flipped
`Production` → `Staging`, the seam was enabled with the 7 exact ids and verified,
and it stays that way while seam-armed rows exist (see D3 and the brief).
Authoritative scope: [full-demo-corpus-2026-10-25.md](work/demo-corpus/full-demo-corpus-2026-10-25.md).

The demo corpus implementation is D1–D3 `done`; D4 (the receipt) was
**ACCEPTED and CLOSED by the Owner on 2026-10-26** — the Owner answered the
final review report with "Accept it please" (a status record only; it does NOT
authorize commit, push, deploy, environment restoration or any new cleanup).
The execution history below stands as recorded.

**Release (2026-10-26, Owner-authorized, COMPLETE):** after acceptance the
Owner ordered the release verbatim ("You have my approval so commit and push
and also deploy"). Commit `2bccdb3e5b59c65ba1cad91abd640bbb7fb92b81` (short
`2bccdb3e`, 18 files, gate 0 warnings + DemoCorpusFacts 64/64, staged-diff
secret scan clean) pushed `origin/main` `9d92b820..2bccdb3e`; deployed to the
rehearsal VPS `srv1604711` (72.62.92.144, unit `sanad-api`, loopback :5001,
`Staging`, DB `SanadDb` revalidated live before any write); live revision
`8913df42` → `2bccdb3e`; migration set unchanged — all nine contexts
"0 pending / 0 migration(s) applied", no DB backup required (none taken);
config fingerprints (`appsettings.*` incl. the seam block, `/etc/sanad/sanad.env`,
unit) identical before/after; unrelated app on port 5000 untouched; binary
backup retained, rollback not needed. `Verify-SanadDeployment.ps1` exit 0 over
a localhost-only tunnel — revision match, required/applied 9/9, smoke
`GET /api/v1/lookups/services` HTTP 200; external-path limitation recorded
(loopback-only bind: public 5001 unreachable, public 80 → 404), wiring not
changed. Provisioner tool re-published at `2bccdb3e` and refreshed at
`/root/sanad-demo-provisioner` (DLL SHA-256 `cd2dfd6d...` byte-identical;
no provisioning run, no DB writes). Any present-tense "nothing committed,
pushed or deployed" statements in the demo-corpus docs and in the execution
history below describe the state during execution, before this Owner-authorized
release. Full evidence: the "Release record (2026-10-26, Owner-authorized)"
section in the [D4 receipt](work/demo-corpus/d4-receipt-2026-10-26.md) and the
brief's "Release (2026-10-26, Owner-authorized)" section. Release `verified`;
phase `completed` (2026-10-26).
D1: all 22 corpus groups, the two guard kinds, the media writer and the
`New-DemoMedia.ps1` generator, green on a throwaway nonce (135 → 763 → 763).
D2: local `SanadDb` seeded additively under the single `--allow-local-sanad-db`
override (886 → 1494 → 1494, no table decreased), 29 demo identities. D3: rehearsal
`SanadDb` seeded additively (8028 → 8711 → 8711, 75 tables changed, none decreased),
11 roles log in and read their demo data, `suspended.family` 403. D4: receipt with
counts, media URLs/content types, per-role logins, markers and the M1–M5
state-coverage matrix. Results:
[d1](work/demo-corpus/d1-dry-run-result-2026-10-25.md),
[d2](work/demo-corpus/d2-local-sanaddb-result-2026-10-26.md),
[d3](work/demo-corpus/d3-rehearsal-result-2026-10-26.md),
[d4](work/demo-corpus/d4-receipt-2026-10-26.md).

**Correction phase (2026-10-26, Owner-directed, second round, work complete —
`ready_for_review` at handback; ACCEPTED with the slice 2026-10-26):** the first D4 handback failed review, and
this round also replaced that round's own false incident story with the
evidence-corrected one. Full detail and honest ledger in the
[D4 receipt](work/demo-corpus/d4-receipt-2026-10-26.md), "Correction phase":

- **Rehearsal mis-invocation — root cause corrected.** The earlier
  "`--groups 0` via bash's read-only `$GROUPS`" story is FALSE (retracted): the
  persisted report `/root/demo-work-v2/portal-provision-manifest-demo1.json`
  shows the invocation reached the provisioner with an **empty group list**
  (`"groups": []`), so the group gate passed and the portal-fixture (W5) base
  fixture executed with zero groups requested. Exact footprint (nothing
  deleted): **27 rows** ULID `01a17593-*` (3 users, 3 user_accounts, 1
  caregiver, 2 certificates, 1 elderly, facility/room_type/room/bed/
  profile_revision/review_history 6, **4 documents with dangling keys
  `private/portal-fixture-demo1-*.pdf` — no file on disk**, 7 email_outbox)
  **+ 7 in-app notifications** (GUID ids, stamped with their source event time
  2026-10-25 12:46:52 - CORPUS-keyed, an attribution correction recorded in
  the Cleanup EXECUTED block below; invisible to ULID scans) = **34 rows**; complete PK + dependency inventory retained at
  `/root/demo-repair/misinvocation-inventory-2026-10-26.txt`. **Email dispatch
  happened:** all 7 outbox rows are **status=3 (Sent)**, dispatched wall-clock
  00:19:26-32 Z, and at least one recipient is a real pre-existing non-demo
  address (`admin@gmail.com`, created 2026-09-08). **Receipt of the seven
  dispatched demo emails is OWNER-CONFIRMED** (Owner, 2026-10-26, verbatim:
  "I've recieved all the emails actually") — a human confirmation, distinct
  from the system observation (7 rows marked Sent by the dispatcher); exact
  per-recipient breakdown held privately with the Owner/Mastermind; no
  recipient contacted, no message deleted; a read-only re-check
  (2026-10-26T02:11Z) confirms **no accidental
  outbox row remains undispatched**. "No provider contact" claims in this
  slice refer to Paymob only.
- **Stop-repeat fix shipped (uncommitted):** parse-time CLI gate
  (`ProvisionerCliGroups.cs` wired in `Program.cs`) — provision with an
  empty/missing `--groups` refused (exit 3, before any work), unknown ids
  (incl. a bare `0`) refused; verify-without-`--groups` stays valid.
- **Media hardening (Owner-instructed refuse semantics):** `DemoCorpusMedia`
  fingerprint-scopes `produced-keys.json`, re-validates recorded keys through
  the real storage implementation (repopulating length metadata), mirrors the
  writer's exact disk layout (full key incl. the `private/` prefix under the
  `-private` sibling), and **refuses the run** on a broken recorded key instead
  of writing an unreferenced replacement (prior fallback removed). Byte trace
  sha256-proven on the rehearsal: all three demo sources identical on disk
  (jpg 57,178 B / pdf 6,540 B / mp4 3,585,618 B); the persisted video key ends
  `.mp4`. Playback in a player **not verified** (stored+locked only).
- **Seam evidence upgraded to config↔DB on both targets — with the round-two
  HIGH finding repaired:** deployed allowlist
  `/root/sanad-api/appsettings.Staging.json` (7 ids exactly,
  `ASPNETCORE_ENVIRONMENT=Staging`) and the identical local
  `appsettings.Development.json` block (local only, no secrets) verified against
  persisted armed ids: care-home numerics `5359006666923497873` /
  `2178478915071097933` (one funded booking each per target), 4 claim captures
  in `families.subscription_payment_attempts`, and linked
  `finance.duplicate_charge_claims` with recorded gateway outcomes
  (`Paymob.RefundOutcomeUnknown` / `Paymob.RefundRejected`). The caregiver
  seam-failed-refund slot needed an Owner-authorized keyed repair on the local
  target: an earlier handback wrongly called the local legacy colon-form row
  "terminal, not armed" — in fact `AdminBookingCommands` sends the FIRST
  SUCCEEDED transaction's stored id on a refund retry (success is eligibility,
  not refund terminality), the slot is retryable by design, and its succeeded
  transaction still carried the colon id the seam does not intercept — a retry
  would have reached the real Paymob gateway. The repair updated exactly one
  PK-pinned `families.payment_transactions` row to the configured slug ids
  (`UPDATE 1`, guard-locked, unrelated rows untouched); post-repair read-only
  evaluation proves the effective retry id equals the allowlist entry on BOTH
  targets; the retry endpoint was never called. A fail-closed
  `SeamArmedIdMismatch` guard now wired into the caregiver seam drive path
  refuses any reused armed slot whose persisted ids mismatch the config.
  `systemctl is-active sanad-api` = active (read-only check only; "no
  processes" claims in earlier notes meant LOCAL test processes).
- **Counts, exact decomposition (per-table snapshots excluding the ten
  `__EFMigrationsHistory` tables):** D3-final stored file (23:02 UTC) = **8638**
  (docs' earlier "8640" not reproducible from any stored snapshot) →
  post-incident+correction `counts-vpre.txt` (00:23:18) = **8682**;
  ∆ = **+44** = 27 incident + 7 in-app notifications + 7 intended correction-run
  caregiver/medical rows + 1 comment like (`Interactions 01a17596-2a8a…`) +
  2 device sessions. Corrected-binary 22-group run + replay (`v2-provision.json`
  00:22:35, `v2-replay3.json` 00:23:17) → `counts-v2-after.txt` (00:27:50) =
  **0 delta**. The earlier "8681" and "+1 medication audit" claims are
  retracted (0 medication-audit rows in the window). Row-level byte-identity of
  pre-existing rows is **UNPROVEN** (never hash-sampled); only additive-only
  semantics are claimed.
- **Tests/build (after the round-two review repair):** provisioner facts **64/64**
  (new regressions: CLI gate, refuse-without-replacement media modes, length
  repopulation, sidecar scoping, and the seam-armed-id mismatch refusal for a
  reused legacy caregiver slot), strict Release build 0/0, no DEBUG probes left.

- **Carried from the accepted first-round repairs (unchanged, re-verified in
  round two):** demo Arabic repaired in source and via the identical
  guarded-UPDATE script on both targets (rehearsal 92 rows; full-corpus scans
  0 hits, wire bytes real UTF-8); demo posts carry the authorized image route
  URL with raw-key `ImageUrl` rows 0 on both targets, like states and the
  comment-like toggle proven by authenticated API calls; demo image proof is
  two distinct kinds, both green — DISK hash equality (sha256 vs source, all
  three media files) and, re-proven in round two, the ROUTED authenticated
  read on the rehearsal: login `demo1.family.owner` → `GET
  /api/v1/community/uploads/images/01a17542-42bc…/file` = **200 `image/jpeg`,
  57,178 B, sha256 identical to the source**, the session created for that one
  read logged out immediately (the earlier "rehearsal 866,519 B" was a
  misidentified pre-existing 2026-10-19 upload).

**Cleanup EXECUTED (2026-10-26, Owner-approved; accepted with the slice
2026-10-26):** the Owner answered the cleanup question with "I approve" and the
exact-list cleanup ran with full verification. **Rows, first pass: 22 actual
incident rows + 7 in-app notifications** (approved at the time; later shown
CORPUS-keyed - see the New Owner items below) plus email_outbox 7, documents 4,
beds/rooms/room_types/review_history/profile_revisions/facilities/elderly 1
each, care-home-owner + phone-grandfather users 2 + accounts 2, in one
explicit-PK transaction with rollback-guard assertions. **Rows, second pass
(same day): the Owner approved the exact 12-row held block (verbatim
"Approved") and it was executed** - the 5 remaining incident rows (caregiver
01a17593-3c2e-7aaf-8a4b-9b9829180fdc + its 2 certificates + its user + its
account) together with their 7 FK-dependent availability rows (1 area + 1
language + 1 service selection, 1 medical schedule, windows Id 7/8, shift Id
15): guarded, rollback-protected single-transaction deletes, all 12 rows
archived full-column plus manifest and exact pre/post 177-table count
snapshots (delta exactly the 12 rows over 10 tables, nothing else); an initial
dispatch attempt died instantly on a provider quota error and target state was
verified untouched before re-execution; the demo1.caregiver login is now gone.
**Files: all 15 approved files deleted** (6 rehearsal + 9 local) after fresh
0-reference rechecks (18 columns rehearsal / 19 columns local, including
CommunityImages, community.Posts.ImageUrl, chat.Attachments), sha256 gates
against the retained inventories, and private archives
(/root/demo-repair/recovery-cleanup-2026-10-26/orphan-media-6-files-2026-10-26.tar.gz
plus per-table row CSVs, FK map, held12-*.csv archives and pre/post count
scans, modes 700/600; locally C:\Users\mosad\Sanad-Recovery\demo-cleanup-2026-
10-26\ with tar + hash manifest). The 2 non-demo landing images stay untouched.
rehearsal produced-keys.json realigned from 3 deleted keys to corpus-
referenced, DB-referenced artifacts (byte-identical fixture content, JSON
validated, pre-realignment copy retained); no produced-keys.json exists
locally; the local library video still references the preserved 01a17510
generation. **Post-clean verification:** incident rows 0 (exact-id checks,
both passes), held block 0 after the second pass, preservation intact (demo
family + its 2 elderly, the 4 corpus caregiver fixtures with schedule + 3
shifts + 2 certificates each, 5 corpus facilities, 19 library contents, seam
rehearsal booking), service active at revision 8913df42 (Staging, DB
reachable) with the 7-id FixtureRefundSeam intact, local seam booking present;
no service restart, no provider contact, nothing committed or pushed. **New
Owner items from the window (mechanism source-proven; the earlier "demo
notification generator" claim is RETRACTED):** (a) 7 in-app notifications were
RE-CREATED from the deleted rows by `CareHomeReviewNotificationProcessor`
(`src/API/Sanad.API/CareHomesIntegration/`, a `BackgroundService` polling
every 20 s): it materializes care-home submission/review SOURCE rows into
notifications stamped with the source event's timestamp (hence the old
2026-10-25 12:46:52 stamps) and de-duplicates solely by idempotency key - and
the 7 re-created rows carry keys identical to the deleted rows' keys, proving
re-materialization (not a generator, not operator content); the corpus source
rows persist, so deleting these again without deleting the source repeats the
cycle; (b) attribution correction: the approved/deleted 7 in-app GUIDs were
CORPUS-keyed (12:46:52), while the ACTUAL 7 incident in-app notifications
(created_on 12:00:00+00, keyed on the incident revision
`01a175933dce...`/history `01a175933dda...`) were STILL PRESENT at that point
and ORPHANED (their source incident rows were deleted) - executed on the Owner's
final approval (verbatim "Yes I apprrove" to the exact seven question; the
intended corpus notifications explicitly preserved): deleted by exact GUID in
a guarded transaction, DELETE exactly 7, notifications 86 -> 79 with zero
other table changes, absence re-verified after multiple processor poll cycles
(source incident rows absent, 0 incident-keyed outbox rows, so no
regeneration or re-dispatch path); full archive + manifest + pre/post
177-table snapshots in the recovery directory, GUIDs and evidence in the D4
receipt "Final-7 incident notification cleanup"; (c) no email re-dispatch occurred: 0 incident-keyed outbox rows
re-present, 0 outbox rows created or sent after 02:40Z, 0 pending (all 65
status=3); the outbox 65-vs-64 gap is RESOLVED (the 65th row is the operator
approval email 01a17613-116e-..., created 02:38:24 - seconds after the 02:38
post-scan - fully accounted); (d) live-operator
drift on the rehearsal is ongoing and documented (admin audit rows
1200 -> 1219 read-only browsing; one real care-home approval at 02:38:24 with
notification 03313da2-... and outbox row 01a17613-116e-... to
demo1.carehome.owner2@test.sanad.local). Stability at close: notifications 86
/ outbox 65 during the window; after the final-7 pass notifications 79 / outbox
65, all verified absent-after-processor; the DB "no triggers" check was a
DB-schema check only and does not rule out application consumers - the
processor above demonstrably re-created rows. Cumulative cleanup result: 41
unintended rows removed (all 34 original accidental rows across the three
approved passes + the 7 approved availability dependents); the historical
mistaken deletion of the 7 intended corpus notifications and their processor
regeneration stays recorded; the 7 regenerated corpus notifications are
preserved as intended corpus content. Full evidence: D4 receipt,
"Cleanup execution" (including "Held-block decision and execution" and
"Final-7 incident notification cleanup"). Email receipt of the 7 dispatched
demo emails remains OWNER-CONFIRMED (cannot recall); no Paymob call was ever
made. This cleanup has no remaining open items; the slice awaits parent
review and a status-only acceptance record.
Settled owner decision (was open): on 2026-10-26 the Owner answered the D3 seam
question — **"Enable non-Prod seam, seed all 22 (Recommended)."** The disposable
rehearsal was flipped `Production` → `Staging` (pre-change env preserved at
`/etc/sanad/sanad.env.demo-d3-bak`, `sha256 8ea59fd7…`), `FixtureRefundSeam` enabled
with the 7 exact ids, verified from the startup state line
(`enabled=True, allowlist entries=7`; prior processes `enabled=False`), and the
seam **stays** enabled while seam-armed rows exist
([operations/fixture-refund-seam.md](operations/fixture-refund-seam.md)). Restoring
`Production` and reaping the armed rows is a future, separately authorized operator
action. Config: `/root/sanad-api/appsettings.Staging.json` (mode 600, 7 colon-free
`RefundOutcomes`); credentials `/root/demo-secrets/credentials.txt` (mode 600).

**Defect found and fixed (D3):** the `FixtureRefundSeam` allowlist is
configuration, and the .NET binder splits configuration keys on `:`, so the demo
money-state provider ids that contained `:` were silently dropped by the binder
(proven: 3 keys → `Count=1`; the `{Key,Value}` array form → `Count=0`). Fix: the
demo seam-facing ids were made **colon-free** (mirroring the S3a `spf-…` shape)
while durable markers stay unchanged; the legacy capture id is still accepted on
reuse so the local target stays idempotent. Post-fix: strict build 0/0, 51/51
provisioner tests, local replay still zero-delta.

Carried observations (recorded, not blockers; updated by the correction phase):
two of six library items are premium and correctly locked for every demo login
(the frozen contract grants `PremiumContent` only to `PremiumPlus`, which the
pinned inventory does not seed) — so premium playback is not demonstrable by a
demo login, and the locked state is the demonstrated state;
`community.Posts.ImageUrl` raw-key rows are **0** after the correction phase (demo
posts carry the authorized route URL); `finance.admin` returns 403 on
finance-admin routes — a seed-data convention (admin-type demo accounts carry no
FinanceAdmin role; this is observed state on the seeded data, NOT a demonstrated
authorization policy rule; `superadmin` reads those routes); `elderly.*`/`dependent.*` are phone-only with no
password by design; 4 pre-existing S3a `portal-fixture-r2-*.pdf` keys are missing
on disk; device-session rows keep accruing on the rehearsal from normal service
activity (wall-clock stamped, outside the corpus); at the round-two close the
measured demo sessions were local 7 / rehearsal 63, all revoked, 0 active; the Owner-approved orphan-media deletion was EXECUTED 2026-10-26 (6 rehearsal + 9 local files; see Cleanup execution); previously-unreferenced media generations existed on
both targets pending the Owner's deletion decision (exact lists in the D4
receipt's limitations); the "52 distinct keys" figure counts distinct storage keys
referenced by the corpus on the rehearsal target (multiple rows share each key).
The D2 (+608) and D3 (+683) deltas are same-method per-target deltas, not
cross-target comparisons (the rehearsal carried more pre-existing S3a data). The
MP4 stop remains cleared (WinRT `MediaComposition` + validated `ffmpeg` fallback).
Public legal copy, exact-list orphan deletion approval and mobile-build evidence
remain external dependencies.

Nothing in D1–D4 is committed, pushed or deployed; no provider call was made and no
release authorization is implied.

Workers: M1–M3 `ses_e8c18c41effeSu7xxoypLQmszL`, M4–M5
`ses_e8be7ecb3ffeHFEvS1nOi08RWq`, release `ses_e8b9342eeffeIrQ93u4nl7Pq8h`,
D1 `ses_e8b79bdf6ffealugPWuCgF3FQQ`. Private handoff and pre-existing untracked
work remain uncommitted, as requested.

## Current working arrangement

Owner direction (2026-10-24): Mastermind minimizes tokens and retains scope,
review, acceptance and final decisions, including bounded evidence review of the
actual diff. Investigation, implementation, edits and checks are delegated to two
River workers — River DeepSeek V4.1 Flash high
(`river/deepseek-ai/DeepSeek-V4.1-Flash#high`) and River GLM 5.3 Flash high
(`river/zai-org/GLM-5.3-Flash#high`); both IDs/variants are verified, and `#high`
effort is required on dispatch or resumption. Workers report concise exact files,
checks and blockers; avoid duplicate work and keep overlapping edits and runtime
serialized. The in-flight GLM W5 read-only design worker
`ses_e91c0592dffeG6g5bWbnzd6dC1` is not interrupted, polled or duplicated and its
current effort is unchanged. Existing slice status and release restrictions below
are unchanged.

## Previous slice: portal report follow-up — RELEASED

**Status: released and verified on Production 2026-10-24.** Released revision
`c1081620723a0ff2352711b4a3dce5d80c993e3a` (`eb218a00..c1081620`), live at
`72.62.92.144:8091`, service `sanad-api.service`, NRestarts=0, config
fingerprints unchanged, 9/9 migration heads including Finance
`20261024133842_AddDuplicateChargeClaims`, verifier exit 0. Receipt:
[portal-report-duplicate-charge-release-2026-10-24.md](work/portal-api-reconciliation/reports/portal-report-duplicate-charge-release-2026-10-24.md).

W5 is **accepted and runtime-verified** on the local target:
[w5-portal-fixture-runtime-2026-10-24.md](work/portal-api-reconciliation/reports/w5-portal-fixture-runtime-2026-10-24.md).
Twelve groups provisioned with persisted readbacks over three idempotent runs,
six provider seams proven inert, private family login plus authenticated family
and admin readbacks browsable on loopback, report-only cleanup with no delete,
and no provider call or money movement. The runtime pass also found and fixed a
real defect: `GET /api/v1/admin/bookings` returned 500 because `.Id.Value` is not
translatable inside EF lambdas; ordering now uses the mapped id (`BookingId :
IComparable`) and lookups unwrap after materialization.

Release gates: strict Release 0/0, full suite 5,523 passed / 26 skipped / 0
failed, W5 offline 17/17, validated pre-cutover dump (899 TOC entries) and app
backup, package SHA-256 verified on target. Docs receipt commit `dd4eb19b` is
docs-only after the deployed revision. Active scope/status authority:
[portal report follow-up](work/portal-api-reconciliation/worker-prompts/portal-report-follow-up-2026-10-24.md).

## Active slice: mobile report follow-up — RELEASED

**Status: released and verified on Production 2026-10-24.** Released revision
`f790ff57275de7ace776273b98e9cd7108c8841a` (`dd4eb19b..f790ff57`), live at
`72.62.92.144:8091`, service `sanad-api.service`, NRestarts=0, config
fingerprints unchanged, 9/9 migration heads, verifier exit 0. Receipt:
[mobile-report-follow-up-release-2026-10-24.md](work/portal-api-reconciliation/reports/mobile-report-follow-up-release-2026-10-24.md).

Owner locked [the mobile report scope](work/portal-api-reconciliation/worker-prompts/mobile-report-follow-up-2026-10-24.md)
and approved the slice plus M3 remediation with "ship it and I approve
everything". Delivered:

- M1 viewer capabilities (River DeepSeek `#high`): `isOwn`/`canEdit`/`canDelete`
  on `CommunityPostResponse` across feed, detail, recommendations, favorites and
  create/edit through one `ResolveCapabilities` rule; anonymity and mutation
  authorization unchanged.
- M2 completion notes (River GLM `#high`): Postman now sends `notes`, docs
  separate request `notes` from readback `caregiverNotes`, and new tests prove
  persistence through both detail projections.
- M3 Card enrollment (River GLM `#high`, read-only, then owner-approved live
  probes): the flag is false because `Paymob__Card3dsIntegrationId` is absent,
  **and** the live host holds a test-mode key (`egy_sk_test_…`) whose mapped
  provider subscription plans `6755`/`6205` both return `404 invalid subscription
  plan id` from the intention API. The Card integration itself is valid (201 with
  `client_secret`). The flag was deliberately left off: enabling it would
  advertise a paid path that fails at Paymob. Plan `6755` is shared across terms,
  `gold` and the 2000-EGP annual plan are unmapped, the webhook is http with
  `RedirectionUrl` absent, and no renewal scheduler exists.

Gates: strict Release 0/0, full suite **5,533 passed / 26 skipped / 0 failed**,
architecture 1/1, diff check clean, Postman JSON valid; backup 808,490 bytes /
907 TOC entries; archive SHA-256 `ed57a47b…37438ba` verified on target.

**Remaining Card-enrollment work is owner-gated, not implementation:** needs
merchant-account access (dashboard or an `api_key` the subscription API accepts)
to create per-term provider subscription plans, then immutable local plan
versions, then `Paymob__Card3dsIntegrationId` + restart, then live-vs-test keys
and `https` webhook/`RedirectionUrl`, then a sandbox end-to-end proof. Production
recurrence still needs one real charge under separate authority.

### Rehearsal enrollment proven end to end (2026-10-25)

The owner confirmed this VPS and Paymob account are a **rehearsal**; real
go-live uses different credentials, server and merchant account. Enrollment is
therefore enabled here on purpose, and the full flow now works.

Third release `4690b5b2e46a311bbb64f76c41c81769a34d4832` (`c76a64dc..4690b5b2`)
is live: 5,543 passed / 26 skipped / 0 failed, architecture 1/1, backup 907 TOC
entries, archive `39d2db66…` verified, `CONFIG_UNCHANGED`, 9/9 heads, smoke 200,
NRestarts=0.

- Six on-sale plan versions mapped to real provider plans
  (`12861`–`12866`) via direct SQL — a rehearsal-only shortcut; production must
  author a new version, publish, then retire the old one.
- `Paymob__Card3dsIntegrationId=5897206` + restart flipped
  `subscriptionCardEnrollmentAvailable` to **true** with no code change.
- Defect found and fixed in that release: the client serialized
  `redirection_url`/`notification_url` as explicit `null`, which Paymob rejects
  (`400`), failing every Card enrollment intent without `Paymob__RedirectionUrl`.
  Proof: identical payload with the key omitted returned `201`. Field-level
  provider rejections now surface as `upstream: <field>: <message>`.
- Live run: quote 57.50 → intent 200 → real unified checkout with a test card →
  3DS cleared → **Approved** (txn `560118491`) → provider subscription `14748`
  (`plan_id 12863`, `amount_cents 5750`, `active`, `next_billing 2026-11-25`) →
  family subscription active, invoice `INV-2026-10-000001` 57.50, PDF valid,
  identity registry and callback ledger rows present, 8 benefits granted.
- The subscription-management auth fix was exercised against that real
  subscription: authenticated `PUT` returned `200` (secret key → 401).
- Not proven: an actual renewal charge (due 2026-11-25); renewal, grace and
  duplicate-delivery logic is covered by `SubscriptionProviderCallbackTests`.
  Production still needs live ids/keys/plans and an `https` webhook.

### Merchant-account progress (2026-10-25, owner supplied the api_key)

Second release `03314442b43b4b1b2afbec7031dabe585d7f1c0c` is live
(`997d6056..03314442`, docs `c76a64dc`): 5,538 passed / 26 skipped / 0 failed,
architecture 1/1, backup 907 TOC entries, archive `ef3edf0f…` verified.

- Merchant `1224628` identified: two **test** integrations (`5897206` VPC card =
  `Paymob__CardIntegrationId`, `5897245` UIG wallet), **empty** provider plan
  catalog — `6755`/`6205` never existed there.
- Five provider subscription plans created (`12861`–`12865`) with tax-inclusive
  amounts and correct frequencies; a subscription intention returns 201 for each,
  where the old ids returned 404.
- Real defect found and fixed: `UpdateSubscriptionAmountAsync` used the secret key,
  which the subscription namespace rejects (401); now exchanges `Paymob__ApiKey`
  for a cached auth token (5 new tests). Key installed in `/etc/sanad/sanad.env`
  (mode 600, the only config change; unit file and appsettings unchanged).
- `subscriptionCardEnrollmentAvailable` **deliberately remains false**: the host
  holds a test-mode secret key, so enabling enrollment would grant paid
  subscriptions for free. Test plan ids are not mapped into Production.
- Go-live still needs live integration ids + live keys (dashboard/support), then
  five live plans, new plan versions carrying those ids, the 3DS integration id,
  and `https` webhook/`RedirectionUrl`.

## Previous slice: media, care records, payments and Rentals removal

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

The former queued-slice status and dispatch instructions above are historical.
The portal-report follow-up is active; its current implementation, verification,
blockers and next action are recorded in the active brief linked at the top of
this handoff. Its release remains separately unauthorized.

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

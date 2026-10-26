# D3 result — additive demo corpus on the rehearsal `SanadDb` (72.62.92.144)

Status: **done** (2026-10-26). Scope authority:
[full-demo-corpus-2026-10-25.md](full-demo-corpus-2026-10-25.md) and
[d1-work-order-2026-10-25.md](d1-work-order-2026-10-25.md). The corpus was written
**additively** on the rehearsal database: insert-or-reuse, singleton reuse (never
overwritten), no reset, no deletion, no provider call, no clock change.

This was the stage that needed the one recorded Owner decision — the non-Production
refund seam. Owner answer (2026-10-26): **"Enable non-Prod seam, seed all 22
(Recommended)."** The rehearsal service was flipped to a non-Production
environment, the seam was enabled with the exact plan-printed allowlist, verified,
and then all 22 groups were seeded.

**The rehearsal intentionally does NOT return to `Production`.** The seam
documentation ([operations/fixture-refund-seam.md](../../operations/fixture-refund-seam.md))
requires the allowlist to stay in place for as long as a fixture-owned
provider-call-armed row exists; disabling it would expose a later operator refund
click to the real provider. So the rehearsal keeps `ASPNETCORE_ENVIRONMENT=Staging`
with `appsettings.Staging.json` until the armed rows are reaped by a separately
authorized operator action. The pre-change environment file is preserved at
`/etc/sanad/sanad.env.demo-d3-bak` (`sha256 8ea59fd7e7fd57b319d055daa94b3d41bc5980308dbbeb0a75125532b6b52d26`,
was `ASPNETCORE_ENVIRONMENT=Production`).

## Preconditions applied first

| Step | Result |
| --- | --- |
| D3 seam-id defect fix (see [d1-dry-run-result-2026-10-25.md](d1-dry-run-result-2026-10-25.md#defect-fixed-in-d3-colon-free-seam-ids)) | seam-facing ids made colon-free; strict build 0/0, 51/51 provisioner tests, local replay still zero-delta |
| Provisioner published | `linux-x64`, 106 files / 32.9 MB | 
| Transfer | zipped (`Compress-Archive`) and `scp`'d; extracted with `python3 -m zipfile` (no `unzip` on host) into `/root/sanad-demo-provisioner` |
| Media staged | `/root/demo-media` (`sanad-demo.jpg` 57178 B, `sanad-demo.mp4` 3585618 B, `sanad-demo-document.pdf` 6540 B); sha256 match local |
| Credentials | `/root/demo-secrets/credentials.txt` (mode 600, 3 lines) from the API's own `ConnectionStrings__IdentityDatabase` in `/etc/sanad/sanad.env`, a read-only `select system_identifier::text from pg_control_system()` (length 19) and the fixture password |
| Environment flip | `Production` → `Staging`; backup written; `/root/sanad-api/appsettings.Staging.json` (mode 600) with the 7 colon-free `RefundOutcomes`; `daemon-reload` + `restart` → `active`, `NRestarts=0` |
| Post-restart identity | `GET /api/v1/deployment/info` → `environment=Staging`, revision `8913df42…`, 9/9 heads, `databaseReachable=true` |

## Seam gate verification (mandatory, passed)

The running process emitted exactly one state line, matching the plan byte-for-byte
(prior processes logged `enabled=False`):

```
Fixture refund seam state: enabled=True, allowlist entries=7, allowlisted transaction ids=[
  2178478915071097933,
  5359006666923497873,
  demo-demo1-caregiver-bookings-seam-failed-refund-order-txn,
  demo-demo1-finance-claims-ambiguous-capture,
  demo-demo1-finance-claims-approved-capture,
  demo-demo1-finance-claims-failed-capture,
  demo-demo1-finance-claims-submitted-capture]
```

## Setup

| Item | Value |
| --- | --- |
| Target | `72.62.92.144`, postgres loopback `127.0.0.1:5433`, database `SanadDb`, user `postgres` |
| Target kind / revision | `rehearsal` / `8913df42aadc519c8deee4de1a16ee56fabf2e67` |
| Access | SSH `root@72.62.92.144`; API readbacks over `http://127.0.0.1:5001`; public via nginx `:8091` |
| Tunnel declaration | `--tunnel-declared`, `--expect-host 127.0.0.1` — `serverAddress=::1/128`, `clusterIdentityVerified=true` |
| Upload root | `/var/sanad/files` (public) + `/var/sanad/files-private` (the app's private sibling) |
| Media dir | `/root/demo-media` |
| Run tag / instant | `demo1` / `2026-10-25T12:00:00Z` |
| Groups | all 22, with `--seam-deployed` and the nine `--expect-migration-heads` |

## Sequence and result

1. **verify** — exit 0; `verificationPassed=true`, `clusterIdentityVerified=true`,
   `declaredHost=127.0.0.1`, `tunnelDeclared=true`, all nine module heads matched,
   `pendingMigrations=0`.
2. **plan** — exit 0, zero database access: **22 planned / 0 refused**, 7-entry
   allowlist identical to the local target.
3. **provision (run 1)** — exit 0, `provisionPassed=true`, `unresolved=[]`,
   **all 22 groups provisioned**.
4. **provision (run 2, replay)** — exit 0, all 22 groups, `unresolved=[]`.

## Idempotency proof (row counts)

| Stage | Rows | Delta |
| --- | --- | --- |
| before (pre-seed) | 8028 | — |
| after run 1 | **8711** | **+683** |
| after run 2 (replay) | **8711** | **+0** |

Run 1 changed **75** of 177 tables and **no table decreased** (purely additive).
The replay changed **0** tables. Highlights of the run-1 delta:
`identity.users`/`identity.user_accounts` 24→53 (+29);
`care_homes.facilities` 3→8, `care_homes.bookings` 12→26 (+14),
`care_homes.visits` 1→11 (+10), `care_homes.rooms` 3→9, `care_homes.beds` 3→15,
`care_homes.documents` 12→28;
`caregivers.caregivers` 8→14, `caregivers.caregiver_ratings` 0→8,
`caregivers.caregiver_rating_moderation_actions` 0→1;
`families.families` 7→15, `families.family_subscriptions` 4→16,
`families.bookings` 3→25 (+22 = 14 caregiver bookings + 8 rating-prerequisite
completed bookings), `families.subscription_invoices` 2→10;
`chat.Conversations` 2→6, `chat.Messages` 4→31, `chat.Attachments` 0→2,
`chat.Reports` 0→2;
`community.Posts` 3→9, `community.PostViews` 1→5, `community.Comments` 1→13;
`cms.library_contents` 13→19, `cms.wellness_tips` 6→12, `cms.help_faqs` 10→18,
`cms.landing_sections` 0→6, `cms.legal_documents` 2→4;
`finance.duplicate_charge_claims` 1→5, `finance.complaint_reviews` 2→6,
`finance.refund_ledgers` 1→7;
`notifications.notifications` 27→78, `notifications.email_outbox` 22→64.

## Media (written through the app's own storage implementation)

Four generated artifacts, produced by the real `LocalDiskFileStorage` into the
target's upload root; every key was re-opened/confirmed by the writer before its
row stored it:

| Kind | Stored key | sha256 | Bytes |
| --- | --- | --- | --- |
| public image | `library/01a17542448f7c46bc42abc884f6a30d.jpg` | `5dd95520…f2b002` | 57178 |
| private image | `private/care-home-media/01a175422a9c78b79cea1918c715ca72.jpg` | `5dd95520…f2b002` | 57178 |
| private document | `private/care-home-documents/01a175422ac5719693ad95532b4ae431.pdf` | `78dd0985…c6917935` | 6540 |
| private video | `private/library-video/01a17542449f75a98e7aa31a3c97f2a3.mp4` | `115033f2…a26f9bd` | 3585618 |

Reuse (all references point at the single artifact per kind): the public image
backs 6 library covers + 6 wellness tips + 1 landing hero; the private image backs
8 facility profile media + 6 room media + 3 room-type media + 1 chat attachment +
1 community image + 2 post images; the private PDF backs 16 facility documents +
1 chat attachment; the private MP4 backs the single video library item.

Resolution checks:

| Check | Result |
| --- | --- |
| `GET /files/library/01a17542…jpg` (anonymous) | **200**, `image/jpeg`, 57178 B |
| same via nginx `:8091` | **200**, `image/jpeg` |
| a private key through `/files/` | **404** (served only by the private pipeline) |
| `GET /api/v1/library/content` (anonymous) | **401** (auth required) |
| every distinct media key referenced by the corpus resolves on disk | **52/52** |
| the only missing keys | 4 **pre-existing** `private/portal-fixture-r2-*.pdf` rows (not demo rows) |

Private bytes live under the sibling root `/var/sanad/files-private` (48 files).

## Marker / state readback

| Check | Value |
| --- | --- |
| demo identities (`[demo1:<role>]`; 25 with `demo1.` email + 4 phone-only elderly/dependents) | **29** |
| demo posts (`SANAD DEMO` title) | **6** (1 anonymous) |
| demo post views (deduplicated) | **4** (3 on `named-family`, 1 on `named-owner2`; **0** of them by the post's author) |
| demo comments | **12** across 9 distinct demo authors |
| chat | 4 demo conversations (6 total) / 27 demo messages / 2 attachments / 2 reports / 1 block |
| demo notifications / email outbox | 12 / 3 |
| published legal docs | 2 (both carry `DEMO - not legal text`; section text `SANAD DEMO placeholder. Not legal text.`) |
| landing sections / wellness / FAQs / library | 6 / 6 / 8 / 6 (2 categories) |
| caregiver ratings (visible / hidden + moderation action) | 8 (7 / 1) |
| care-home facilities / inventory / visits / bookings | 5 / 3 room types · 6 rooms · 12 beds / 10 visits / 14 bookings |
| caregiver bookings | 14 (+8 prerequisite completed bookings for ratings) |
| duplicate-charge claims / complaint reviews | 4 / 4 |

### Seam-armed rows (why the seam stays enabled)

The money-state groups persist provider-call-armed end states whose outcomes are
pinned by the deployed allowlist only:
`care_homes.bookings.refund_status` distribution `{0:18, 1:3, 2:1, 3:1, 4:2, 5:1}`
and `finance.duplicate_charge_claims.state` `{1:2, 3:3}`, plus
`families.subscription_refund_requests` (1→3) and `finance.refund_ledgers` (1→7).
Armed rows exist, so the seam must remain enabled (see the note at the top).

## Authenticated per-role readback (one clean login per role)

`11` roles log in with the fixture password and read their own demo data; the
suspended account is refused.

| Role | Login | Representative reads |
| --- | --- | --- |
| `family.owner` | 200 | dependents 2 · current subscription 1 · invoices 8 · chat conversations 2 (`SANAD DEMO`) · community posts list 8 (6 of them `SANAD DEMO`) · unread notifications 1 |
| `family.member` | 200 | home sections 200 |
| `caregiver.1` (Medical, status 4) | 200 | profile (`SANAD DEMO`) · help-requests 0 |
| `caregiver.3` (Companion) | 200 | profile (`SANAD DEMO`) |
| `caregiver.5` (status 6 Rejected) | 200 | profile (`SANAD DEMO`) |
| `carehome.owner1` (facility status 5) | 200 | facility `mine` (`SANAD DEMO`) · inventory 6 |
| `carehome.owner5` (status 6) | 200 | facility `mine` (`SANAD DEMO`) |
| `support` | 200 | `/api/v1/support/tickets` 0 (partner-contact messages live in `identity.partner_contacts`, +3) |
| `superadmin` | 200 | statistics `totalUsers=53` · care-homes 8 · visits 11 · ratings 8 · posts 9 · legal docs 4 · landing sections 6 · chat reports 2 · duplicate-charge claims 5 · complaint reviews 6 |
| `content.admin` | 200 | library content 19 · wellness tips 12 |
| `finance.admin` | 200 | all `/api/v1/admin/finance/*` endpoints → **403** (account type is not finance-admin; `superadmin` reads them) |
| `suspended.family` | **403** | `Identity.Login.UserSuspended` (by design) |
| `elderly.1` (phone) | n/a | `User.CreateElderly` seeds no password → no password login (by design) |

Raw capture: `/root/demo-readback-d4.txt` on the VPS.

## Honest notes / limitations

- **Rehearsal left `Staging` + seam enabled** — intentional and required while the
  armed rows exist; not restored. Backup at `/etc/sanad/sanad.env.demo-d3-bak`.
  Reaping the armed rows and restoring `Production` is a future, separately
  authorized operator action.
- **Premium library items are correctly locked.** Two of the six library items
  (`article-2`, `video-1`) are marked premium. The frozen contract grants
  `PremiumContent` (benefit 8) **only** to the `PremiumPlus` plan
  (`SubscriptionPlan.Free`/`Premium` both set it unavailable; asserted in
  `SubscriptionValueObjectTests`). The corpus seeds only `free`/`premium`
  subscriptions, so no demo login can unlock them: the list/detail show them
  visible-but-locked and `GET /api/v1/library/content/{id}/video` returns the same
  uniform **404** it returns for every non-entitled caller (the route deliberately
  cannot be used to probe entitlement). This is the intended locked-state demo,
  not a defect; playable premium content would need a `PremiumPlus` subscription,
  which the pinned inventory (2 families / 6 subscriptions) does not include.
- **`finance.admin` is 403 on every finance-admin route** — its account type is
  not the finance-admin capability; `superadmin` reads those endpoints. Carried
  observation, unchanged by the corpus.
- **`community.Posts.ImageUrl` holds a raw private key for 2 demo rows.** Posts
  normally expose an image through `/api/v1/community/uploads/images/<id>/file`;
  the demo rows store the private storage key directly, so the image is not
  reachable through the post route. Cosmetic; the row is valid.
- **Acceptance bullet superseded.** The brief's "the single image and video resolve
  anonymously through `/files/...`" is superseded by the pinned decision that
  `video/mp4` is only accepted by the private writer: the video is reachable only
  through the authorized media route, not `/files/`.
- **4 pre-existing missing keys** — `private/portal-fixture-r2-*.pdf` referenced by
  pre-existing S3a facility documents; not demo rows, not touched.
- **Readback side effects (not corpus rows), cleaned.** Authenticated readback is
  not free: it created **59** `identity.device_sessions` rows on the demo accounts
  (which start session-free) and **1** append-only
  `families.admin_medication_access_audits` row (`SuperAdmin` `ListNotifications`
  at `2026-10-25T22:55:23Z`). Every demo device session was soft-revoked (mirroring
  `logout-all`) leaving **0 active** demo sessions; the audit row is append-only
  and is left in place. These do not affect the provisioner idempotency proof
  (which was measured before any readback: 8711 → 8711).
- Repeated automated readback can hit the login session cap
  (`DeviceSessionPolicy.MaximumActiveSessions = 5` → `Identity.Login.SessionLimitReached`,
  409). A demo operator logging in normally is unaffected.

## Boundaries respected

Additive only; no table decreased; no reset/truncate/drop; no provider call; no
clock change; no guard disablement; no commit, push or deploy. The only
configuration change is the recorded non-Production seam enablement above.

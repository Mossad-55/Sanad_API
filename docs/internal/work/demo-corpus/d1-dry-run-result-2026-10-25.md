# D1 result — demo corpus dry run on a throwaway nonce database

Status: **done**. Scope authority: [full-demo-corpus-2026-10-25.md](full-demo-corpus-2026-10-25.md)
and [d1-work-order-2026-10-25.md](d1-work-order-2026-10-25.md). D1 wrote **no** real
target: the only database touched was a disposable `SanadDemoNonce_<timestamp>`
on the local cluster, dropped at the end.

## Setup

| Item | Value |
| --- | --- |
| Nonce database | `SanadDemoNonce_20261025235332` (guard `^SanadDemoNonce_[0-9]{6,20}$`) |
| Server | `localhost:5432` (local `postgresql-x64-18`), cluster identity verified |
| Target kind | `demo-nonce` |
| Revision correlation | `local-dryrun` |
| Pinned instant | `2026-10-25T12:00:00Z` (every demo timestamp is relative to it) |
| Run tag / marker | `demo1` / `demo:demo1` |
| Upload root | a throwaway directory; public keys under `--storage-root`, private keys under its `-private` sibling |
| Media source | `docs/internal/tools/New-DemoMedia.ps1` output (`sanad-demo.jpg` 57178 B, `sanad-demo.mp4` 3585618 B, `sanad-demo-document.pdf` 6540 B) |
| Migration heads | all nine matched; `clusterIdentityVerified: true`, `tunnelDeclared: false` |

## Sequence and result

1. **verify** — exit 0; all nine module heads matched the pinned expectations,
   `pendingMigrations: 0` everywhere, cluster identity verified.
2. **plan** — exit 0, zero database access: **22 planned / 0 refused**. Plan
   execution order:
   `identity-accounts, families-people, families-subscriptions, caregivers-profiles,
   care-home-facilities, care-home-inventory, caregiver-bookings, caregiver-ratings,
   care-home-bookings, care-home-visits, finance-platform, finance-claims,
   finance-complaints, chat, community, cms-library, cms-wellness, cms-faq,
   cms-landing, cms-legal, cms-support, notifications`.
   Seam allowlist (**7** entries, printed byte-exact):
   `caregiver-bookings [seam-failed-refund]` → `Rejected`,
   `care-home-bookings [rejected-refund-pending]` → `Rejected`,
   `care-home-bookings [seam-failed-refund]` → `Rejected`,
   `finance-claims [submitted]` → `Rejected`,
   `finance-claims [approved]` → `Rejected`,
   `finance-claims [ambiguous]` → `Unknown`,
   `finance-claims [failed]` → `Rejected`.
3. **provision (run 1)** — exit 0, **all 22 groups provisioned, 0 unresolved**.
4. **provision (run 2, replay)** — exit 0, all 22 groups provisioned, 0 unresolved.

## Idempotency proof (row counts)

| Stage | Rows | Delta |
| --- | --- | --- |
| after `migrate` (migration history only) | 135 | — |
| after run 1 | **763** | +628 |
| after run 2 (replay) | **763** | **+0** |

`Compare-Object` of the full per-table count snapshot before/after the replay is
**empty**: the second run created zero rows and changed no identity. 82 tables
gained rows on run 1; none changed on run 2. Highlights (run 1):
`identity.users`/`identity.user_accounts` +29, `families.families` +8,
`families.family_subscriptions` +12 (exactly one current row per family),
`families.bookings` +22, `caregivers.caregivers` +6,
`caregivers.caregiver_ratings` +8, `care_homes.facilities` +5,
`care_homes.bookings` +14, `chat.Messages` +27,
`community.Posts` +6 (one anonymous), `community.PostViews` +4,
`community.Comments` +12, `finance.refund_ledgers` +6,
`finance.duplicate_charge_claims` +4, `finance.complaint_reviews` +4,
`families.subscription_invoices` +8, `cms.landing_sections` +6,
`cms.help_faqs` +8, `notifications.notifications` +12.

## Marker / state spot checks (run 1 database)

| Check | Value |
| --- | --- |
| demo posts (marker in title) | 6 |
| anonymous posts | 1 |
| post views (deduplicated) | 4 |
| community image rows | 1 |
| current subscriptions per family (max) | 1 |
| subscriptions total | 12 |
| legal documents published | 2 (`status = 2 Published`), section title `DEMO - not legal text` |
| landing sections | 6 |
| help FAQs | 8 |
| notifications | 12 (all addressed to demo accounts) |

## Media

Every key is produced by the real `LocalDiskFileStorage` into the target upload
root using the app's own folders; each produced key is re-opened through the same
abstraction (private) or confirmed at `root + key` (public) before it is allowed
into a row, so a missing/unopenable file fails the run. Bytes written on run 1:
public `library/<guid>.jpg`; private `care-home-media/<guid>.jpg`,
`library-video/<guid>.mp4`, `care-home-documents/<guid>.pdf`,
`subscription-invoices/<guid>.pdf` (app-generated invoice PDFs),
`chat-attachments/…`. The single generated JPEG is reused for every image field
and the single generated MP4/PDF for every video/document field.

## Defects found by the dry run (all fixed before the green run)

The nonce run's whole purpose was to surface runtime defects a code review cannot:
subscription `is_current` unique-index violation; Medical home-visit window placed
on a shift day; a Medical caregiver approved with no availability entry; companion
`EightHourDay` window not exactly 8 h; professional certificates added to a
Companion caregiver; visit reschedule flipping its source before creating the
replacement; article sections on a `Video` library item; a LINQ `Contains`
predicate EF could not translate; `SubscriptionInvoiceService` adds without
saving (last invoice never flushed); `refund_ambiguity_reason` `varchar(64)`
overflow; `masked_iban` `varchar(8)` overflow; a community-image row re-inserted
on every replay because the writer names each file with a fresh GUID (now reuses
the key the demo posts already carry); plus per-group rollback of unsaved tracked
changes so one failed group cannot poison the next, inner-exception-chain
diagnostics, and Postgres `Detail` surfaced on constraint violations.

## Defect fixed in D3: colon-free seam ids

The dry run above validated the allowlist **by group label**, so it did not surface
that the money-state provider ids contained `:`. The `FixtureRefundSeam` allowlist
**is** configuration (`FixtureRefundSeam:RefundOutcomes:<txnId>`) and the .NET
binder splits configuration keys on `:`, so on the deployed app those ids were
silently dropped — the gate that made the `--seam-deployed` declaration a lie was
invisible to the dry run. Proven directly: three such keys bound as `Count=1`, and
the `{ Key, Value }` array form bound as `Count=0`.

Fix (D3, 2026-10-26): the demo seam-facing ids were made **colon-free** (mirroring
the S3a `spf-…` shape) while the durable markers stay unchanged; the already-seeded
legacy capture id is still accepted on reuse so the local target stays idempotent.
Verification after the fix: strict build 0/0, 51/51 provisioner tests, the local
replay still zero-delta, and the deployed startup state line reports all 7 ids
(the D3 gate). The current allowlist (run tag `demo1`):

| Group | Allowlist id | Outcome |
| --- | --- | --- |
| caregiver-bookings | `demo-demo1-caregiver-bookings-seam-failed-refund-order-txn` | `Rejected` |
| care-home-bookings | `5359006666923497873` | `Rejected` |
| care-home-bookings | `2178478915071097933` | `Rejected` |
| finance-claims (submitted) | `demo-demo1-finance-claims-submitted-capture` | `Rejected` |
| finance-claims (approved) | `demo-demo1-finance-claims-approved-capture` | `Rejected` |
| finance-claims (ambiguous) | `demo-demo1-finance-claims-ambiguous-capture` | `Unknown` |
| finance-claims (failed) | `demo-demo1-finance-claims-failed-capture` | `Rejected` |

## Limitations

- A replay over a fully seeded database still re-writes **1 public and 2 private
  media files** (`library` cover, `care-home-media` cover, `library-video`) because
  those groups call the writer before the load-or-reuse check. It adds **no rows
  and changes no identity**; the orphan files are harmless. This is the only
  observable non-row side effect of a replay.
- The demo provisioner does **not** run the S3a refund-inertness invariant; the
  seam-gated groups only persist the armed rows whose outcomes D3 arms through the
  deployed `FixtureRefundSeam` allowlist.

## Handback for D2/D3

- D2 needs the Owner's recorded local-`SanadDb` override and the same command with
  `--target-kind local-demo --allow-local-sanad-db --run-tag demo1`, pointed at the
  real upload root.
- D3 needs the provisioner published `linux-x64` and run **on the VPS** (so media
  bytes land in the server's upload root), with the seam enabled and the deployed
  revision's allowlist matching the 7 entries above.
- No commit, push, deploy, VPS, provider call, clock change, or write to `SanadDb`,
  the rehearsal database, or any shared target occurred in D1.

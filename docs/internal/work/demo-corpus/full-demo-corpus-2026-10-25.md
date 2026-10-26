# Full demo corpus — approved scope (post-release slice)

## Control record

- Owner request (2026-10-25): seed a comprehensive fake dataset "all over, literally
  for everything", on the rehearsal VPS **and** the local `SanadDb`.
- Owner decisions: hybrid depth (several rows per list/state, one per rare state),
  generated `SANAD DEMO` media (one image + one video, reused everywhere), legal
  pages seeded as clearly marked demo text, implementation **after** the current
  portal second-pass release is deployed and verified.
- Cleanup execution (2026-10-26): the Owner approved the exact-list cleanup and it
  was executed and verified the same day - 29 of the 34 inventoried mis-invocation
  rows deleted, 6 rehearsal and 9 local orphan demo files deleted, 5 rows held for
  an Owner expansion decision (see "Cleanup execution" below and in the D4
  receipt). Nothing committed, pushed or deployed.
- Status: `approved` / `in_progress` (2026-10-25). D1–D4 provisional handback by a
  stopped worker failed review; the Owner redirected the remaining correction phase
  to the single continuing worker (2026-10-26):

  **Correction phase (2026-10-26, second round) — work complete, handed back
  `ready_for_review`, then ACCEPTED and CLOSED by the Owner/Mastermind on
  2026-10-26 (Owner verbatim to the final review report: "Accept it please";
  a status record only - it does not authorize commit, push, deploy,
  environment restoration or any new cleanup).** Base `9d92b820`,
  live API revision `8913df42` unchanged, nothing committed. The first-round
  handback was rejected; this round repairs the six findings and, critically,
  replaces the round's own false incident story with the evidence-corrected one
  (per-finding evidence in
  [d4-receipt-2026-10-26.md](d4-receipt-2026-10-26.md), "Correction phase"):
  1. Corrupted Arabic: source literals fixed; the identical guarded-UPDATE repair
     (`repair-arabic.sql`, MD5 `dfc33a71…`, 24,669 B both sides) applied to both
     targets — rehearsal 92 rows repaired, every residual 0; full-corpus scans on
     both targets **0 hits** before and after all replays; wire bytes real UTF-8
     (0x3F count 0). Row-level byte-identity of pre-existing rows remains
     **UNPROVEN** (never hash-sampled) — only additive-only semantics are claimed.
  2. Image route/likes: new posts write the authorized route URL
     `/api/v1/community/uploads/images/{id}/file`; raw-key `ImageUrl` rows **0**
     on both targets; image route 200 `image/jpeg` at **57,178 B** (the demo
     image) on BOTH targets — the earlier "rehearsal 866,519 B" was a
     misidentified pre-existing 2026-10-19 upload; `isLiked` true/false reads
     match `community."Interactions"`; comment like toggle proven both states,
     zero net DB change.
  3. Seam evidence upgraded: the deployed allowlist
     (`/root/sanad-api/appsettings.Staging.json`, `ASPNETCORE_ENVIRONMENT=Staging`)
     and the identical local `appsettings.Development.json` block (local-only
     config, no secrets) were verified against the **persisted armed ids on both
     targets** (payment transactions, the two care-home numeric bookings, four
     claim captures, and the linked duplicate-charge claims with their recorded
     gateway outcomes).
     **Review-round-two HIGH finding, repaired:** the previous "local keeps its
     pre-change colon-form caregiver seam id (terminal; the slug allowlist
     entry is a no-op locally)" claim was WRONG — `AdminBookingCommands`
     selects the first SUCCEEDED transaction and sends its stored id for the
     refund retry (payment success is eligibility, not refund terminality),
     the local seam-failed-refund slot was retryable, and its succeeded
     transaction still carried the legacy colon id the seam does not
     intercept (a retry would have passed through to the real gateway).
     Owner-authorized keyed repair applied on the local target (one PK-pinned,
     guard-locked `families.payment_transactions` UPDATE to the exact
     configured slug ids; `UPDATE 1`; unrelated rows preserved);
     post-repair read-only evaluation proves the effective retry id equals
     the allowlist entry on BOTH targets; the retry endpoint was never
     called; a fail-closed `SeamArmedIdMismatch` guard wired into the
     caregiver seam drive path now refuses any reused armed slot whose
     persisted ids mismatch the config (regression fact included).
     `systemctl is-active sanad-api` = active (read-only; the service was never
     stopped). "Local API processes" in earlier notes meant local test processes.
  4. Media hardening (Owner-instructed refuse semantics): `DemoCorpusMedia` now
     fingerprint-scopes `produced-keys.json` to one target+source set, re-validates
     recorded keys through the real storage implementation (repopulating length
     metadata), mirrors the writer's exact disk layout (full key under the
     `-private` sibling), and **REFUSES the run** on a broken recorded key instead
     of writing an unreferenced replacement (the previous fallback behavior was
     removed per the Owner's instruction). Byte trace sha256-proven on the
     rehearsal for all three demo files; the persisted video key ends `.mp4`
     (correcting the receipt's `….jpg` typo); playback in a player is NOT
     verified — stored+locked only.
  5. Rehearsal mis-invocation root cause CORRECTED (the previous
     "bash `$GROUPS` → `--groups 0`" story is false and retracted): the persisted
     report shows the invocation reached the provisioner with an **EMPTY group
     list**; the group gate only judges listed groups, so the portal-fixture
     (W5) path ran its base fixture with zero groups requested. Exact measured
     footprint: **27 `01a17593-*` rows** + **7 instant-stamped in-app
     notifications** (complete PK lists retained in
     `/root/demo-repair/misinvocation-inventory-2026-10-26.txt`), none deleted;
     **7 outbox rows were marked Sent (00:19:26-32 Z)** and at least one
     recipient is a real pre-existing non-demo address (`admin@gmail.com`);
     receipt OWNER-CONFIRMED (2026-10-26, Owner verbatim: "I've recieved all
     the emails actually"; distinct from the system observation "7 rows
     marked Sent"), no recipient contacted, and a read-only re-check
     confirms no accidental outbox row remains undispatched; 4 fixture
     marked Sent"), no recipient contacted, and a read-only
     document rows carry dangling keys (no file on disk). "Provider contact"
     refrains in this slice mean Paymob only. Fix shipped (uncommitted): a
     parse-time CLI gate (`ProvisionerCliGroups.cs` + `Program.cs`) — provision
     with an empty/missing `--groups` is refused (exit 3), unknown ids (incl. a
     bare `0`) refused, verify-without-`--groups` stays valid. Cleanup of the
     accidental rows/files needs the Owner's explicit decision.
  6. Tests/build: provisioner facts **64/64** (new regressions: reuse length
     metadata, missing/empty/altered/wrong-typed recorded files refuse with no
     replacement written, other-target sidecars ignored, traversal keys refused,
     CLI group-gate shapes, and the seam-armed-id mismatch refusal for a reused
     legacy caregiver slot), Release build 0/0.
  7. Docs corrected: D4 receipt rewritten with the evidence-corrected incident
     (counts: D3-final stored snapshot 8638 → post-incident+correction 8682,
     ∄), the brief (this block), and the handoff.
  Current changed files (all uncommitted): Mastermind_Handoff.md,
  full-demo-corpus brief, d4-receipt, Program.cs,
  ProvisionerCliGroups.cs (new), DemoCorpusMedia.cs (hardened),
  DemoCorpusGroups.cs + DemoCorpusProvisioner.cs (seam-armed-id guard) + the
  other DemoCorpus*.cs sources,
  DemoCorpusFacts.cs (64/64), `appsettings.Development.json`
  (local 7-id seam block). D4 was accepted and closed by the Owner/Mastermind
  on 2026-10-26 (status record only).

- Pinned revision at dispatch: `5377bbc9` (portal second-pass release verified;
  runtime `beb315fe`).
- Owner authorization (2026-10-25, after M1–M3 acceptance): full release and
  "create everything" for this phase, continuing without further check-ins. This
  covers commit/push/deploy of M1–M5, the D1 dry run, and the additive demo
  seeding of both approved targets, including the single recorded
  `--allow-local-sanad-db` override for the local `SanadDb` (additive only, no
  reset, singleton rows reused and never overwritten). It does **not** authorize
  changing the rehearsal runtime environment/configuration or enabling the
  refund seam, so money-state groups stopped at that owner decision — which the
  Owner then answered separately on 2026-10-26 (see Open items #3 and D3).
- Depends on: the fixture refund-outcome seam being enabled on each target before
  any money-state group is written, plus the Owner's local-`SanadDb` override
  approval before D2.
- Authority for target writes: per-target approval recorded in the task rows.
  Neither this document nor scope approval authorizes a target write, reset,
  provider call, clock change, commit, push or deploy.

## Non-negotiable boundaries

- Targets: rehearsal database `SanadDb` on `72.62.92.144` (through the identity-
  proven tunnel), and the **local** `SanadDb`, both via the existing
  `Sanad.PortalProvisioner` guards.
- Local `SanadDb` is normally refused by the provisioner. The corpus gets exactly
  one explicit override (`--allow-local-sanad-db`), accepted only with the demo
  run tag, `--target-kind local-demo`, the cluster-identity check, and a printed
  warning in the plan. It never applies to the report-fixture groups.
- Additive only: insert or reuse; never update or delete a row the corpus did not
  create. Existing singletons (plan versions, site settings, support contact,
  legal documents, landing sections) are reused when present and never
  overwritten; if a demo value is required and the singleton already exists, that
  group is skipped and reported.
- No Production, no reset/truncate/drop, no provider call, no clock change, no
  guard disablement, no service restart, no commit/push/deploy.
- Synthetic data only: invented people, addresses, phones, clinical notes and
  documents. No real personal data. Medical records remain id/ẓcontent fixtures
  with no real identities.
- Generated media only (no third-party stock): one JPEG with a visible
  `SANAD DEMO` mark and one short demo MP4, reused across every media field.
  Owner decision (2026-10-25): seeded media must follow the same workflow as a
  user upload — the bytes are written through the API's own storage
  implementation into the target's upload root, and the database stores the
  returned key. No invented paths, no blobs in the database, no new route, and
  no file placed behind the storage layer. Public image fields use the public
  writer and resolve at `/files/<key>`; the MP4 is `video/mp4`, which only the
  private writer accepts, so it is referenced only by private/video-capable
  fields and resolved through the authorized media route. A seeded reference
  whose file is missing or unopenable fails the run.
  Video frames cannot carry burned-in text without an encoder on this machine;
  the demo marking is carried by filename, title, description and the DEMO poster
  image. Installing `ffmpeg` would allow burned-in text if the Owner asks.
- Legal documents are published only with `DEMO — not legal text` in both the
  title and the body; they must never be mistaken for approved legal copy.
- Credentials for demo logins go only to the private file under
  `C:\Users\mosad\AppData\Local\Temp\opencode`; admin-type demo accounts are
  actors for data, not handed out.

## Corpus inventory (hybrid)

Deterministic identities keyed by tag `demo1`; reruns upsert, never duplicate.

| Group | Content | Count |
|---|---|---|
| `identity-accounts` | One account per role plus verified / unverified / suspended variants | 16 |
| `families-people` | 2 families, 4 dependents, medical access grants, device/session-free | 2 / 4 |
| `families-subscriptions` | Free, premium active, expired, cancelled, downgrade pending | 6 |
| `caregivers-profiles` | Approved, pending review, rejected, suspended; specialties, availability | 6 |
| `caregiver-ratings` | Visible and hidden ratings with bilingual review text | 8 |
| `care-home-facilities` | Draft, pending review, corrections requested, approved, suspended; documents, cover/logo media | 5 |
| `care-home-inventory` | Room types, rooms, beds, shared/private/suite | 3 / 6 / 12 |
| `care-home-visits` | Settings with operating hours, visiting windows, closures; visits pending/approved/rejected/cancelled/rescheduled | 1 / 10 |
| `care-home-bookings` | Every status: awaiting payment, awaiting decision, accepted, rejected, cancelled, refunded, completed, checked out; refund Pending/Failed/Completed; extension; dispute | 14 |
| `caregiver-bookings` | Every status incl. confirmed, in progress, completed, cancelled, refunds, payout, extension | 14 |
| `finance-platform` | Platform charge rules readouts, invoices, transaction rows paired to bookings | reuse / 8 |
| `finance-claims` | Duplicate-charge claims Submitted, Approved-awaiting-execution, Ambiguous, Failed; service-shutdown refunds | 4 / 2 |
| `finance-complaints` | Received, Investigating, Approved, Refused with award references | 4 |
| `chat` | 4 conversations (open, hidden, blocked, reported), 25–30 messages, 2 attachments, read marks | 4 threads |
| `community` | Posts, comments, reactions, one moderation removal with deletion audit | 6 / 12 |
| `cms-library` | Library image, video and article items; categories | 6 |
| `cms-wellness` | Wellness tips with free-text categories and the demo image | 6 |
| `cms-faq` | Published, hidden and deactivated FAQs with bilingual answers | 8 |
| `cms-landing` | Sections with image refs, testimonials and FAQ refs, ordered; stats | 6 |
| `cms-legal` | Privacy + Terms, published, `DEMO — not legal text` | 2 |
| `cms-support` | Support contact + 3 partner-contact messages | 1 / 3 |
| `notifications` | Every notification type, read and unread, both languages, email outbox rows | 12 |

Total: roughly 250–350 records plus media, excluding messages and ledger lines.

## Consistency rules the groups must satisfy

- One current subscription per family; claims reference their own capture and
  never compete with a shutdown refund for the same budget.
- Booking chronology is monotonic relative to the run instant: created → paid →
  decided → started → completed; refunds after payment, never before.
- Cancellation/refund reasons respect documented length caps (`≤500`, `≤1000`).
- Completed caregiver bookings carry a payout; completed care-home bookings have
  no open dispute.
- Every media field points at the single demo image/video pair; no dangling keys.
- Every seeded list row belongs to a seeded account, so role filters show data.
- Notifications are addressed only to demo accounts.
- Landing refs resolve to seeded testimonials/FAQs and stay ordered; hidden refs
  are hidden as intended.
- Legal pages are `Public` audience so the landing site can render them, and carry
  the demo marker in title and body.

## Execution stages (after the release)

| Task | Scope | Gate |
|---|---|---|
| D1 | Provisioner corpus groups + media generator + demo-mark indicators, local dry run on a throwaway nonce database | Code review; strict build; no target writes |
| D2 | Local `SanadDb`: identity proof, pre-run counts, plan review, provision, rerun, readback | Owner confirms local target and the override |
| D3 | Rehearsal `SanadDb`: SSH identity + cluster identifier, tunnel, verify, plan, provision, rerun, authenticated readback per role | Seam enabled and verified on the deployed revision |
| D4 | Receipt: counts, media URLs, per-role logins that work, demo markers confirmed, state coverage matrix per report item | Mastermind acceptance |

## Acceptance

- A second run changes zero row counts and zero identities.
- Pre-existing rows are byte-identical after the run (spot-checked counts and
  sampled rows).
- Each role can log in and see its own demo data; admin reads show the seeded
  states with the documented filters.
- The single image and video resolve anonymously through `/files/...` with the
  right content types, and every reference points at them. *(Superseded for the
  video by the pinned private-writer-only decision: public image via `/files/...`,
  MP4 via the authorized media route — see D4.)*
- No provider call occurs during provisioning or readback; money-state rows exist
  only where the seam is enabled.
- Legal pages visibly carry `DEMO — not legal text`.
- Nothing is committed, pushed or deployed by this slice.

## Open items

1. Owner confirmation to install `ffmpeg` if burned-in video text is required;
   otherwise the documented demo labelling stands.
2. ~~Final approval of the local `SanadDb` override before D2 writes.~~ **Approved
   2026-10-26 ("migrate + seed") and executed — see D2.**
3. ~~**D3 seam decision (open):** ...~~ **Answered 2026-10-26: "Enable non-Prod
   seam, seed all 22 (Recommended)."** Executed — the disposable rehearsal now runs
   `Staging` with the seam enabled and verified, all 22 groups seeded. The seam
   **stays** enabled while seam-armed rows exist (reaping + restoring `Production`
   is a future authorized operator action). The `FixtureRefundSeam` allowlist is
   configuration, and .NET configuration splits keys on `:`, so the demo
   seam-facing ids were made **colon-free** (see the D1 correction note and the D3
   result).

## Combined mobile report — approved contract

Read-only investigation session: `ses_e8c2165daffeZ0i6TZ9Au1QIn5`,
`river/zai-org/GLM-5.3-Flash#high`. The Owner approved the recommendations below
on 2026-10-25 ("Approved. and Delegate the workers."). These are the locked
product decisions for M1–M5; a worker that finds a genuine conflict with existing
behavior reports it instead of inventing a different policy.

### Locked decisions

**M1 — post views.** `viewsCount` is returned on post list, detail,
recommendations and saved/favorites, sourced from a stored counter (same numeric
type as the existing likes/comments counters). Recording route:
`POST /api/v1/community/posts/{postId}/view` under the same authorization and
visibility rules as the post detail read (401 unauthenticated, 404 when the post
is not visible to the caller, including moderator-hidden/removed posts). One
viewer counts at most once per post for the post's lifetime (unique
post+viewer record), the post's own author never increments it, and a repeated
request returns the current count without incrementing. Response carries the post
id and the current `viewsCount`. Anonymous posts count views without revealing
their authors. Counters never go negative and the counter change is atomic with
the viewer record.

**M2 — post author identifier.** The same post DTOs carry `authorId`: the stored
author id for non-anonymous posts, and **always `null`** for anonymous posts —
for every caller, including the author and admins. No other identity field is
added.

**M3 — comment viewer state.** `GET /api/v1/community/posts/{postId}/comments`
items (and replies, consistently) carry `isOwn`, `canEdit`, `canDelete` and
`isLiked`. `isOwn` is true only when the authenticated caller is the stored
comment author; on anonymous comments it reveals nothing to other callers.
`canEdit`/`canDelete` mirror the **actual** current mutation authorization
(ownership, moderation/admin capability, and post/comment state), so they are
false whenever the corresponding mutation would be rejected.
`isLiked` reflects the caller's persisted like. No author identity is added to
anonymous comments.

**M4 — retry-safe creates.** Optional `Idempotency-Key` header on exactly:
`POST /api/v1/family/bookings/checkout`,
`POST /api/v1/family/care-home-bookings/checkout`,
`POST /api/v1/family/dependents/{dependentId}/care-records` and
`POST /api/v1/caregiver/care-records`. Absent header = today's behavior. Key:
1–128 printable ASCII, trimmed; otherwise a coded 400. Records are scoped to the
authenticated actor, the operation and the key, and stored in the same
persistence context as the write so the key and the created row commit
atomically. The request fingerprint covers the canonical request body plus every
route/path identifier. Same key + same fingerprint replays the original status
and body without creating another hold, record, audit entry or provider call;
same key + different fingerprint is a coded 409. Concurrent duplicates execute
exactly once (unique constraint; the loser replays). Authorization is re-checked
before any replay. Retention is 7 days, documented; no purge worker is added in
this slice. Payment-intent creation is a separate operation and stays out of
scope.

**M5 — date-specific availability.** A read endpoint accepts `date` and
`shiftType` and returns `timeZoneId` plus the available start/end intervals for
that date, in addition to the existing weekly schedule endpoint (additive; the
weekly contract is not removed or relabeled). Intervals exclude times outside the
caregiver's availability, manual unavailability, active booking overlaps and
temporary holds as **currently** defined by the code — no new expiry or timeout
is invented, and pending bookings are not blanket-treated as expired. Cross-
midnight and daylight-saving transitions are handled by the resolved IANA zone.
Checkout uses the same rule set and a final conflict-safe check inside its
transaction, rejecting unavailable slots with a coded error. Stored UTC
timestamps are not relabeled. The response is advisory, not a reservation.
`Africa/Cairo` is the documented display zone for demo fixtures; the owning
domain's stored zone semantics decide the authoritative answer.

### Implementation tasks

| Task | State | Scope / gate |
|---|---|---|
| M1 | `completed` | Accepted 2026-10-25 after diff review: `Post.ViewsCount` + `PostView` (`(PostId, ViewerUserId)` unique, cascade FK), `RecordPostViewCommand` reusing the detail visibility predicate (Published, not blocked), author excluded, repeat returns current count, one `SaveChangesAsync` for row+counter, Community migration `20261025170718` (generated, not applied), `viewsCount` on the shared post DTO, docs + Postman, 7 unit facts + HTTP 401/404/200 facts. |
| M2 | `completed` | Accepted: `authorId` on the same post DTO, stored id for named posts and `null` for anonymous for every caller; covered by the same facts. |
| M3 | `completed` | Accepted: `isOwn`/`canEdit`/`canDelete`/`isLiked` on comments, replies and create/update responses; `canEdit` = the update handler's ownership gate, `canDelete` = ownership + no replies (the delete handler's `CommentHasReplies`), replies expose no edit/delete capability, likes and reply-existence batched per page. |
| M4 | `completed` | Accepted 2026-10-25 after diff review: optional header on exactly the four creates, one-key parse with coded 400, canonical fingerprint (route ids in, server-derived values out), per-context `idempotency_requests` with actor+operation+key uniqueness, stored byte-exact replay, 409 on mismatch, loser detach/replay, care-home lookup inside its serializable guard. Gate-vs-replay ordering accepted as implemented (confirmed replay keeps its original answer; maintenance still blocks a new checkout). Mastermind fixed one corrupted comment block and one stale test double; strict build 0/0, full suite 5688/0/27 skipped, migration tests green. |
| M5 | `completed` | Accepted: additive `availability/day` with `Africa/Cairo` and UTC intervals, one shared rule object for read and checkout, busy windows through the API port using the exact checkout predicate, fixed-shift drop vs hourly subtraction, DST gap omission/standard-offset resolution, serializable + advisory-lock checkout guard, `Bookings.AvailabilityConflict` distinct from `ScheduleConflict`. Cross-midnight predicate gap mirrored and documented; no hold/expiry invented. |
| D1 | `done` | All 22 corpus groups + media generator + demo markers implemented; **D1 dry run green on the throwaway nonce database** (see below): plan 22 planned / 0 refused / 7-entry seam allowlist; provision exit 0 all 22 groups; replay exit 0 with **zero** row delta (135 → 763 → 763). Gate: code review, strict build 0/0, 51/51 provisioner tests, no real target written. |
| D2 | `done` | **Local `SanadDb` seeded (Owner approved "migrate + seed").** Three pending migrations applied (care_homes, community, families) to reach the demo heads; plan exit 0 (22/0, override warning printed); provision run 1 exit 0 all 22 groups; replay exit 0 with **zero** row delta (886 → 1494 → 1494, no table decreased). 29 demo identities, markers, media and legal marker confirmed. Evidence: [d2-local-sanaddb-result-2026-10-26.md](d2-local-sanaddb-result-2026-10-26.md). |
| D3 | `done` | **Rehearsal `SanadDb` seeded.** Owner answered the seam decision ("Enable non-Prod seam, seed all 22"); the disposable rehearsal was flipped to a non-Production environment, the seam enabled with the 7 exact colon-free ids and verified from the startup state line, then all 22 groups were seeded. verify/plan green; provision exit 0 all 22; replay exit 0 with **zero** row delta (8028 → 8711 → 8711, 75 tables changed, none decreased). Authenticated readback: 11 roles log in and see demo data, `suspended.family` 403. Full evidence: [d3-rehearsal-result-2026-10-26.md](d3-rehearsal-result-2026-10-26.md). |
| D4 | `ACCEPTED` (Owner/Mastermind, 2026-10-26) | Receipt: [d4-receipt-2026-10-26.md](d4-receipt-2026-10-26.md) — counts, media URLs/content types, working per-role logins, demo markers, and the per-report-item (M1–M5) state-coverage matrix, with the acceptance checklist and the carried notes. Acceptance is a status record only (no commit/push/deploy/restore/new-cleanup authorized). |

### D1 dry-run result (2026-10-25)

Green on the throwaway nonce database: plan 22 planned / 0 refused with the
7-entry seam allowlist; provision exit 0 with all 22 groups; replay exit 0 with
**zero** row delta (135 → 763 → 763, 82 tables gained rows on run 1). Media bytes
were written by the real `LocalDiskFileStorage` into the target upload root.
No real target was written; the nonce database was dropped. Full evidence,
defect list and limitations: [d1-dry-run-result-2026-10-25.md](d1-dry-run-result-2026-10-25.md).

> **D1 correction (D3, 2026-10-26).** The D1 allowlist was printed by *group label*,
> not by raw id, so it did not surface that the money-state provider ids contained
> `:`. The `FixtureRefundSeam` allowlist **is** configuration, and the .NET binder
> splits configuration keys on `:`, so those entries were silently dropped on the
> deployed app (proven: 3 keys bound as `Count=1`; the `{Key,Value}` array form
> bound as `Count=0`). Fix: make the demo seam-facing ids **colon-free** (mirroring
> the S3a `spf-…` shape) while keeping the durable markers unchanged; the already
> seeded legacy capture id is still accepted on reuse so the local target stays
> idempotent. After the fix: strict build 0/0, 51/51 provisioner tests, local replay
> still zero-delta, and the deployed state line reports all 7 ids (D3 gate).

### D2 local `SanadDb` result (2026-10-26)

Green on the real local `SanadDb` under the single approved `--allow-local-sanad-db`
override. The three pending module migrations were applied first (care_homes,
community, families) so the demo heads matched, then the additive seed: plan exit 0
(22 planned / 0 refused, override warning printed), provision run 1 exit 0 with all
22 groups, replay exit 0 with **zero** row delta (886 → 1494 → 1494) and no table
decreased. 29 demo identities (25 with `demo1.` email + 4 phone-only
elderly/dependents), 6 demo posts (1 anonymous), 4 post views, 6 landing sections,
2 published legal docs (`DEMO - not legal text`), 27 chat messages, 12 visits;
media landed through the real `LocalDiskFileStorage` into
`src/API/Sanad.API/bin/Release/net10.0/sanad-files`. Additive only — no reset, no
deletion. Full evidence: [d2-local-sanaddb-result-2026-10-26.md](d2-local-sanaddb-result-2026-10-26.md).

### D3 rehearsal `SanadDb` result (2026-10-26)

Green on the rehearsal VPS under the Owner's answered seam decision. The disposable
rehearsal was flipped from `Production` to `Staging` (backup kept), the refund seam
enabled with the plan's 7 exact colon-free ids, and the startup state line verified
`enabled=True, allowlist entries=7` (prior processes logged `enabled=False`). Then:
verify exit 0 (identity + 9/9 heads), plan 22 planned / 0 refused, provision run 1
exit 0 all 22 groups, replay exit 0 with **zero** row delta (8028 → 8711 → 8711, 75
tables changed, none decreased, +29 identities). Media written through the real
`LocalDiskFileStorage` into `/var/sanad/files` (+ `-private`); the public image
resolves anonymously at `/files/…` and all 52 corpus keys resolve. Authenticated
readback: 11 roles log in and see their demo data, `suspended.family` 403. The
rehearsal **stays** `Staging` with the seam enabled while seam-armed rows exist.
Full evidence: [d3-rehearsal-result-2026-10-26.md](d3-rehearsal-result-2026-10-26.md).

### D4 receipt (2026-10-26)

Both targets seeded and receipted: local `SanadDb` (886 → 1494 → 1494) and rehearsal
`SanadDb` (8028 → 8711 → 8711), replay delta **+0** on both, no table decreased.
Counts, media URLs/content types, the working per-role login table, the demo
markers, and the per-report-item (M1–M5) state-coverage matrix are in
[d4-receipt-2026-10-26.md](d4-receipt-2026-10-26.md). Acceptance checklist met
(with the pinned correction that the video is private-writer-only, not `/files/`).
Nothing committed, pushed or deployed.

**Slice CLOSED:** D1–D4 were ACCEPTED by the Owner/Mastermind on 2026-10-26
(Owner verbatim to the parent's final review report: "Accept it please"). This
brief remains the authoritative scope/history document for the slice; the
private Mastermind handoff records the closure and the remaining
separately-authorized optional actions (commit/push of the uncommitted slice
work, the future rehearsal Production restore, and the regenerated corpus
notifications kept as intended corpus content). Acceptance is a status record
only - it does NOT authorize commit, push, deploy, environment restoration or
any new cleanup.
### Cleanup execution (2026-10-26, Owner-approved)

The Owner approved the exact-list cleanup ("I approve") and it was executed and
verified the same day. Rows: 22 actual incident rows + 7 in-app notifications
(approved at the time; later shown to be CORPUS-keyed - see below) + outbox 7,
documents 4, beds/rooms/room_types/review_history/profile_revisions/
facilities/elderly 1 each, 2 users + 2 user_accounts, in one explicit-PK
transaction with rollback-guard assertions. The 5 remaining incident rows were
initially HELD because deleting the fixture caregiver cascades into 7
unapproved availability rows the correction run attached to it; the Owner then
approved the exact 12-row block (verbatim "Approved") and it was executed the
same day with guarded, rollback-protected transaction deletes, full-column
archives and exact pre/post 177-table count snapshots - the delta was exactly
the 12 rows over 10 tables and nothing else (a first dispatch attempt died
instantly on a provider quota error; target state was verified untouched
before re-execution). Files: all 6 rehearsal orphan
media files and all 9 local orphan demo media files deleted after fresh
0-reference rechecks (18 columns on the rehearsal, 19 locally, including
CommunityImages, community.Posts.ImageUrl and chat.Attachments), sha256 gates
and private archives; the 2 non-demo landing images untouched; the rehearsal
produced-keys.json realigned to corpus-referenced, DB-referenced artifacts with
byte-identical fixture content; no produced-keys.json exists locally.
Verification: incident rows 0, the demo family/caregiver/media preserved
(5 corpus facilities, 19 library contents, seam rehearsal booking), service
active at revision 8913df42 with the 7-id seam intact, live-operator drift on
the rehearsal documented and reconciled - including 7 in-app notifications
that the cleanup's own deletion RE-CREATED via the notification writer
`CareHomeReviewNotificationProcessor` (a `BackgroundService` polling every
20 s that materializes care-home submission/review source rows with the
SOURCE EVENT's timestamp and de-duplicates only by idempotency key; the 7
re-created rows carry keys identical to the deleted rows' keys, so they are
re-materializations, not a demo generator and not operator content; deleting
them again while the corpus source rows persist will repeat the cycle), one
real care-home approval at 02:38:24, and an attribution correction: the
first-pass 7 in-app deletions were corpus-keyed (a historical mistaken
deletion of intended corpus notifications - the processor re-created them from
surviving source rows and they are preserved), while the ACTUAL 7 incident
in-app notifications, after being left orphaned for a separate decision, were
deleted on the Owner's explicit final approval ("Yes I apprrove") by exact
GUID with the corpus notifications untouched. The outbox 65-vs-64 gap was
resolved: the 65th row is the 02:38:24 operator approval email, created
seconds after the 02:38 post-scan. Cumulative: all 34 original accidental
rows + the 7 approved availability dependents = 41 unintended rows removed;
nothing remains pending on this cleanup. Absence re-verified after multiple
processor poll cycles. Nothing committed, pushed or deployed. Full evidence,
counts and recovery paths: the D4 receipt, "Cleanup execution" (including
"Final-7 incident notification cleanup").

### M1–M3 worker handback (2026-10-25)

Session: this Sanad phase worker session (opencode `deepseek-v4.1-flash-free`), base
revision `5377bbc9`, nothing committed. Scope: Community module only, plus the
shared id primitive, tests, app docs and the app Community Postman collection.
M4–M5, admin DTOs/routes, the private handoff and pre-existing untracked debris are
untouched.

Changed files:

- New: `src/BuildingBlocks/Domain/.../Primitives/Ids/CommunityPostViewId.cs`,
  `src/Modules/Community/Domain/.../Posts/PostView.cs`,
  `src/Modules/Community/Application/.../Posts/RecordPostViewCommand.cs`,
  `src/Modules/Community/Application/.../Comments/GetCommunityCommentViewerStateQuery.cs`,
  `src/Modules/Community/Infrastructure/.../Persistence/Configurations/PostViewConfiguration.cs`,
  `src/Modules/Community/Infrastructure/.../Persistence/Migrations/20261025170718_AddCommunityPostViews.cs`
  (+ `.Designer.cs`),
  `tests/Sanad.UnitTests/Community/CommunityPostViewsAndCommentViewerStateTests.cs`
  (7 facts), `tests/Sanad.UnitTests/API/CommunityPostViewApiTests.cs` (3 HTTP facts).
- Modified: `Post.cs` (`ViewsCount`, `IncrementViews`), `ICommunityDbContext.cs`
  and `CommunityDbContext.cs` (`PostViews`), the Community model snapshot,
  `CommunityPostsController.cs` (view route, `viewsCount`/`authorId`,
  `CommunityPostViewResponse`), `CommentsController.cs` (batched viewer state and
  the four flags on comments/replies incl. create/update responses),
  `CommunityFeedTests.cs` + `AdminCommunityRestrictionQueryTests.cs` (test doubles),
  `NewEndpointsAuthorizationContractTests.cs` (new `RecordView` 401 row).
- Docs/Postman: `docs/app/community/writes.md` (post views, `authorId`, anonymity,
  comment/reply viewer state), `docs/app/community/README.md`, and
  `docs/postman/app/Sanad.App.Community.postman_collection.json` (new `Record post view`
  item plus updated post/comment/reply descriptions); JSON validated.

Migration: `20261025170718_AddCommunityPostViews` — adds
`community.Posts.ViewsCount integer NOT NULL DEFAULT 0` (monotonic, increment-only)
and the `community.PostViews` table with unique index `(PostId, ViewerUserId)`,
cascade FK to `Posts`, `ViewedOnUtc timestamp with time zone`. Generated at design
time with a dummy connection string; **not applied** to any database.

Design summary: one `SaveChangesAsync` writes the viewer row and the counter
increment together; `DbUpdateException` re-checks whether the concurrent winner's
row exists and returns the current count only then. Visibility/authorization is the
post-detail predicate (`Published`, minus blocked authors); author reads return the
current count without a row. Reads share the single `CommunityPostResponse`
serializer, so all surfaces carry `viewsCount` and `authorId`. Comment
`canEdit` = ownership (the update route's only gate), `canDelete` = ownership AND
no replies (mirrors the `CommentHasReplies` 409), replies always
`canEdit`/`canDelete` = false (no reply mutation route), `isLiked` from persisted
likes.

Checks run (all in this working tree):

- `dotnet build Sanad.slnx -c Release -warnaserror --nologo` → **Build succeeded,
  0 Warning(s), 0 Error(s)**.
- `dotnet test tests\Sanad.UnitTests\Sanad.UnitTests.csproj -c Release --no-build
  --filter "FullyQualifiedName~Sanad.UnitTests.Community|FullyQualifiedName~CommunityPostViewApiTests|FullyQualifiedName~NewEndpointsAuthorizationContractTests"`
  → **Passed 219, Failed 0, Skipped 0**.
- Postman JSON: `[System.IO.File]::ReadAllText(...) | ConvertFrom-Json` → **VALID
  JSON**, 6 folders, 18 items in `01. Posts and interactions`.
- `git diff --check` → clean (only git's LF→CRLF normalization warnings).

Not run / limitations: full solution test suite; any live Postman request (no target
writes authorized); the migration against a database; a live concurrent duplicated
first-view race (the unique index is the backstop, the facts cover the sequential
dedup path). Admin Community DTOs/routes are unchanged — `viewsCount`/`authorId`
live on the app DTOs that admin app-surface reads also use. Reply `isLiked` is
data-only because no reply-like route exists. `favoritesCount`/`likesCount`/
`commentsCount` behavior and every existing status code are unchanged.

### M4–M5 worker handback (2026-10-25)

Session: this Sanad phase worker session (opencode `deepseek-v4.1-flash-free`), base
revision `5377bbc9`, nothing committed, no migration applied to any database, no
provider/target call. Scope: M4 retry-safe creates on exactly the four contracted
routes and M5 date-specific availability plus the conflict-safe checkout check.
M1–M3 Community files, admin DTOs/routes, the private handoff and the pre-existing
untracked debris are untouched.

Changed files:

- New shared kit: `src/BuildingBlocks/Application/Sanad.BuildingBlocks.Application/Idempotency/`
  (`IdempotencyKey`, `IdempotencyFingerprint`, `IdempotencyRequest`,
  `IdempotentOutcome`, `IdempotencyOperations`, `IIdempotentResponseSnapshotter`)
  and `src/BuildingBlocks/Infrastructure/.../Persistence/PostgresErrors.cs`.
- New API seams: `src/API/Sanad.API/Idempotency/MvcIdempotentResponseSnapshotter.cs`,
  `src/API/Sanad.API/CaregiversIntegration/ApiCaregiverBusyBookingLookup.cs`,
  `.../CaregiverAvailabilityRulesGateway.cs`.
- New per-context M4 persistence: `Families|CareHomes.Domain/Idempotency/IdempotencyRequest.cs`,
  `.../Configurations/IdempotencyRequestConfiguration.cs`, plus the unapplied
  migrations `20261025175627_AddIdempotencyRequests` (schema `families`) and
  `20261025175720_AddIdempotencyRequests` (schema `care_homes`) with `.Designer.cs`.
- New M5 rules: `Caregivers.Application/Availability/` (`CaregiverDayAvailability`,
  `CaregiverDayAvailabilityRules`, `CaregiverAvailabilityTimeZone`,
  `CheckCaregiverAvailabilityQuery`, `GetCaregiverDayAvailabilityQueryHandler`),
  `Caregivers.Application/Abstractions/Data/ICaregiverBusyBookingLookup.cs`;
  `Families.Application/Abstractions/Caregivers/ICaregiverAvailabilityRules.cs`,
  `Families.Application/Abstractions/Bookings/ICaregiverBookingCheckoutGuard.cs`,
  `Families.Infrastructure/Bookings/CaregiverBookingReservationGuard.cs`.
- New tests: `tests/Sanad.UnitTests/BuildingBlocks/IdempotencyKeyTests.cs`,
  `BuildingBlocks/IdempotencyFingerprintTests.cs`, `API/IdempotencyHeaderContractTests.cs`,
  `Families/Idempotency/FamiliesCheckoutIdempotencyTests.cs`,
  `Families/Idempotency/FamilyCareRecordIdempotencyTests.cs`,
  `CareHomes/CareHomeBookingCheckoutIdempotencyTests.cs`,
  `Caregivers/CaregiverDayAvailabilityTests.cs`,
  `Families/Bookings/BookingCheckoutAvailabilityTests.cs`,
  `Persistence/IdempotencyMigrationTests.cs`, `Support/IdempotencyTestDoubles.cs`.
- New doc: `docs/app/families/care-records.md` (both create endpoints + retry rules).
- Modified: `ApiControllerBase.cs` (one header parse + `IdempotentOk`/`IdempotentCreated`
  replay helpers), the four controllers, `DependencyInjection.cs`,
  `ResultProblemDetailsMapper.cs` (the eight codes), both `DbContext`s and
  `I*DbContext` (`IdempotencyRequests`, `DetachAdded`), the two model snapshots,
  the four handlers/commands, `CaregiverAvailabilityController.cs` (day route), the
  two `.csproj` files (`BuildingBlocks.Infrastructure` reference),
  `NewEndpointsAuthorizationContractTests.cs`, `docs/app/families/bookings.md`,
  `docs/app/caregivers/schedule-and-availability.md`,
  `docs/care-homes/booking-lifecycle.md`, and the four app Postman collections.

M4 behavior: absent header runs the current code path (byte-identical responses);
`Idempotency-Key` is trimmed and must be 1–128 printable ASCII, else
`*.IdempotencyKeyInvalid` (400) with no row written; the key is scoped to
`(actor, operation, key)` with operation strings `family.bookings.checkout`,
`family.care-home-bookings.checkout`, `family.care-records.create` and
`caregiver.care-records.create`; the fingerprint covers the canonical body plus
every route/path identifier and no server-derived value (no `UtcNow`, ids, or
resolved prices); replay returns the stored status/body snapshot with no second
hold, record, audit entry or provider call; same key with a different fingerprint
is `*.IdempotencyConflict` (409). The row is added to the same `SaveChangesAsync`
as the business write; a unique violation detaches the added row and replays the
winner. Retention is 7 days documented only — no purge worker, `created_on_utc`
indexed.

M5 behavior: additive `GET /api/v1/caregivers/{caregiverId:guid}/availability/day?date=&shiftType=`
returns `timeZoneId` (`Africa/Cairo`), the requested `shiftType` and UTC
`intervals`; the weekly endpoint is unchanged. One shared rule object serves the
read and the checkout check. Busy windows come from a new API-level port using
exactly the current checkout status/overlap predicate — no hold or expiry concept
was invented, and pending bookings are not blanket-treated as expired. DST gap
times are omitted; ambiguous fall-back resolves to the standard offset. Checkout
now runs exact containment of the booked window inside declared availability
before the existing overlap check (so overlap diagnostics keep precedence) inside
a serializable + advisory-lock guard: outside availability →
`Bookings.AvailabilityConflict` (409); provider-busy and serialization races →
`Bookings.ScheduleConflict`.

Reported ordering conflict (not a silent policy change): the pinned decision says
the maintenance gate runs before lookup and is skipped only on a confirmed replay.
The handlers therefore evaluate the gate, but when a valid header is present they
hold a gate error until after the lookup so a confirmed replay still returns its
stored response instead of a maintenance 503; without a header the gate returns
immediately exactly as before. Care-home checkout additionally keeps its local
facility/room-type/media validation ahead of the guard and the deferred gate, and
catches the unique violation outside the guard. A replay is documented as
historical, not a new offer. If the Mastermind wants maintenance to win over
replay, that is a two-line reorder — flag it as a deviation to accept or change.

Checks run (all in this working tree):

- `dotnet build Sanad.slnx -c Release -warnaserror --nologo -v q` → **Build
  succeeded, 0 Warning(s), 0 Error(s)**.
- Focused `dotnet test` filter (Idempotency tests +
  `BookingCheckoutAvailabilityTests` + `CaregiverDayAvailabilityTests` +
  `NewEndpointsAuthorizationContractTests` + `BookingRemediationTests` +
  `CareHomeBookingLifecycleTests` + `CareRecordHandlerTests` +
  `BookingPaymentIntentTests`) → **213 passed, 0 failed**; the
  idempotency-only run earlier → **70 passed**; the availability/migration run
  earlier → **26 passed**.
- `docs/internal/tools/Verify-ApiContractMapping.ps1` → controller signatures
  673, Postman requests 772, **missing 0, orphan 0**; all four edited collections
  parse with `ConvertFrom-Json` → **valid JSON**.
- `git diff --check` → clean (only git's LF→CRLF normalization advisories).

Not run / limitations: the full solution test suite (not authorized by the
contract); any live request against a target environment; the two migrations
against a real database (generated at design time with dummy connection strings,
**not applied**); a live concurrent duplicate through Postgres (the unique index
is the backstop, and the loser path is covered with a SQLite adapter that commits
a competing winner row). Availability subtraction works in local tick space using
the literal checkout predicate, so a booking that starts inside a cross-midnight
window is still not detected as busy — the pre-existing gap is mirrored and
documented in code, tests and docs rather than fixed here.

### Execution order

1. M1–M3 implemented by the first worker; M4–M5 follow in a second worker after
   M1–M3 are reviewed, because both touch the same solution build.
2. Mastermind integrated gate, then a separate release approval.
3. D1 dry run on a disposable database; then D2 local and D3 rehearsal seeding
   under their own approvals; D4 receipt.

D1–D4 are complete. The rehearsal service now runs `Staging` with the refund seam
enabled — the Owner's answered D3 decision — and stays that way while seam-armed
fixture rows exist; restoring `Production` is a future authorized operator action
after those rows are reaped. No provider call was made and no release authorization
is implied.

### M4–M5 pinned implementation decisions (Mastermind, 2026-10-25)

Contract investigation: `ses_e8bfdb7aeffeGmASgAWQMSc1p4`. Accepted defaults:

- Replay returns a stored byte-exact response snapshot (`text`, not `jsonb`),
  serialized with MVC's own `JsonOptions`; never recomputed from the mutated
  business row.
- Idempotency rows live in each writing context (`families.idempotency_requests`,
  `care_homes.idempotency_requests`), keyed `(actor_id, operation,
  idempotency_key)` with the created `resource_id`, response status/body and
  `created_on_utc`; no FK to the business row.
- Absent header = byte-identical current behavior; key parse happens once in the
  controllers (1–128 printable ASCII, trimmed) with a coded 400; rejected
  attempts never write a row.
- Authorization and the platform-maintenance gate run before lookup; the gate is
  skipped only on a confirmed replay, and a replay is documented as historical,
  not a new offer.
- Retention is documentation-only: no expiry logic in code, `created_on_utc`
  indexed for a future purge.
- Availability is reported in `Africa/Cairo` for the caregiver-scoped endpoint;
  the wire `shiftType` is the existing `BookingShiftType`, mapped to declared
  windows by kind/duration. Busy windows use exactly the current checkout
  predicate and statuses; no hold/expiry concept is invented.
- Checkout uses a serializable transaction plus advisory lock and rejects a
  slot outside declared availability with a new `Bookings.AvailabilityConflict`
  (409), distinct from `Bookings.ScheduleConflict`; provider-busy races map to
  `ScheduleConflict`.
- The existing cross-midnight gap in the overlap predicate is mirrored exactly
  and documented, not silently fixed in this slice.

### Release (2026-10-25)

- **Release state: `verified`** for final revision
  `8913df42aadc519c8deee4de1a16ee56fabf2e67` (`456858d8..8913df42`, 2 files, no
  migration change), live at `http://72.62.92.144:8091` on service `sanad-api`, runtime
  `Production`. Feature revision `456858d8` (`5377bbc9..456858d8`) was deployed first;
  the fix redeploy used the same procedure. Pass two: publisher 0/0, archive SHA-256
  `914aedcb4924e7bc8ec30288acb1016d86047821370f4181081f561eb3d2c3f2`, validated
  `SanadDb` dump (833,385 bytes, 907-entry TOC) and app-directory backup, config
  fingerprints unchanged, service active/running with `NRestarts=0`, **0 migrations
  applied** and required heads unchanged (9/9, 0 pending), verifier exit 0, refund seam
  absent/disabled.
- The `456858d8` pass-1 role readbacks passed (post views/`authorId`, comment viewer
  state, day availability, support authorization); its one recorded FAIL — replay body
  not byte-identical to the fresh 201 (445 vs 473 bytes, `\uXXXX` escaping) — is
  **fixed and verified on `8913df42`**: Arabic hospital name + `<b>ملاحظة</b>` details,
  fresh and replay both **201, 440 bytes, `byte_equal=true`**, raw UTF-8 with no `\u`
  escapes, one stored row, and **409** `Families.CareRecords.IdempotencyConflict` on a
  changed payload. Community view/comments, day availability and support 403 were
  re-checked after the redeploy with no regression.
- Receipt: [mobile-report-release-2026-10-25.md](../portal-api-reconciliation/reports/mobile-report-release-2026-10-25.md).
- Synthetic release smoke writes (documented in the receipt, additive): one post view on
  `01a15588-3d20-78ee-a67b-7aa522aea4dc`; three `SANAD RELEASE CHECK Hospital` family
  care records `01a17478-d5d9-7e75-9931-31daca20e923`,
  `01a17479-2742-7a64-81b1-e5fb89c81f73`, `01a1747a-7eeb-71d6-a121-588e617edd23`; and
  the byte-identity check record `01a17485-010a-7fc4-81fd-cfeb09d31cfb`
  (`مستشفى سند التجريبي`, details `SANAD DEMO — <b>ملاحظة</b> للمراجعة`), all for
  dependent `01a17319-d059-7b7b-b92d-94e7ec18d0a4` with matching idempotency rows.
  D1-D4 must reuse/skip them, never delete them.
- D3 ran on this revision. The Owner settled the non-Production seam decision on
  2026-10-26; the seam was enabled with the 7 exact ids and verified from the startup
  state line (see D3), after which all 22 groups — money-state included — were seeded
  additively. The release itself did not change the seam.

# D2 result — additive demo corpus on the local `SanadDb`

Status: **done** (Owner approved "migrate + seed" on 2026-10-26). Scope authority:
[full-demo-corpus-2026-10-25.md](full-demo-corpus-2026-10-25.md) and
[d1-work-order-2026-10-25.md](d1-work-order-2026-10-25.md). This is the single
recorded `--allow-local-sanad-db` override; the local `SanadDb` is a real
(non-disposable) database, so the run was additive only — no reset, no deletion,
singletons reused, never overwritten.

## Preconditions applied first

The local `SanadDb` was three modules behind the demo-corpus heads, so the three
pending migrations were applied before any seed (Owner-approved):

| module | before | after |
| --- | --- | --- |
| care_homes | `20261022213000_AddCareHomeRefundPolicySnapshots` | `20261025175720_AddIdempotencyRequests` |
| community | `20261023150000_AddPostDeletionAudit` | `20261025170718_AddCommunityPostViews` |
| families | `20261023131500_AddCareRecords` | `20261025175627_AddIdempotencyRequests` |

The other six heads already matched. Migration used the app's own EF assemblies
over the local cluster (loopback `localhost:5432`, database exactly `SanadDb`),
migrations only — no app startup, no test-user seeding.

## Seed result

| Item | Value |
| --- | --- |
| Target | `localhost:5432` / `SanadDb`, kind `local-demo`, revision `local-demo` |
| Upload root | `src/API/Sanad.API/bin/Release/net10.0/sanad-files` (public) + its `-private` sibling |
| Run tag / instant | `demo1` / `2026-10-25T12:00:00Z` |
| Plan | exit 0, **22 planned / 0 refused**, override warning printed |
| Provision run 1 | exit 0, **all 22 groups provisioned, 0 unresolved** |
| Provision run 2 (replay) | exit 0, all 22 groups, 0 unresolved |

### Row counts

| Stage | Rows | Delta |
| --- | --- | --- |
| after migrate (before seed) | 886 | — |
| after run 1 | **1494** | +608 |
| after run 2 (replay) | **1494** | **+0** |

`Compare-Object` of the full per-table snapshot before/after the replay is
**empty**, and **no table decreased** before → after run 1: the corpus is purely
additive on top of the pre-existing local data. Per-table deltas match the D1
nonce dry run exactly (e.g. `caregivers.caregivers` +6, `caregiver_ratings` +8,
`caregiver_rating_moderation_actions` +1, `community.Posts` +6,
`families.bookings` +10 here vs +22 on the empty nonce because the nonce had no
prior fixtures — the demo *delta* per group is identical).

### Marker / state readback

| Check | Value |
| --- | --- |
| demo identities (`[demo1:<role>]`; 25 with `demo1.` email + 4 phone-only elderly/dependents) | 29 |
| demo posts (marker in title) | 6 (1 anonymous) |
| demo post views (deduplicated) | 4 |
| demo community image rows | 1 |
| legal documents published | 2; section title `DEMO - not legal text` present |
| landing sections | 6 |
| chat messages | 27 |
| care-home visits | 12 |
| demo notifications | 12 (of 53 total; all to demo accounts) |

Media landed through the real `LocalDiskFileStorage` into the local Release upload
root: public `library/<guid>.jpg`; private `care-home-media`,
`care-home-documents`, `library-video`, `subscription-invoices`. Every produced
key was re-opened/confirmed by the writer before the row stored it (run exit 0).

## Boundaries respected

Additive only; the pre-existing local fixture/dev rows are unchanged (no table
decreased). No reset, no deletion, no commit, push, deploy, provider call, clock
change, or VPS access. Replay adds zero rows. The only non-row side effect is the
same as D1: a replay re-writes 1 public + 2 private media files as orphans.

## Handback

- D3 (rehearsal) needs the provisioner published `linux-x64` and run **on the
  VPS** with the seam enabled and verified on the deployed revision, pointed at
  the server's upload root.
- D4 receipt follows D3.

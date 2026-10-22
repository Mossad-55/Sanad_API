# Public legal + visit idempotency (decisions slice P1–P3) — release receipt

Date: 2026-10-22. Status: **Done**. Owner order: 2026-10-22 — "release", after locking all nine decisions with the recommendations ("I love your recommendations… fast and the most efficient path"). Brief: [decisions-and-public-legal-2026-10-22.md](../worker-prompts/decisions-and-public-legal-2026-10-22.md); planning menu: [owner-decision-menu-2026-10-22.md](../owner-decision-menu-2026-10-22.md).

## Committed and pushed

Base `3af81a1` (post team-replies release) → pushed `main` at `1e3d2b021ba9455483b3976a712b76b40c2fef67`. Three release commits (+ the docs closeout commit at the end of this receipt's turn — see final line):

| Commit | Subject | Files |
|---|---|---|
| `da9f0e4` | `feat(public-legal)` | 11 (P1: enum, API base guards, new anonymous controller, validator, 2 test files, 3 docs, Postman) |
| `03fd9e0` | `feat(visit-idempotency)` | 11 (P2: controller, mapper row, handler/command, domain, config, migration pair files-only, snapshot, visits.md, Postman, tests) |
| `1e3d2b0` | `docs(decisions-closeout)` | 3 (P3: partner-contact reply expectation, app-rating policy, plan-copy note) |

Owner edits/tooling set remains uncommitted as always. Gate evidence of record: P1 — Release build 0/0 + focused `~Legal|~HelpCenter|~ApiControllerBase` **123/123**; P2 — Release build 0/0 + focused `~CareHomeVisit` **31/31** (13 pre-existing untouched); both independently reproduced by fresh-session reviewers (P1 GLM ACCEPT incl. the forced `Public=0` sentinel-guard deviation ruling; P2 DeepSeek ACCEPT-WITH-FIXES with one High relational finding — raced catch inside the Serializable guard — fixed with the reviewer's smallest fix and re-accepted on a focused round-2 re-check; Postman variable fix included).

## Package

Published from the pushed revision. Manifest revision `1e3d2b021ba9455483b3976a712b76b40c2fef67` (match); nine required heads — only CareHomes moved (`20261022121500_AddCareHomeVisitIdempotency`), no Identity/Cms change; output **110 entries**, CLEAN scan (no `.env`/dev settings/keys). Archive `deploy/sanad-api-1e3d2b0.tar.gz` — 12,145,899 bytes, SHA-256 `afcd17b660cd4fc8056365d7153d4b4609874359b40c665c4db7f058498f6ab2`; transfer SHA-256 matched.

## Target, backup, cutover

Production VPS `72.62.92.144:8091`, service `sanad-api.service`, `/root/sanad-api`, effective DB identity `SanadDb` (via `/etc/sanad/sanad.env`, host-side only). Pre-cutover live state: revision `153ad158…`, Production, reachable, CareHomes applied head = `20261020210910_AddCareHomeRatingModeration` (verified both via `/deployment/info` and directly from `care_homes."__EFMigrationsHistory"`). Release directory `/root/sanad-releases/decisions-public-legal-visit-idem-1e3d2b0-20261022/`:

- `database-before-decisions.dump` — custom-format `pg_dump` of `SanadDb`, **1,090,645 bytes**, SHA-256 `62764412812eaf82a37ea834e24aa716c27bb84a33e59608d409955866f579ba`, `pg_restore --list` **816 TOC entries**;
- `app-before.tar.gz` — 12,141,605 bytes of the prior binary directory;
- `config-fingerprints-before/after.txt` — env file, systemd unit, both `appsettings*.json` hashed before/after: **identical**; both appsettings preserved during overlay.

Cutover: stop → overlay 110 files (excluding `appsettings*.json`) → start. Post-restart `active`, `NRestarts=0`.

## Migration (startup policy, evidenced in the captured startup log + direct DB reads)

Exactly one moved: **CareHomes `20261022121500_AddCareHomeVisitIdempotency`** ("1 pending" → "Applying migration"); the other eight contexts 0 pending. Schema verified at the database: `care_homes.visits.idempotency_key` (`character varying`, nullable) + unique index `IX_visits_family_id_idempotency_key` present; CareHomes history head = `20261022121500`.

## Deployment verification (verifier exit 0, via loopback SSH tunnel; public HTTP is ISP-redirected)

`Verify-SanadDeployment.ps1` with the pushed manifest + captured startup log (67,535 bytes): remote revision **matches** `1e3d2b0…` on Production; required/applied **9/9 match**; startup log evidences all nine context checks; anonymous smoke `GET /api/v1/lookups/services` **200**.

## Feature smokes (live, on-host loopback, single attempts, no mutation)

| Check | Result |
|---|---|
| `GET /api/v1/public/legal/PrivacyPolicy` (named type) | **404 `Cms.Legal.NotPublished`** — correct documented behavior: no Public-audience document has been authored/published yet; **publishing the first Public doc in the CMS legal admin is the go-live action** for the new public pages |
| `GET /api/v1/public/legal/1` (numeric type) | **404**, same code |
| `GET /api/v1/public/legal/Bogus` (malformed type) | **400 `Cms.Content.InvalidDocumentType`** |
| `GET /api/v1/admin/legal-documents` anonymous | **401** |
| `POST /api/v1/family/care-home-visits` anonymous | **401** (no mutation; see limitation below) |

No rows were written by these probes (all reads/denials); no verification artifacts created this time.

## Limitations and follow-ups

- **Visit idempotency was not exercised live end-to-end** — it requires a family JWT, and no production account credentials are used, consistent with every prior receipt. Its raced-path correctness on real PostgreSQL rests on the review-verified design (out-of-guard unique-violation handling; the guard's rollback semantics confirmed in code), the additive migration's applied schema, and the 31-test focused gate; the unique index is applied now, so the first live family write with a key exercises it.
- **Publishing a Public document is a Content/admin action, not code**: ready when Content wants it — create (or set a published doc's audience to) Public for Privacy Policy and Terms & Conditions in `api/v1/admin/legal-documents` (SuperAdmin/ContentAdmin); it becomes readable by anyone immediately (the admin doc carries this go-live warning).
- Decision follow-ups outside this release: MOB-08 five display strings (mobile applies), MOB-12/plan AR/EN copy + decision-7 Arabic mirror (one Content pass), MOB-10/11 + eligible-residents + `subscriptionCardEnrollmentAvailable` (mobile adoption), Q13/WEB-15 (frontend team).
- Remaining deferred: supplier card visibility change (needs a concrete frontend field list); per-flow payment signals (revisit only on business need).

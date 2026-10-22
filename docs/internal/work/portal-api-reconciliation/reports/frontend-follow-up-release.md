# Frontend/mobile follow-up (T1–T11) — release receipt

Date: 2026-10-22. Status: **Done**. Owner order: 2026-10-22 single go/no-go ("GO", with D1 = whole-file/last-commit shared-file policy, D2 = keep the Cms snapshot drift repair inside `feat(t11-site-settings)`, D3 = commit the brief set only; `README.md` and `docs/internal/governance/AGENTS.md` left uncommitted).

## Committed and pushed

Base `4fa7c77e3544abb0650aa5baf6c24358a1b48654` → pushed `main` at `82b7af65d7b4f4ae1c01a45fa3ad5dfe68864a1c`. Twelve commits:

| Commit | Subject |
|---|---|
| `fcd9769` | `feat(t1-payment-availability)` — 8 files |
| `1454394` | `feat(t2-resident-eligibility)` — 4 files |
| `76895c3` | `feat(t3-audience-resolution)` — 14 files |
| `a1f6cba` | `feat(t4-admin-enrichment)` — 10 files |
| `447b434` | `feat(t5-mobile-reads)` — 19 files |
| `0afa870` | `docs(t6-doc-restorations)` — 4 files (incl. the T6 adapter guard) |
| `7410092` | `feat(t7-community-blocks)` — 18 files, migration files only |
| `dff1195` | `feat(t8-cms-svg)` — 9 files, **no migration** |
| `8e48b7d` | `feat(t9-partner-contact)` — 21 files, migration files only |
| `18e4c34` | `feat(t10-subscription-copy)` — 19 files, migration files only |
| `3c9bf26` | `feat(t11-site-settings)` — 21 files, migration files only |
| `82b7af6` | `docs(phase-status)` — brief, intake artifacts, two team reports, release plan |

148 task files committed; the owner edits/tooling set (`.codex/**`, `.opencode*/**`, `workflow-audit/**`, build/test logs, `README.md`, `docs/internal/governance/AGENTS.md`, `tools/opencode_bridge/opencode_bridge.py`) remains uncommitted in the working tree, untouched. Final strict build of the committed tree: `dotnet build Sanad.slnx -c Release --no-incremental` → **0 warnings / 0 errors**, exit 0.

Per-task gate evidence is in the [active brief](../worker-prompts/frontend-follow-up-2026-10-21.md); T9-B (public legal) was **not** delivered — deferred pending the WEB-08 Product/Content answer.

## Package

Published from the pushed revision by the standard publisher. Manifest revision `82b7af65d7b4f4ae1c01a45fa3ad5dfe68864a1c`; nine required migration heads (five unchanged, four new). Archive `deploy/sanad-api-82b7af6.tar.gz` — **119 entries**, 12,137,236 bytes, SHA-256 `4ba9eaf22064064fa2fffd2e2570cb5988eb5fe9ae322f6a691c7ab300b6f240`; archive listing contains no `.env`, development settings, keys or certificates.

## Target, backup, cutover

Production VPS `72.62.92.144:8091`, service `sanad-api.service`, `/root/sanad-api`. Pre-cutover the live service was active, Production, database reachable, revision `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`, all nine applied heads matching. Release directory `/root/sanad-releases/frontend-82b7af6-20261022/`:

- `database-before-frontend.dump` — custom-format `pg_dump`, **736,626 bytes**, SHA-256 `d534c0c5087c7e56eb739b37e1f0849ed52eab2f3fb28f5a1fccdb811faee90a`, `pg_restore --list` **803 TOC entries**;
- `app-before.tar.gz` — 12,055,945 bytes of the prior binary directory;
- `config-fingerprints-before/after.txt` — service unit, environment file and both `appsettings*.json` hashed before and after: **identical** (CONFIG_UNCHANGED).

Transfer SHA-256 matched the build machine. Cutover: stop → overlay published files (excluding `appsettings*.json`; both preserved) → start. Post-restart `service=active`, `NRestarts=0`.

## Migrations (applied by the production startup policy, evidenced in the startup log)

Exactly four moved, one per context, all others zero:

| Context | Applied |
|---|---|
| Identity | `20261021205122_AddPartnerContacts` |
| Cms | `20261022060548_AddSiteSettings` |
| Families | `20261022044106_AddSubscriptionPlanVersionCopy` |
| Community | `20261021192605_AddCommunityAuthorBlocks` |

9 context checks (9/9 pending/applied evidence), all completing cleanly before traffic acceptance.

## Deployment verification (verifier exit 0, via loopback SSH tunnel; public HTTP is ISP-redirected)

`Verify-SanadDeployment.ps1`: remote revision **matches** `82b7af65…` on Production; required/applied migrations **9/9 match**; startup log carries all nine context checks; anonymous smoke `GET /api/v1/lookups/services` HTTP 200.

## Feature smokes (live, on-host loopback)

| Check | Result |
|---|---|
| `GET /api/v1/site-settings` (T11 public, empty store) | **200** `{"navbarLinks":[],"seo":null}` — documented honest empty shape |
| `GET /api/v1/admin/site-settings` anonymous | **401** |
| `PUT /api/v1/admin/site-settings` anonymous | **401** |
| `POST /api/v1/community/blocks` anonymous (T7 surface) | **401**, no mutation |
| `POST /api/v1/partner-contact` invalid (T9-A) | **400** validation, no write |
| `POST /api/v1/partner-contact` valid, key `t9-release-verify-82b7af6-001` | **200** receipt `{"contactId":{"value":"01a162b3-29f2-7b8b-b7e7-c7ec4d1c9f1f"},"submittedOnUtc":"2026-10-22T08:21:11.2599829Z"}` — documented shape (exactly **one** support-inbox row created, self-labeled as a verification artifact, safe to archive) |
| duplicate same key + same content | **200**, same `contactId` (timestamp serialized with the persisted microsecond precision vs the in-memory 100-ns ticks — same instant; the E07-documented representation nuance) |
| same key, different content | **409 `Identity.PartnerContact.IdempotencyConflict`** — the corrected race contract works live |
| `GET /api/v1/family/subscriptions/plans` / `GET /api/v1/admin/subscriptions/plans` anonymous | **401** both (family/admin surfaces; the bilingual members' live authenticated shape is unit-tested only, per the brief's recorded InMemory limitation) |

Not exercised live: authenticated flows (no production administrator/account credentials were used, consistent with prior receipts), the Landing SVG upload path, admin site-settings authoring, the community block write path. The live OpenAPI and the verifier's revision/head checks cover route publication.

## Limitations and follow-ups

- All unit evidence remains EF-InMemory level; the relational single-row check (`ck_site_settings_single_row`) and the T9-A real-provider race were exercised only through behavior contracts above.
- The stale consumer report `reports/frontend-team-follow-up-2026-10-21.md` (line 38, "SVG not yet available") is historical and was not edited — **a correction notice should be forwarded to the mobile/frontend team** with this receipt: sanitized SVG upload for the **landing** folder is now available (T8), and site-wide editable navbar/SEO settings are available (T11).
- One support-inbox row (`Release Verification (Mastermind)`) is retained in Production; archive at will, never repair historical data by script.

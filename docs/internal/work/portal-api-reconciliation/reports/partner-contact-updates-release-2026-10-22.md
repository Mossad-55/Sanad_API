# Partner-contact optional fields — release receipt

Date: 2026-10-22. Status: **Done**. Owner order: 2026-10-22 — "I send the 2 reports… Also I authorize the release" (team-replies follow-up brief, all four rounds R1–R4 accepted with fresh-session independent reviews before this release).

## Committed and pushed

Base `6e9bba7f5ff90ffb7c7c742ee431aa2c03a1c664` → pushed `main` at `153ad158349f4b6f942ab979f8f2a8d8c9195d99`. Three commits:

| Commit | Subject | Files |
|---|---|---|
| `27b36eb` | `feat(partner-contact-optional-fields)` | 12 (code + tests + migration files only) |
| `5fd2ce5` | `docs(consumer-guides)` | 6 (docs + Postman) |
| `153ad15` | `docs(phase-status)` | 3 (brief, team response, handoff) |

The owner edits/tooling set (`.codex/**`, `README.md`, `docs/internal/governance/AGENTS.md`, `tools/opencode_bridge/**`, build/test/log files) remains uncommitted in the working tree, exactly as in the prior release. Gate evidence of record (rerun after the last accepted review fix): strict `dotnet build Sanad.slnx -c Release --no-incremental` **0 warnings / 0 errors**; focused `FullyQualifiedName~PartnerContact` **30/30** (pre-R3 baseline independently corrected to 21 — the old "64" was a combined legal+partner filter count).

## Package

Published from the pushed revision by `Publish-SanadApi.ps1 -Configuration Release`. Manifest revision `153ad158349f4b6f942ab979f8f2a8d8c9195d99` (match); nine required migration heads, only Identity moved (`20261022094500_AddPartnerContactOptionalFields`); output **110 entries**, no `.env`, development settings, keys or certificates. Archive `deploy/sanad-api-153ad15.tar.gz` — 12,141,361 bytes, SHA-256 `d170610b9ea8bd8f1b43ace69218e92748ccb414f23dafd36ceb93d8b4f4eb8c`; transfer SHA-256 matched on the target.

## Target, backups, cutover

Production VPS `72.62.92.144:8091`, service `sanad-api.service`, workdir `/root/sanad-api`, effective database identity **`SanadDb`** (read from `/etc/sanad/sanad.env`, which is host-side only), Production, DB reachable. Pre-cutover live state verified: revision `82b7af65…`, required = applied 9/9, Identity head at the verified predecessor `20261021205122_AddPartnerContacts` (also read from `identity."__EFMigrationsHistory"` directly).

Release directory `/root/sanad-releases/partner-contact-updates-153ad15-20261022/`:

- `database-before-partner-contact-updates.dump` — custom-format `pg_dump` of `SanadDb`, **1,090,224 bytes**, SHA-256 `b49c89d33deb4cce9a0a8d38a26c0d48f7c3dc2e5eb68c8af3f80609da4ae36b`, `pg_restore --list` **816 TOC entries**;
- `app-before.tar.gz` — 12,135,304 bytes of the prior binary directory;
- `config-fingerprints-before/after.txt` — `/etc/sanad/sanad.env`, the systemd unit and both `/root/sanad-api/appsettings*.json` hashed before and after: **identical** (CONFIG FINGERPRINTS IDENTICAL); both `appsettings*.json` preserved during the overlay.

Cutover: stop → overlay the 110 published application files (excluding `appsettings*.json`) → start. Post-restart `service=active`, `NRestarts=0`. (One operational note: the fingerprint-diff aborted once on its own header comment line after the overlay but **before** `start`; the hash lines were identical, and the corrected comparison confirmed it before the service was started.)

## Migration (applied by the production startup policy, evidenced in the captured startup log)

Exactly one moved: **Identity `20261022094500_AddPartnerContactOptionalFields`** — "1 pending migration(s)" → "Applying migration" → "1 migration(s) applied". All other eight contexts: 0 pending. Schema verified at the database: `identity.partner_contacts` now carries trailing nullable `city`, `company_type`, `phone` (all `character varying`, `nullable=YES`) — legacy rows read `NULL`, as pinned by the review tests.

## Deployment verification (verifier exit 0, via loopback SSH tunnel; public HTTP is ISP-redirected)

`Verify-SanadDeployment.ps1` with the pushed manifest and the captured startup log (660 lines): remote revision **matches** `153ad158…` on Production; required/applied migrations **9/9 match**; startup log evidences all nine context checks; anonymous smoke `GET /api/v1/lookups/services` **HTTP 200**.

## Feature smokes (live, on-host loopback, single attempts)

| Check | Result |
|---|---|
| `POST /api/v1/partner-contact` valid, **incl. new fields** (`phone "+20 100 000 0099"`, `companyType "Clinic"`, `city "Cairo"`), key `r3-release-verify-153ad15-001` | **200** receipt `{"contactId":{"value":"01a1630a-b47f-7701-acf6-240c02c9f69a"},"submittedOnUtc":"2026-10-22T09:56:48.3777807Z"}` |
| duplicate same key + same content | **200**, **same `contactId`** (idempotent receipt) |
| same key, `city "Cairo"→"Alexandria"` (new field alone differs) | **409 `Identity.PartnerContact.IdempotencyConflict`** — the new-field conflict contract works live |
| invalid phone shape (`12+34` — interior `+`, the F2-tightened pattern) | **400 `Api.Validation.Failed`** with per-property error `Phone: 'Phone' is not in the correct format.` — no write |
| `GET /api/v1/admin/support-tickets` anonymous | **401** (admin inbox surface protected) |

One self-labeled support-inbox row (`Release Verification (Mastermind)`) was created by check 1 — a verification artifact, safe to archive, never to repair by script.

## Limitations and follow-ups

- Authenticated admin reads of the new fields (support inbox list/detail showing `phone`/`companyType`/`city` for real submissions) were not exercised live — no production administrator credentials were used, consistent with prior receipts; the read-path behavior is unit-tested (including the legacy-row nulls) and the OpenAPI/revision publication covers the routes.
- The `http://` local scheme (no TLS behind the local reverse proxy path) is unchanged from the prior receipt's environment; the public path remains as operated before this release.
- The team response file's WEB-10 entry was amended post-send to "delivered" with this revision; a short amendment note was added to its header — forward the WEB-10 line (or re-send the file) to the frontend team with this receipt.
- Still deferred (unchanged): WEB-08/T9-B public legal, MOB-15 per-flow payment signal, supplier card visibility, contact reply promise, app-feedback policy, authored AR/EN copy; Q13 fifth Admin screen name and WEB-15 fresh frontend evidence remain with the teams.

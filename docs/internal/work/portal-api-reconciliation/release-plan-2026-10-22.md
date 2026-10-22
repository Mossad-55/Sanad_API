# Release Plan — Frontend/Mobile Follow-up (T1–T11)

**Prepared:** 2026-10-22 · **Status: EXECUTED the same day** — go/no-go received (GO; D1 whole-file/last-commit, D2 drift repair inside T11, D3 brief set only); see the [release receipt](reports/frontend-follow-up-release.md). This file is retained as the approved plan of record.

## 1. What is being released

All approved implementation from the locked brief:

| Phase | Commit (proposed) | Evidence of record |
|---|---|---|
| T1 payment availability | `feat(t1-payment-availability)` | 157/157 |
| T2 resident eligibility | `feat(t2-resident-eligibility)` | 13/13 |
| T3 audience resolution | `feat(t3-audience-resolution)` | 90/90 |
| T4 admin enrichment | `feat(t4-admin-enrichment)` | 56/56 |
| T5 mobile reads | `feat(t5-mobile-reads)` | 49/49 |
| T6 doc restorations | `docs(t6-doc-restorations)` | 17/17 (offline) |
| T7 community blocks | `feat(t7-community-blocks)` | 65 (T7) / 141 combined |
| T8 CMS SVG sanitize+gate | `feat(t8-cms-svg)` | 76 (T8) / 141 combined — **no migration** |
| T9-A partner contact | `feat(t9-partner-contact)` | 64/64 |
| T10 subscription bilingual copy | `feat(t10-subscription-copy)` | 48/48 |
| T11 navbar/SEO site settings | `feat(t11-site-settings)` | 18/18 (after accepted fix) |

**Deferred (NOT in this release):** T9-B public legal content — blocked on the WEB-08 Product/Content answer (`LegalAudience` has no public member; no audience is guessed).

**Base:** HEAD `4fa7c77e3544abb0650aa5baf6c24358a1b48654` (production currently at revision `042a7fd4dd898c2c93579335fe34dd1e61a4d2c5`).

## 2. Commit sequence and file ownership

Full per-commit file lists: see the Mastermind attribution of 2026-10-22 (105 modified + 43 new = 148 task files). Shared files (14) are carried **whole in the last task's commit** that touches them; consequence: 5 code files (`GetPostsQuery.cs`, `CommunityDbContext.cs`, `AdminSupportTicketsController.cs`, `CommunityFeedTests.cs`, shared Postman JSONs) leave earlier commits not independently compilable. **Decision 2 below.**

Commit order 1→11 as tabled above, then:

- `docs(phase-status)` — active brief + the 2026-10-20 contract/clarifications + the two 2026-10-21 team reports (the brief links to these; committing them together avoids dangling links). Optional per Decision 3.
- `docs:` closeout — parent-owned `docs/internal/Mastermind_Handoff.md` checkpoint update (written by Mastermind at execution time, reflecting final states and revisions).

**Explicitly NOT committed (owner edits/tooling — preserved in the working tree):**
`.codex/**`, `.opencode/**`, `.opencode-audit-tmp/**`, `workflow-audit/**`, `tools/opencode_bridge/opencode_bridge.py`, `buildout*.txt`, `testout*.txt`, `testout_e06.txt`, `migout.txt`, `e07-library-smoke.ps1`, `README.md`, `docs/internal/governance/AGENTS.md`.
(Note: `tools/Invoke-E07LocalHttpVerification.ps1` and `Invoke-E08LocalHttpVerification.ps1` ARE committed — they are T6 deliverables, not cruft.)
If you want `README.md` / `docs/internal/governance/AGENTS.md` included, say so and they get a separate `docs(workflow-policy)` commit (see Decision 3).

## 3. Owner decisions required (in the go/no-go)

| # | Decision | Options |
|---|---|---|
| D1 | Shared-file policy | **A (recommended):** whole-file-in-last-commit — simple, final tree identical; intermediate commits not all buildable. **B:** hunk-split the 5 overlapping code files so every commit builds (slower, more error-prone) |
| D2 | CMS snapshot drift repair (the T11 snapshot also re-syncs `FamilyHomeSection` columns missed at HEAD) | **A (recommended):** keep inside `feat(t11-site-settings)` — migration files unchanged, applied schema unaffected. **B:** separate `fix(cms-snapshot-drift)` commit for a clean audit trail |
| D3 | Planning artifacts & owner files | **A (recommended):** commit `docs(phase-status)` (brief + contract + 2 reports) as listed; leave `README.md` + governance `AGENTS.md` uncommitted. **B:** also commit README + governance file in `docs(workflow-policy)`. **C:** commit none of the planning artifacts |

## 4. Database migrations — files today, applied only at release

None applied yet. At release, **after a pre-cutover backup**, in chronological order (four independent DbContexts, order not technically required — chronological is audit convention):

| Order | Migration | Module | Back-breaking risk |
|---|---|---|---|
| 1 | `20261021192605_AddCommunityAuthorBlocks` | Community | Additive table only — none |
| 2 | `20261021205122_AddPartnerContacts` | Identity | Additive table only — none |
| 3 | `20261022044106_AddSubscriptionPlanVersionCopy` | Families | Six additive nullable columns — none |
| 4 | `20261022060548_AddSiteSettings` | Cms | New table (with single-row check) — none |

All four are additive; each gets its own receipt (name, timestamp, exit status). Never `database update` outside this authorized window; never reset/delete local fixtures.

## 5. Execution sequence after GO

1. **Pre-cutover backup** of the production `SanadDb` (house practice before any migration apply).
2. **Stage + commit** the 11 feature commits in order (D1/D2 applied), then `docs(phase-status)` per D3, then the parent writes + commits `docs:` `Mastermind_Handoff.md` closeout.
3. **Push** to the remote; report pushed SHAs.
4. **Deploy** via the same procedure as prior waves (target host `72.62.92.144:8091`); report the resulting revision SHA as the receipt.
5. **Apply the 4 migrations** against production with per-migration receipts (as authorized in this document; only after the deploy's build uses them).
6. **Post-deploy verification** (read-only / harmless probes):
   - `GET /api/v1/site-settings` → `200 { navbarLinks: [], seo: null }` (empty store)
   - `POST /api/v1/support/contact` (partner contact) once → 200 receipt shape; a duplicate with same key → same receipt; a differing-payload retry → 409 (bounded, single attempts only)
   - Family subscription plan catalog shows new bilingual fields as `null`-defaults for legacy plans
   - Community block endpoints accept/reject per contract (one probe each)
   - Landing admin SVG upload to `landing` folder accepted; to another folder rejected (one probe)
   - Admin `GET /api/v1/admin/site-settings` unauthorized without Cms policy; anonymous admin PUT rejected
7. **Report closeout:** revision SHA, migration receipts, verification results — then surface the **stale report correction**: `reports/frontend-team-follow-up-2026-10-21.md:38` says "SVG not yet available", which is now false; forward a correction notice with the release.

## 6. Known residual limitations (disclosed, accepted)

- All unit-level (EF InMemory); no relational/runtime evidence pre-release — deploy verification (step 6) is the first live exercise.
- T11: pipeline/validator integration tests deferred; duplicate-key → 409 covered at mapping level only.
- T9-A: rate-limit is application-layer; behind a non-XFF-overwriting proxy, address rotation can evade it (deployment caveat).
- Unrepaired known baseline: `PortalRemainingResponseContractTests.NestedResponseRefs_ResolveToActualShapes` lives in an **unmodified** file (Owner dispute re `checkoutOnUtc`) — unaffected by this release, remains unrepaired per instruction.

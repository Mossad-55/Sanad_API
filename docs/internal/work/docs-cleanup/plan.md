# Docs separation & hardening — frozen plan (v1, read-only, no implementation)

Owner order: end the nightmare — separate OUR work from THEIR work, enforce
the structure, clean conflicts, detail the guides. Nothing here authorizes
deletion beyond what is listed in §5, and every move keeps git history
(`git mv`) plus a redirect note at the old path for one release cycle is
NOT used (docs have no runtime URLs) — instead every moved file leaves its
references updated in the same commit, verified by link check.

## 0. The rule (constitution — all future work follows this) — ENACTED 2026-10-12

Our work lives in ONE place: `docs/internal/`. Consumer docs live in `app/`
(mobile), `admin/` (flat, one file per area), `auth/` (shared), `postman/`
(`app/`+`admins/`), `care-homes/` (owner hub + README). The 2026-10-12 move
(`chat|governance|notifications|operations|opus|slices|tools|work` +
`Mastermind_Handoff.md` + `Project_Main_Goal.md` → `docs/internal/…`, single
commit, links + script paths verified) enacted this. No new top-level doc
folders without owner approval.

- `docs/app/<area>/` — mobile/family/caregiver/elderly/public/consumer surfaces.
- `docs/admin/*.md` — admin surfaces, one file per area, flat (no new subfolders
  without approval). The stray `docs/admin/elderly/README.md` moves to
  `docs/app/elderly/` (it is consumer content in the wrong home).
- `docs/auth/` — shared auth flows (both audiences). Stays.
- `docs/postman/` — collections mirror the same split (`app/`, `admins/`,
  legacy `care-homes/` stays read-only). One `README.md` index exists.

Our work (planning, tracking, evidence, runbooks) lives ONLY under
`docs/internal/` — enacted, not proposed:

- `docs/internal/work/<package>/` — single root for work packages
  (`plan.md`, `tasks.md`, `handoff.md`, `reports/`). Future packages start here.
- `docs/internal/chat|notifications|opus/` — slice packages (moved, history intact).
- `docs/internal/governance/`, `docs/internal/operations/` (runbooks),
  `docs/internal/slices/` (historical), `docs/internal/tools/` (scripts),
  `docs/internal/Mastermind_Handoff.md`, `docs/internal/Project_Main_Goal.md`.

Forbidden: plans/tasks/reports/handoffs under `app/` or `admin/`; consumer
guides under package dirs; new top-level doc folders without approval.

## 1. Moves (executed 2026-10-12, single commit — this table is the record)

| # | From | To | Status |
|---|---|---|---|
| 0 | `docs/{chat,governance,notifications,operations,opus,slices,tools,work}` + root `Mastermind_Handoff.md` + `Project_Main_Goal.md` | `docs/internal/…` (same leaf names) | Done — `git mv`, links + script `../..`→`../../..` paths fixed, link audit 0 broken |
| 1 | `docs/admin/user-management/plan.md` | `docs/internal/work/admin-users/plan.md` | Done 2026-10-12 — package moved as a unit, self-references fixed |
| 2 | `docs/admin/user-management/tasks.md` | `docs/internal/work/admin-users/tasks.md` | Done — same commit |
| 3 | `docs/admin/user-management/handoff.md` | `docs/internal/work/admin-users/handoff.md` | Done — same commit (leaves `docs/admin/` for the future operator guide `user-management.md`) |
| 4 | `docs/admin/elderly/README.md` | — | Dropped: inspection proved it is an *admin* route index (correctly placed), not consumer content. My original move was a misjudgment, recorded here so it is not re-proposed. |

## 2. Conflict resolutions (no blind deletions)

| # | Conflict | Resolution |
|---|---|---|
| 1 | `docs/care-homes/*` vs `docs/app/families/*` + `docs/admin/care-homes.md` (same endpoints, different eras) | `docs/care-homes/` becomes the single owner/facility guide hub with a banner; family-side duplicates shrink to cross-links; admin stays in `docs/admin/care-homes.md`. Content merges only where verbatim-duplicated; otherwise link, don't copy. |
| 2 | `docs/app/notifications.md` + `docs/admin/notifications.md` | Already correctly split — verify freshness only. |
| 3 | Stale counts/banners in `endpoint-coverage-*`, migration-tasks | Already bannered 2026-10-11 — verify, no further action. |
| 4 | `docs/slices/` leftovers (`documentation/`, `subscriptions/`) | Historical evidence — banner only, never delete (protected by Cleanup_Proposal). |

## 3. Detailing pass (consumer docs must earn their keep)

Every file under `app/`, `admin/`, `auth/` must carry: purpose line, audience +
auth policy, route table (verb + path), request/response shapes, full error-code
table with statuses, and lifecycle/edge notes. Postman examples must match the
guide bodies. Gaps found become a checklist in `tasks.md`, fixed in priority
order (auth → money → care-homes → chat → rest), never by inventing contracts —
ambiguous behavior comes back to the owner.

## 4. READMEs (indexes, not content)

- `docs/app/README.md` (new): per-area table with audience + policy per row.
- `docs/admin/README.md` (new): same for admin surfaces.
- Existing `docs/README.md`, `docs/postman/README.md` verified and linked.

## 5. Deletions (exact list — nothing else gets deleted)

1. Nothing tracked is deleted in this package. Every conflict above resolves by
   banner, link, or merge — because the approved Cleanup_Proposal protects the
   historical files and blind deletion is what created past loops.
2. If the owner later names exact files, they are deleted only after the
   replacement-verification note is written in `tasks.md`.

## 6. Gates per phase

`git diff --check`; contract-map Postman 0/0; Postman JSON parse all 20;
markdown link check (script-assisted, existing links only — no new tooling);
full unit + arch suites once at closeout (docs-only changes must not break
collectors that parse docs). No API/DB/Bruno runtime. Commit per phase on
`main`, push only on owner order.

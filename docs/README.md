# Ahlena API — documentation index

> **Brand note:** this repository was historically branded **Sanad / سند**. Owner-approved local rename to **Ahlena / أهلنا** implemented 2026-10-31, pending parent review; see `docs/internal/work/ahlena-rebrand-2026-10-31.md`. Slice plans, reports, and handoffs under `docs/internal/` are historical records and may still reference the old brand.

Single map of this `docs/` tree. Start here; do not re-derive it per task.

## Project direction

- [Project main goal](internal/Project_Main_Goal.md) — scope, priorities, slice index, boundaries.
- [Mastermind handoff](internal/Mastermind_Handoff.md) — current state, last gates, next action.
- [Governance](internal/governance/AGENTS.md) — worker roles, verification rules, safety rails.

## Slice execution packages (frozen contracts + checklists + reports)

| Slice | Plan | Tasks | Reports | Guides |
|---|---|---|---|---|
| Chat | [chat/plan.md](internal/chat/plan.md) | [chat/tasks.md](internal/chat/tasks.md) | [chat/reports/](internal/chat/reports/) | [app/chat/](app/chat/) |
| Notifications | [notifications/plan.md](internal/notifications/plan.md) | [notifications/tasks.md](internal/notifications/tasks.md) | [notifications/reports/](internal/notifications/reports/) | [app/notifications.md](app/notifications.md) |
| Opus | [opus/plan.md](internal/opus/plan.md) | [opus/tasks.md](internal/opus/tasks.md) | [opus/reports/](internal/opus/reports/) | [app/library/](app/library/), [app/community/](app/community/), [app/caregivers/](app/caregivers/) |
| Care Homes | [operations/care-homes/Care_Homes_Tasks.md](internal/operations/care-homes/Care_Homes_Tasks.md) | same | task evidence inline | [care-homes/](care-homes/) |

## API references for frontend

- [Dated frontend reports and handoffs](frontend-handoffs/README.md) — separate Admin/App audiences, phase/date/topic, corrections and links to implementation/deployment evidence. Execution status stays in each internal slice plan.
- Feature guides: [app/](app/) (family/caregiver/elderly views) and [admin/](admin/) (admin views).
- Postman: [postman/](postman/) — 24 collections, every controller route mapped (see mapping check below). `local-fixtures.bru` is generated and never committed.
- OpenAPI is runtime-generated (`/openapi/v1.json`); no committed spec by design. Controllers carry `ProducesResponseType` annotations.

## Verification tooling (living source of truth)

- `docs/internal/tools/Verify-ApiContractMapping.ps1` — controllers ↔ Postman mapping. Run before every merge; Postman must stay 0 missing / 0 orphan. Known limitation: the script's exit code covers only the Postman missing/orphan counts — Bruno checks are intentionally excluded from this gate (owner direction), so a clean exit is not a Bruno coverage claim.
- `docs/internal/tools/Publish-AhlenaApi.ps1` — Release packaging + deployment manifest (see [operations/publish.md](internal/operations/publish.md), trigger word: `publish`).
- `docs/internal/tools/Verify-AhlenaDeployment.ps1` — post-start manifest + smoke verification (see [operations/deploy-runbook.md](internal/operations/deploy-runbook.md)).
- `tools/Start-LocalBrunoFixtureApi.ps1` (repo-root `tools/`) - guarded disposable-target API launcher for Bruno checks.
- `docs/internal/tools/Invoke-BrunoPreflight.ps1` — offline Bruno selection checks.

## Operations runbooks

- [operations/bruno-failure-first.md](internal/operations/bruno-failure-first.md) — stateful check discipline, first-failure cards.
- [operations/deployment.md](internal/operations/deployment.md) — package/verify flow and migration policy.
- [operations/publish.md](internal/operations/publish.md) — the `publish` trigger contract.
- [operations/deploy-runbook.md](internal/operations/deploy-runbook.md) — environment, secrets, and release steps.
- [operations/migrations.md](internal/operations/migrations.md) — migration policy.
- [operations/security.md](internal/operations/security.md) — security rules.

## Historical (read-only, do not resume)

- `Mastermind_Handoff.md` dated checkpoints, `operations/care-homes/` task logs, `slices/documentation/Documentation_Migration_Tasks.md`, frozen coverage snapshots in `operations/endpoint-coverage-*.md` (see banners on each file). Deletion needs a separate exact-list confirmation.

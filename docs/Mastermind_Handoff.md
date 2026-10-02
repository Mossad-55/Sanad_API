# Mastermind handoff

Updated: 2026-10-02. Active role: execution mastermind for the owner-approved Care homes slice.

## Current state

- Goal: deliver the Care homes backend slice from the supplied operator/Family UI and owner-confirmed requirements.
- Completed: planning intake and cleanup; owner-resolved shared percentage fee/tax basis and manual payout eligibility/recovery; caregiver review permission correction and Family caregiver ratings/top-10 with unit/Bruno coverage and synchronized docs/Postman; Care Homes onboarding/Admin lifecycle; HC-TASK-014 license expiry alerts and durable email outbox; HC-TASK-020 bounded inventory, maintenance, owner/Admin APIs, derived availability seam, docs, Postman, and Bruno coverage. All Care Homes work remains on linked `main` worktree `D:\Sanad_API\.codex\worktrees\care-homes-main`.
- Verification checkpoint: after network-enabled NuGet restore, main-worktree solution build passed with 0 warnings/errors; focused tests 57/57; full suite Architecture 1 + Unit 2,145 passed. The owner-authorized disposable DB was dropped by the owner and rebuilt by the guarded local fixture script; one owner facility was created through the owner API. Inventory Bruno passed 30/30 assertions (30 passed, 1 unrelated Wellness Tips parser skip). API and generated fixture env/image were cleaned. No production/provider action occurred.
- HC-TASK-020 remains In Progress only because `ICareHomeOccupancyProvider` awaits hold/stay data from HC-TASK-032/034; do not implement booking creation in this bounded inventory task. The wider Care Homes slice still includes occupancy/booking, discovery, Family stays/visits/ratings, shared percentage fee configuration/usage, manual payout ledger, notifications, and remaining endpoint coverage.
- Latest static contract mapping: 339 controller route-method signatures, 385 Postman API requests, 0 missing/orphan Postman mappings, 1,111 Bruno API requests, and 0 missing/orphan Bruno mappings. `git diff --check` passed with CRLF replacement notices.
- Authoritative decisions: [Care homes decisions](operations/care-homes/Care_Homes_Decisions.md).
- Task status: [Care homes tasks](operations/care-homes/Care_Homes_Tasks.md).
- UI evidence: [UI review](operations/care-homes/UI_Review.md).
- Execution guidance: [Care homes execution handoff](operations/care-homes/Execution_Handoff.md).
- Final decisions: separate Admin-configured percentage fee and tax, each on payment base price, shared across caregiver/Care homes/Family subscription payments; Family refunds apply proportionally to its platform fee; manual bank payout only after completed stay with later refunds recorded as facility balance owed; Admin may approve genuinely non-expiring documents; SMS verification OTP allowed; SuperAdmin/SupportAdmin may adjudicate check-in with evidence/effective time. Do not repeat resolved questions.
- Exactly one next action: after the owner deploys this pushed main checkpoint and reports the result, begin HC-TASK-021 with the mandated scout role in the same main worktree. Do not repeat HC-TASK-020 Bruno (30/30 passed); run Bruno only when a concrete code/fixture change invalidates that evidence. Preserve unrelated worktree changes. The owner handles deployment; no production/provider action is authorized.

## Scope

Backend only. Include caregiver review permission correction now (SuperAdmin/SupportAdmin, not ContentAdmin), Family caregiver ratings/top-10, Admin APIs and in-app/email events; no SMS notifications. Do not expand unrelated caregiver booking work or create future slice backlogs. Priority direction only: Care homes → Chat → Notifications → application UI reconciliation.

Read [governance](governance/AGENTS.md) and preserve the worker order and gates. The owner approved this slice and answered its remaining business contracts. Continue the current slice only; do not re-ask completed intake.

## Safety and cleanup

During the earlier documentation cleanup, no product code, database/provider actions, migrations or execution-phase changes occurred; that cleanup deleted only its exact 118-file manifest. The later owner-approved caregiver-review and caregiver-rating implementation is recorded in the active task evidence. Preserve the existing dirty Elderly audit, VPS environment, root fixture helper, UI, and protected fixture images.

The earlier 103-file generated project backlog is superseded, not executable. [Cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the confirmed 118-file cleanup and retained-reference checks. Preserve current source/evidence and unrelated changes; the older legacy deletion list was not used automatically.

Owner clarified cleanup must include irrelevant legacy workflows, documentation and handoffs, not just generated drafts. The reconciled [cleanup proposal](operations/care-homes/Cleanup_Proposal.md) records the completed exact 118-file deletion and replacement mapping: 102 withdrawn generated task drafts plus 16 unchanged legacy sources. Current/modified audits, coverage evidence, the modified fixture helper, and all unrelated files were explicitly excluded. Obsolete instructions were removed rather than renamed or archived as competing active sources.

## Retained historical migration evidence

Governance and four scripts have canonical docs locations; root AGENTS.md is the discovery pointer. Earlier script relocation checks passed: 312 route-method signatures, 352 Postman requests, 993 Bruno requests, no missing/orphan mappings; fixture environment restoration verified. These are historical migration checks, not current Care homes implementation results. See retained [migration evidence](slices/documentation/Documentation_Migration_Tasks.md).

## Cleanup checkpoint

Replacement/link review and the confirmed deletion are complete. Retained deployment and mapping references use `docs/tools`; no product code or execution phase changed. This cleanup checkpoint is historical; the active Care homes next action is recorded above.

# Mastermind handoff

Updated: 2026-10-02. Active role: planning mastermind; product implementation has not started.

## Current state

- Goal: deliver the Care homes backend slice from the supplied operator/Family UI and owner-confirmed requirements.
- Completed: inspected 19 operator and 10 Family screens; consolidated all 24 batch answers; prepared one scoped task checklist and execution prompt.
- Active phase: cleanup completed; backend execution remains gated by the approved Care homes plan. All batch answers and five final clarifications are recorded. One documenter prepared the decisions/checklist; mastermind consolidated and checked them. No implementation gates ran.
- Authoritative decisions: [Care homes decisions](care-homes/Care_Homes_Decisions.md).
- Task status: [Care homes tasks](care-homes/Care_Homes_Tasks.md).
- UI evidence: [UI review](care-homes/UI_Review.md).
- Execution prompt: [Care homes execution handoff](care-homes/Execution_Handoff.md).
- Final decisions: percentage platform fees; refund percentage applies to Family fee; manual bank payouts recorded by Admin; Admin may approve genuinely non-expiring documents; SMS verification OTP allowed; SuperAdmin/SupportAdmin may adjudicate check-in with evidence and effective time. Scout maps remaining fee-basis/rounding/payout eligibility conventions; do not repeat resolved intake.
- Exactly one next action: owner hands the prepared Care homes execution prompt to the backend-execution mastermind after reviewing the retained cleanup result.

## Scope

Backend only. Include caregiver review permission correction now (SuperAdmin/SupportAdmin, not ContentAdmin), Family caregiver ratings/top-10, Admin APIs and in-app/email events; no SMS notifications. Do not expand unrelated caregiver booking work or create future slice backlogs. Priority direction only: Care homes → Chat → Notifications → application UI reconciliation.

Read [governance](governance/AGENTS.md) and preserve the worker order and gates. Upon owner approval the executing mastermind can start independent approved tasks and block only tasks depending on unanswered contracts. Before each new product slice, conduct owner application intake; do not re-ask this completed UI inventory.

## Safety and cleanup

No product code, database/provider actions, migrations or execution-phase changes occurred. The confirmed documentation cleanup deleted only the exact 118-file manifest. Preserve the existing dirty Elderly audit, VPS environment, root fixture helper, UI, and protected fixture images.

The earlier 103-file generated project backlog is superseded, not executable. [Cleanup proposal](care-homes/Cleanup_Proposal.md) records the confirmed 118-file cleanup and retained-reference checks. Preserve current source/evidence and unrelated changes; the older legacy deletion list was not used automatically.

Owner clarified cleanup must include irrelevant legacy workflows, documentation and handoffs, not just generated drafts. The reconciled [cleanup proposal](care-homes/Cleanup_Proposal.md) records the completed exact 118-file deletion and replacement mapping: 102 withdrawn generated task drafts plus 16 unchanged legacy sources. Current/modified audits, coverage evidence, the modified fixture helper, and all unrelated files were explicitly excluded. Obsolete instructions were removed rather than renamed or archived as competing active sources.

## Retained historical migration evidence

Governance and four scripts have canonical docs locations; root AGENTS.md is the discovery pointer. Earlier script relocation checks passed: 312 route-method signatures, 352 Postman requests, 993 Bruno requests, no missing/orphan mappings; fixture environment restoration verified. These are historical migration checks, not current Care homes implementation results. See retained [migration evidence](slices/documentation/Documentation_Migration_Tasks.md).

## Cleanup checkpoint

Replacement/link review and the confirmed deletion are complete. Retained deployment and mapping references use `docs/tools`; no product code or execution phase changed. Exactly one next action: owner hands the approved Care homes execution prompt to the backend-execution mastermind; worker order and execution phases remain unchanged.

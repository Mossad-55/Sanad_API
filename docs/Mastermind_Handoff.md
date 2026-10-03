# Mastermind handoff

Updated: 2026-10-02. Active role: execution mastermind for the owner-approved Care homes slice.

## Current state

- Goal: deliver the Care homes backend slice from the supplied operator/Family UI and owner-confirmed requirements.
- Completed: planning intake and cleanup; owner resolved the shared percentage fee/tax basis and manual payout eligibility/recovery; caregiver review permission correction and Family caregiver ratings/top-10 implemented with unit/Bruno coverage and synchronized docs/Postman. Initial Care homes owner onboarding now has a domain aggregate, persistence/migration, owner create/read/draft-save endpoints, focused unit tests, Bruno coverage, and a separate Postman collection/guide.
- Active phase: Care Homes backend implementation. Owner submission and Admin queue/detail/document/private-file/review routes are implemented and verified. HC-TASK-014 daily license alerts and durable email outbox are implemented and verified. HC-TASK-020 inventory migration `20261002150725_AddCareHomeInventory` was generated, inspected, and applied only to the owner-authorized `localhost:5432/SanadBrunoTestDb`; no reset/drop occurred. Its initial stateful Bruno run failed 8/30 assertions after the seeded owner lacked a facility and the inventory error mapper was incomplete; those corrections are authored and reviewer-approved, but the rerun is blocked by unrelated untracked Family source compile errors. The broad slice remains incomplete; occupancy integration, booking, Family discovery/stays/visits/ratings, shared percentage fee configuration/usage, manual payout ledger, notifications, and remaining endpoint coverage are still open.
- Static contract mapping passed after the latest Bruno update: 325 controller route-method signatures, 371 Postman requests, 0 missing/orphan Postman mappings, 1,081 Bruno API requests, and 0 missing/orphan Bruno mappings. `git diff --check` passed with only CRLF replacement notices.
- Authoritative decisions: [Care homes decisions](operations/care-homes/Care_Homes_Decisions.md).
- Task status: [Care homes tasks](operations/care-homes/Care_Homes_Tasks.md).
- UI evidence: [UI review](operations/care-homes/UI_Review.md).
- Execution guidance: [Care homes execution handoff](operations/care-homes/Execution_Handoff.md).
- Final decisions: separate Admin-configured percentage fee and tax, each on payment base price, shared across caregiver/Care homes/Family subscription payments; Family refunds apply proportionally to its platform fee; manual bank payout only after completed stay with later refunds recorded as facility balance owed; Admin may approve genuinely non-expiring documents; SMS verification OTP allowed; SuperAdmin/SupportAdmin may adjudicate check-in with evidence/effective time. Do not repeat resolved questions.
- Exactly one next action: resolve or explicitly authorize handling of the unrelated Family compile blocker, then rerun the corrected HC-020 stateful Bruno collection only on `localhost:5432/SanadBrunoTestDb` with readback and fixture cleanup, followed by build/focused/full verification. No reset/drop, commit/push, deployment, or provider/network action is authorized by this evidence. The inventory migration is already applied to the exact disposable target; booking integration remains with HC-TASK-032/034. Preserve unrelated worktree edits.

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

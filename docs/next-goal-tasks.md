# Next Goal: Notifications/Events and Remaining Elderly Owner Decisions

This is a planning checklist only. The next goal has **not started**. Begin it only after the owner authorizes scope and resolves the business decisions below. No deployment or production-data operation is implied.

## Phase 1 — Owner decisions and event contract

- [ ] **Blocked — Needs Owner Verification** — Decide Welcome placement, Elderly targeting, benefit-tile content/source, and the Welcome-to-OTP/navigation CTA contract.
- [ ] **Blocked — Needs Owner Verification** — Decide field-by-field Support/Admin visibility for Elderly emergency-contact name/relationship/phone, DOB/derived age, photo, detailed address, health notes, and latest assessment.
- [ ] **Blocked — Needs Owner Verification** — Decide dedicated SOS notification preference behavior versus the current help-request preference reuse.
- [ ] **Not started** — Confirm event inventory, recipient eligibility, preference policy, sensitive-data policy, retention, and delivery expectations for each notification class.
- [ ] **Not started** — Carry forward public-audit questions for missed check-in/history semantics, deleted-recipient mapping, physical purge/audit retention, SOS precision/export/escalation, and any required operational-management actions.

## Phase 2 — Push/email delivery architecture

- [ ] **Not started** — Scout existing notification contracts and provider integrations; define an outbox/job boundary, idempotency, retry/backoff, dead-letter/observability, and delivery-status model before implementation.
- [ ] **Not started** — Implement approved push-provider delivery for the agreed event inventory, preserving the durable in-app inbox as the source of truth.
- [ ] **Not started** — Implement approved email-provider delivery only for owner-approved event classes and templates.
- [ ] **Not started** — Add scheduling/reminder behavior only for explicitly approved events and timing rules.
- [ ] **Not started** — Add least-privilege Admin/support delivery inspection and management, if approved; sensitive record reads must remain audited.

## Phase 3 — Verification and release preparation

- [ ] **Not started** — For each bounded slice, use the sequential scout → implementer → test author → reviewer → documenter workflow, one worker at a time, with GPT-6 Luna at medium effort.
- [ ] **Not started** — Add focused tests and Bruno success/failure/auth coverage for every added or behaviorally changed endpoint; run stateful Bruno only against disposable local/test fixtures before commit.
- [ ] **Not started** — Sync API/auth docs, Postman, safe Bruno examples, public audit, and private handoff; update `docs/goal-progress.md` at every task transition.
- [ ] **Not started** — Require clean `dotnet build` and `dotnet test` results with zero warnings; review exact diff and stage only authorized tracked paths before any authorized commit/push.
- [ ] **Not started** — Provide a deployment-readiness assessment and list migrations/operational prerequisites. Deployment remains a separate owner-authorized action.

## Carry-forward facts

- The current goal is closed and pushed at `1b641a83a25fda656fc1380961760ed83ae829d7`.
- Durable in-app notifications, current recipient fan-out, Elderly OTP eligibility, and current Admin operational views are implemented and verified; do not redo them absent a concrete gap.
- For Elderly requests, late-medication alerts, and SOS, current in-app recipients include eligible linked Family members, the assigned caregiver only with an active `Confirmed`/`InProgress` booking (and the applicable active/preference conditions), plus active SupportAdmin. No unassigned caregiver receives alerts merely because a booking is available.
- Elderly request-OTP returns 204 only for an active Elderly-only Identity account with a matching usable profile; unknown and ineligible phones share the approved 404 response before OTP persistence/dispatch.
- Existing owner-verification items remain blockers for changes that would expose those fields or define new behavior. Push/email/provider delivery, outbox/jobs, retries/scheduling, and broader event inventory are deferred to this goal.
- No deployment or production data mutation occurred in the current goal.

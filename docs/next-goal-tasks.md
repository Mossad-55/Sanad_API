# Next Goal: Notifications/Events and Remaining Elderly Owner Decisions

The owner approved the recommendations on 2026-09-28. This goal is now **In progress** and is tracked in `docs/goal-progress.md`; execute strictly one task at a time. No deployment or production-data operation is implied.

## Phase 1 — Owner decisions and event contract

- [x] **Done — Owner approved** — Welcome is Elderly-specific, CMS-managed after language selection and before OTP, with localized headline/benefit tiles and CTA to Elderly OTP; preserve shared splash behavior.
- [x] **Done — Owner approved** — Default-deny operational Admin access to sensitive Elderly profile/contact fields; any exception requires field-specific minimum scope, purpose/reason, and audited read.
- [x] **Done — Owner approved** — SOS uses a dedicated preference; safety-required Family and SupportAdmin alerts are not silently suppressed by general help-request preference; caregiver remains active-booking/eligibility bound.
- [x] **Done — Owner approved** — Event categories/preferences are explicit per event; UTC timestamps and Elderly-local rules govern date-based behavior.
- [x] **Done — Owner approved** — Carry-forward defaults for check-in, medication history, retention/purge, deleted-recipient representation, SOS location/export/escalation/dialing, clinical review/tenant scope, emergency-contact lifecycle, and event-scoped Admin management are recorded in `docs/goal-progress.md`.

## Phase 2 — Push/email delivery architecture

- [x] **Done — Scout existing notification contracts and provider integrations** — The durable inbox is `notifications.notifications`, with per-recipient content/read state and a unique optional idempotency key; it has no channel delivery state, attempts, scheduling, or provider receipts. Event fan-out currently runs synchronously after business-context saves through gateways into the Notifications context, so those writes are not atomic and can leave a committed business event without its alert if notification persistence fails. SMTP exists for identity/support email and SMS Misr is used for OTP; Development implementations are no-ops. No push provider, device-token registration/store, notification outbox, `IHostedService`/`BackgroundService`, or scheduler was found. | Recommended before implementation: agree on provider/device-token lifecycle and transactional outbox ownership. Source/event transactions currently span different module contexts, so the outbox must be committed with each source event (or an explicit recovery/consistency model approved); do not assume a separate Notifications DB outbox is atomic.
- [ ] **Blocked — Implement approved push-provider delivery** — No push provider or device-token lifecycle is selected/configured. Needs owner decision on provider/platform and token registration/retirement contract before safe implementation.
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
